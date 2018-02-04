﻿using System.Linq;

namespace PrtgAPI.PowerShell.Progress
{
    internal partial class ProgressManager
    {
        private void InitializeSelectPipeline()
        {
            //With each record Select-Object sends us, its queue size decreases. We need to record
            //the initial queue state so that we may record our progress between calls to ProcessRecord.
            //As the ProgressManager is torn down between ProcessRecord calls, we save this state
            //in a member variable that will persist during the ProgressManager's cmdlet.
            if (cmdlet.ProgressManagerEx.BlockingSelectPipeline != null)
            {
                var newPipeline = CacheManager.GetSelectPipelineOutput();

                //No point in doing anything; we're in the EndProcessing block anyway
                if (newPipeline == null)
                    return;

                var previousPipeline = new Pipeline(CmdletPipeline.Current, cmdlet.ProgressManagerEx.BlockingSelectPipeline.List);

                if (previousPipeline.List.Contains(newPipeline.Current))
                    SelectPipeline = previousPipeline;
                else
                {
                    var list = previousPipeline.List;
                    list.AddRange(newPipeline.List.Take(previousPipeline.List.Count - 1 > 0 ? previousPipeline.List.Count - 1 : previousPipeline.List.Count));
                    SelectPipeline = new Pipeline(newPipeline.Current, list);
                }
            }
            else
            {
                SelectPipeline = CacheManager.GetSelectPipelineOutput();
            }

            cmdlet.ProgressManagerEx.BlockingSelectPipeline = SelectPipeline;
        }

        private int? GetSelectObjectOperationStraightFromVariableTotalRecords()
        {
            if (Scenario == ProgressScenario.SelectSkipLast && PreviousRecord != null)
            {
                TotalRecords -= upstreamSelectObjectManager.TotalSkipLast;
            }
            else
            {
                if (upstreamSelectObjectManager?.HasSkipLast == true && PreviousRecord == null)
                {
                    TotalRecords = EntirePipeline.List.Count - (upstreamSelectObjectManager.TotalAnySkip);
                }
            }

            return TotalRecords;
        }

        private int? GetSelectObjectOperationFromCmdletFromVariableTotalRecords()
        {
            if (Scenario == ProgressScenario.SelectSkipLast)
                TotalRecords -= upstreamSelectObjectManager.TotalSkipLast;

            return TotalRecords;
        }

        private int GetIncrementProgressFromPipelineTotalRecords(int maxCount)
        {
            if (Scenario == ProgressScenario.SelectSkipLast)
            {
                var previousCmdlet = CacheManager.GetPreviousPrtgCmdlet();

                if (previousCmdlet != null) //todo: what if we had two skiplasts in a row. whats the total now?
                {
                    var previousManager = previousCmdlet.ProgressManager;
                    var total = previousManager.TotalRecords.Value;

                    maxCount = total - upstreamSelectObjectManager.TotalSkipLast;

                    if (upstreamSelectObjectManager.HasSkip)
                        maxCount = maxCount - upstreamSelectObjectManager.TotalSkip;
                }
                else
                {
                    if (PipeFromVariableWithProgress)
                    {
                        if (upstreamSelectObjectManager.HasSkip || upstreamSelectObjectManager.HasSkipLast)
                            maxCount = EntirePipeline.List.Count - upstreamSelectObjectManager.TotalAnySkip;
                    }
                }
            }

            return maxCount;
        }

        public void MaybeCompletePreviousProgress()
        {
            if (!ProgressEnabled)
                return;

            if (upstreamSelectObjectManager != null)
            {
                if (readyParser.Ready())
                {
                    if ((upstreamSelectObjectManager.HasFirst || upstreamSelectObjectManager.HasSkip || upstreamSelectObjectManager.HasIndex) && PreviousRecord != null)
                    {
                        if (PreviousContainsProgress)
                        {
                            //The previous cmdlet can't be writing progress if it's not active
                            CompleteProgress(PreviousRecord, true);
                        }
                        else
                            CompleteProgress();
                    }
                    else
                    {
                        CompleteProgress();
                    }
                }
            }
        }
    }
}
