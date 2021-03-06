﻿using System.Diagnostics.CodeAnalysis;

namespace PrtgAPI.Parameters
{
    /// <summary>
    /// Base class for all parameters that perform an action against a PRTG server pertaining to a specific object.
    /// </summary>
    [ExcludeFromCodeCoverage]
    class BaseActionParameters : Parameters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseActionParameters"/> class.
        /// </summary>
        /// <param name="objectId">The ID of the object these parameters should apply to.</param>
        public BaseActionParameters(int objectId)
        {
            ObjectId = objectId;
        }

        /// <summary>
        /// The ID of the object these parameters should apply to.
        /// </summary>
        public int ObjectId
        {
            get { return (int) this[Parameter.Id]; }
            set { this[Parameter.Id] = value; }
        }
    }
}
