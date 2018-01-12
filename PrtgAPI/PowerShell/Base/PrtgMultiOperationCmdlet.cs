﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using PrtgAPI.Objects.Shared;

namespace PrtgAPI.PowerShell.Base
{
    /// <summary>
    /// Base class for all cmdlets that perform actions or manipulate multiple objects in a single request against PRTG.
    /// </summary>
    public abstract class PrtgMultiOperationCmdlet : PrtgOperationCmdlet
    {
        /// <summary>
        /// <para type="description">Specifies whether this cmdlet should queue all objects piped to this cmdlet to execute a single
        /// request against PRTG for processing all objects. By default this value is true.</para>
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Batch { get; set; } = SwitchParameter.Present;

        internal List<PrtgObject> objects = new List<PrtgObject>();

        private void QueueObject(PrtgObject obj)
        {
            objects.Add(obj);

            if (ProgressManagerEx.CachedRecord == null)
            {
                if (ProgressManager.ProgressWritten)
                    ProgressManagerEx.CachedRecord = ProgressManager.CurrentRecord;
                else if (ProgressManager.PreviousRecord != null)
                    ProgressManagerEx.CachedRecord = ProgressManager.PreviousRecord;
            }
        }

        /// <summary>
        /// If <see cref="Batch"/> is true, queues the object for processing after all items have been identified. Otherwise, executes this cmdlet's action immediately. 
        /// </summary>
        /// <param name="obj">The object to process.</param>
        /// <param name="activity">The progress activity title to display when queuing the object.</param>
        protected void ExecuteOrQueue(SensorOrDeviceOrGroupOrProbe obj, string activity)
        {
            if (Batch)
                ExecuteQueueOperation(obj, activity);
            else
                PerformSingleOperation();
        }

        private void ExecuteQueueOperation(SensorOrDeviceOrGroupOrProbe obj, string activity)
        {
            ExecuteQueueOperation(obj, activity, $"Queuing {obj.BaseType.ToString().ToLower()} '{obj.Name}'");
        }

        /// <summary>
        /// Queues an object for execution after all objects have been identified.
        /// </summary>
        /// <param name="obj">The object to queue.</param>
        /// <param name="activity">The progress activity title to display.</param>
        /// <param name="progressMessage">The progress message to display.</param>
        protected void ExecuteQueueOperation(PrtgObject obj, string activity, string progressMessage)
        {
            ExecuteOperation(() => QueueObject(obj), activity, progressMessage);
        }
        
        /// <summary>
        /// Executes this cmdlet's action on all queued objects.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="activity">The progress activity title to display.</param>
        /// <param name="progressMessage">The progress message to display.</param>
        /// <param name="complete">Whether progress should be completed after calling this method.<para/>
        /// If the queued objects are to be split over several method calls, this value should only be true on the final call.</param>
        protected void ExecuteMultiOperation(Action action, string activity, string progressMessage, bool complete = true)
        {
            progressMessage += $" ({objects.Count}/{objects.Count})";

            ProgressManager.ProcessMultiOperationProgress(activity, progressMessage);

            action();
            
            if(PrtgSessionState.EnableProgress && complete)
                ProgressManager.CompleteProgress();
        }

        internal string GetMultiTypeListSummary()
        {
            var toProcess = objects;
            var remaining = 0;

            if (objects.Count > 10)
            {
                var amount = 10;

                if (objects.Count == 11)
                    amount = 9;

                toProcess = objects.Take(amount).ToList();
                remaining = objects.Count - amount;
            }

            var grouped = toProcess.Cast<SensorOrDeviceOrGroupOrProbe>().GroupBy(t => t.BaseType).ToList();

            var builder = new StringBuilder();

            for (int i = 0; i < grouped.Count; i++)
            {
                //Add the BaseType (e.g. sensor(s))
                builder.Append(grouped[i].Key.ToString().ToLower());

                if (grouped[i].Count() > 1)
                    builder.Append("s");

                builder.Append(" ");

                var groupItems = grouped[i].ToList();

                //Add each object name (e.g. 'Ping', 'DNS' and 'Uptime'
                for (int j = 0; j < groupItems.Count(); j++)
                {
                    builder.Append($"'{groupItems[j].Name}'");

                    if (j < groupItems.Count - 2)
                        builder.Append(", ");
                    else if (j == groupItems.Count - 2)
                    {
                        //If there's more than 1 group and we're not the last group, OR if there are remaining items, dont say "and"

                        if ((grouped.Count > 1 && i == grouped.Count - 1 || grouped.Count == 1) && remaining == 0)
                            builder.Append(" and ");
                        else
                            builder.Append(", ");
                    }
                        
                }

                //Add a delimiter for the next group based on whether we'll also have to
                //report the number of objects we didn't name afterwards
                if (i < grouped.Count - 2)
                    builder.Append(", ");
                else if (i == grouped.Count - 2)
                    builder.Append(remaining > 0 ? ", " : " and ");
            }

            if (remaining > 0)
                builder.Append($" and {remaining} others");

            return builder.ToString();
        }

        internal string GetListSummary(List<PrtgObject> list = null, Func<PrtgObject, string> descriptor = null)
        {
            if (list == null)
                list = objects;

            if (descriptor == null)
                descriptor = o => $"'{o.Name}'";

            return GetListSummary<PrtgObject>(list, descriptor);
        }

        internal string GetListSummary<T>(List<T> list, Func<T, string> descriptor)
        {
            var toProcess = list;
            var remaining = 0;

            if (list.Count > 10)
            {
                var amount = 10;

                if (list.Count == 11)
                    amount = 9;

                toProcess = list.Take(amount).ToList();
                remaining = list.Count - amount;
            }

            var builder = new StringBuilder();

            for (int i = 0; i < toProcess.Count; i++)
            {
                builder.Append(descriptor(toProcess[i]));

                if (i < toProcess.Count - 2)
                    builder.Append(", ");
                else if (i == toProcess.Count - 2)
                {
                    builder.Append(remaining > 0 ? ", " : " and ");
                }
            }

            if(remaining > 0)
                builder.Append($" and {remaining} others");

            return builder.ToString();
        }

        internal string GetCommonObjectBaseType()
        {
            var baseType = objects.Cast<SensorOrDeviceOrGroupOrProbe>().First().BaseType.ToString().ToLower();

            if (objects.Count > 1)
                baseType += "s";

            return baseType;
        }

        /// <summary>
        /// Provides a one-time, postprocessing functionality for the cmdlet.
        /// </summary>
        protected override void EndProcessing()
        {
            if (objects.Count > 0 && Batch)
            {
                var ids = objects.Select(o => o.Id).ToArray();

                ExecuteWithCoreState(() => PerformMultiOperation(ids));
            }
        }

        /// <summary>
        /// Invokes this cmdlet's action against the current object in the pipeline.
        /// </summary>
        protected abstract void PerformSingleOperation();

        /// <summary>
        /// Invokes this cmdlet's action against all queued items from the pipeline.
        /// </summary>
        /// <param name="ids">The Object IDs of all queued items.</param>
        protected abstract void PerformMultiOperation(int[] ids);
    }
}