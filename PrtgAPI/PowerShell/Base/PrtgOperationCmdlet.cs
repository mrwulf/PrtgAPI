﻿using System;
using System.Linq;
using System.Management.Automation;
using System.Reflection;

namespace PrtgAPI.PowerShell.Base
{
    /// <summary>
    /// Base class for all cmdlets that perform actions or manipulate objects in PRTG.
    /// </summary>
    public abstract class PrtgOperationCmdlet : PrtgCmdlet
    {
        /// <summary>
        /// Executes an action and displays a progress message (if required).
        /// </summary>
        /// <param name="action">The action to be performed.</param>
        /// <param name="progressMessage">The body of the progress message to display.</param>
        /// <param name="complete">Whether to allow <see cref="PrtgOperationCmdlet"/> to dynamically determine whether progress should be completed</param>
        protected virtual void ExecuteOperation(Action action, string progressMessage, bool complete = true)
        {
            ProgressManager.ProcessOperationProgress(ProgressActivity, progressMessage);

            try
            {
                action();
            }
            catch (PipelineStoppedException)
            {
                //Invoked when Select was the last cmdlet in the pipeline
                ProgressManager.TryCompleteProgress();

                throw;
            }

            var passThru = this as IPrtgPassThruCmdlet;

            if (passThru != null && !ProgressManager.PostProcessMode())
                passThru.WritePassThru();

            if (complete)
                CompleteOperationProgress();
        }

        /// <summary>
        /// Parse a value into its expected type. Requires the target <paramref name="property"/> contain a Parse method.
        /// </summary>
        /// <param name="property">The property the value applies to.</param>
        /// <param name="value">The value to apply to the property.</param>
        /// <returns>If the target property type contains a Parse method which did not throw upon being called, the parsed value. Otherwise, the original value.</returns>
        protected object ParseValueIfRequired(PropertyInfo property, object value)
        {
            if (value is PSObject)
                value = ((PSObject) value).BaseObject;

            //Types that can have possible enum values (such as TriggerChannel) possess a static Parse method for type conversion by the PowerShell runtime.
            //Only parse types that are defined in the PrtgAPI assembly.
            if (property.PropertyType.Assembly.FullName == GetType().Assembly.FullName && !property.PropertyType.IsEnum)
            {
                var method = property.PropertyType.GetMethod("Parse", BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Static);

                if (method != null)
                {
                    try
                    {
                        var newValue = method.Invoke(null, new[] { value });
                        value = newValue;
                    }
                    catch (Exception)
                    {
                        //Don't care if our value wasn't parsable
                    }
                }
            }
            else
            {
                //Try and parse the value if the property type is an Enum or Nullable Enum
                var type = property.PropertyType;

                if (!type.IsEnum)
                    type = Nullable.GetUnderlyingType(property.PropertyType);

                if (type?.IsEnum == true)
                {
                    if (Enum.GetNames(type).Any(e => e.ToLower() == value?.ToString().ToLower()))
                        return Enum.Parse(type, value.ToString(), true);
                }
            }

            return value;
        }

        private void CompleteOperationProgress()
        {
            if (ProgressManager.ProgressEnabled)
            {
                if (ProgressManager.PipeFromVariableWithProgress && ProgressManager.PipelineIsProgressPure)
                    ProgressManager.CompleteProgress();
                else
                {
                    if (ProgressManager.PreviousCmdletIsSelectObject)
                        ProgressManager.MaybeCompletePreviousProgress();
                    else
                    {
                        if (ProgressManager.PipelineUpstreamContainsBlockingCmdlet || ProgressManager.PostProcessMode())
                            ProgressManager.CompleteProgress();
                    }
                }
            }
        }

        internal abstract string ProgressActivity { get; }
    }
}
