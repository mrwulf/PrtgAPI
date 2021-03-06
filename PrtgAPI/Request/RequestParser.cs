﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using PrtgAPI.Attributes;
using PrtgAPI.Helpers;
using PrtgAPI.Parameters;

namespace PrtgAPI.Request
{
    static class RequestParser
    {
        #region Notifications

        internal static TriggerChannel GetTriggerChannel(TriggerParameters parameters)
        {
            TriggerChannel channel = null;

            switch (parameters.Type)
            {
                case TriggerType.Speed:
                    channel = ((SpeedTriggerParameters)parameters).Channel;
                    break;
                case TriggerType.Volume:
                    channel = ((VolumeTriggerParameters)parameters).Channel;
                    break;
                case TriggerType.Threshold:
                    channel = ((ThresholdTriggerParameters)parameters).Channel;
                    break;
            }

            return channel;
        }

        internal static XDocument ExtractActionXml(XDocument normal, XElement properties, int id)
        {
            var thisDoc = new XDocument(normal);
            var items = thisDoc.Descendants("item");
            items.Where(i => i.Element("objid").Value != id.ToString()).Remove();
            items.Single().Add(properties.Nodes());
            return thisDoc;
        }

        #endregion
        #region Add Objects

        internal static SearchFilter[] GetFilters(int destinationId, NewObjectParameters parameters)
        {
            var filters = new List<SearchFilter>()
            {
                new SearchFilter(Property.ParentId, destinationId)
            };

            if (parameters is NewSensorParameters)
            {
                //When creating new sensors, PRTG may dynamically assign a name based on the sensor's parameters.
                //As such, we instead filter for sensors of the newly created type
                var sensorType = parameters[Parameter.SensorType];

                var str = sensorType is SensorType ? ((Enum)sensorType).EnumToXml() : sensorType?.ToString();

                filters.Add(new SearchFilter(Property.Type, str?.ToLower() ?? string.Empty));
            }
            else
                filters.Add(new SearchFilter(Property.Name, parameters.Name));

            return filters.ToArray();
        }

        internal static List<KeyValuePair<Parameter, object>> ValidateObjectParameters(NewObjectParameters parameters)
        {
            var properties = parameters.GetType().GetNormalProperties().ToList();

            foreach (var property in properties)
            {
                var requireValue = property.GetCustomAttribute<RequireValueAttribute>();

                if (requireValue != null && requireValue.ValueRequired)
                    ValidateRequiredValue(property, parameters);

                var dependency = property.GetCustomAttribute<DependentPropertyAttribute>();

                if (dependency != null)
                    ValidateDependentProperty(dependency, property, parameters);
            }

            var lengthLimit = parameters.GetParameters().Where(p => p.Key.GetEnumAttribute<LengthLimitAttribute>() != null).ToList();

            return lengthLimit;
        }

        private static void ValidateRequiredValue(PropertyInfo property, NewObjectParameters parameters, DependentPropertyAttribute attrib = null)
        {
            var val = property.GetValue(parameters);

            var dependentStr = attrib != null ? $" when property '{attrib.Name}' is value '{attrib.RequiredValue}'" : "";

            if (string.IsNullOrEmpty(val?.ToString()))
            {
                throw new InvalidOperationException($"Property '{property.Name}' requires a value{dependentStr}, however the value was null or empty.");
            }

            var list = val as IEnumerable;

            if (list != null)
            {
                var casted = list.Cast<object>();

                if (!casted.Any())
                    throw new InvalidOperationException($"Property '{property.Name}' requires a value, however an empty list was specified.");
            }
        }

        private static void ValidateDependentProperty(DependentPropertyAttribute attrib, PropertyInfo property, NewObjectParameters parameters)
        {
            var target = parameters.GetType().GetProperty(attrib.Name).GetValue(parameters);

            if (target.ToString() == attrib.RequiredValue.ToString())
                ValidateRequiredValue(property, parameters, attrib);
        }

        internal static Parameters.Parameters GetInternalNewObjectParameters(int deviceId, NewObjectParameters parameters)
        {
            var newParams = new Parameters.Parameters();

            foreach (var param in parameters.GetParameters())
            {
                newParams[param.Key] = param.Value;
            }

            newParams[Parameter.Id] = deviceId;

            return newParams;
        }

        #endregion
        #region Get Object Properties

        internal static XmlFunction GetGetObjectPropertyFunction(string property)
        {
            if (property.TrimEnd('_').ToLower() == "comments")
                return XmlFunction.GetObjectStatus;

            return XmlFunction.GetObjectProperty;
        }

        #endregion
        #region System Administration

        [ExcludeFromCodeCoverage]
        internal static CommandFunction GetClearSystemCacheFunction(SystemCacheType cache)
        {
            if (cache == SystemCacheType.General)
                return CommandFunction.ClearCache;
            if (cache == SystemCacheType.GraphData)
                return CommandFunction.RecalcCache;

            throw new NotImplementedException($"Don't know how to handle cache type '{cache}'");
        }

        [ExcludeFromCodeCoverage]
        internal static CommandFunction GetLoadSystemFilesFunction(ConfigFileType fileType)
        {
            if (fileType == ConfigFileType.General)
                return CommandFunction.ReloadFileLists;
            if (fileType == ConfigFileType.Lookups)
                return CommandFunction.LoadLookups;

            throw new NotImplementedException($"Don't know how to handle file type '{fileType}'");
        }

        #endregion
    }
}
