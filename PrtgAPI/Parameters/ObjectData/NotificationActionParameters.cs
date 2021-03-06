﻿using System.Diagnostics.CodeAnalysis;
using PrtgAPI.Request;

namespace PrtgAPI.Parameters
{
    /// <summary>
    /// Represents parameters used to construct a <see cref="PrtgUrl"/> for retrieving <see cref="NotificationAction"/> objects.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class NotificationActionParameters : TableParameters<NotificationAction>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationActionParameters"/> class.
        /// </summary>
        public NotificationActionParameters() : base(Content.Notifications)
        {
        }

        internal NotificationActionParameters(int[] objectIds) : base(Content.Notifications)
        {
            SearchFilter = new[] {new SearchFilter(Property.Id, objectIds)};
        }
    }
}
