﻿using System.Collections.Generic;
using System.Management.Automation;
using PrtgAPI.Request;

namespace PrtgAPI.Parameters
{
    /// <summary>
    /// Represents raw parameters used to construct a <see cref="PrtgUrl"/> for creating a new sensor.
    /// </summary>
    public class RawSensorParameters : PSObjectSensorParameters
    {
        /// <summary>
        /// Determines whether a parameter is in the underlying parameter set.
        /// </summary>
        /// <param name="name">The name of the parameter to locate.</param>
        /// <returns>True if any parameters exist with the specified name; otherwise false.</returns>
        public bool Contains(string name) => ContainsInternal(name, false);

        /// <summary>
        /// Removes all occurrences of a specified parameter from the underlying parameter set.
        /// </summary>
        /// <param name="name">The name of the parameter to remove.</param>
        /// <returns>True if one or more items were successfully removed. If no items exist with the specified name, this method returns false.</returns>
        public bool Remove(string name) => RemoveInternal(name, false);

        /// <summary>
        /// Initializes a new instance of the <see cref="RawSensorParameters"/> class.
        /// </summary>
        /// <param name="sensorName">The name to use for this sensor.</param>
        /// <param name="sensorType">The type of sensor these parameters will create.</param>
        public RawSensorParameters(string sensorName, string sensorType) : base(sensorName, sensorType, false)
        {
        }

        /// <summary>
        /// Provides access to the underlying custom parameters of this object.
        /// </summary>
        [Hidden]
        public List<CustomParameter> Parameters
        {
            get
            {
                if (this[Parameter.Custom] == null)
                    this[Parameter.Custom] = new List<CustomParameter>();

                return (List<CustomParameter>)this[Parameter.Custom];
            }
            set { this[Parameter.Custom] = value; }
        }
    }
}
