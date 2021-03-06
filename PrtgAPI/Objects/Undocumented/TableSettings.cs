﻿using System;
using System.Xml.Serialization;
using PrtgAPI.Objects.Deserialization;

namespace PrtgAPI
{
    /// <summary>
    /// Settings that are found within table objects in PRTG, including Sensors, Devices, Groups and Probes.
    /// </summary>
    public class TableSettings : ObjectSettings
    {
        const string TimeFormat = "yyyy,MM,dd,HH,mm,ss";

        #region Proxy Settings for HTTP Sensors

        /// <summary>
        /// Whether to inherit HTTP Proxy settings from the parent object.<para/>
        /// Corresponds to Proxy Settings for HTTP Sensors -> Inherit Proxy Settings.
        /// </summary>
        [XmlElement("injected_httpproxy")]
        public bool? InheritProxy { get; set; }

        /// <summary>
        /// IP Address/DNS name of the proxy server to use for HTTP/HTTPS requests.<para/>
        /// Corresponds to Proxy Settings for HTTP Sensors -> Name.
        /// </summary>
        [XmlElement("injected_proxy")]
        public string ProxyAddress { get; set; }

        /// <summary>
        /// Port to use to connect to the proxy server.<para/>
        /// Corresponds to Proxy Settings for HTTP Sensors -> Port.
        /// </summary>
        [XmlElement("injected_proxyport")]
        public int? ProxyPort { get; set; }

        /// <summary>
        /// Username to use to for proxy server authentication.<para/>
        /// Corresponds to Proxy Settings for HTTP Sensors -> User.
        /// </summary>
        [XmlElement("injected_proxyuser")]
        public string ProxyUser { get; set; }

        [XmlElement("injected_proxypassword")]
        internal string proxyPassword { get; set; }

        /// <summary>
        /// Whether a HTTP/HTTPS proxy password has been set.<para/>
        /// Corresponds to Proxy Settings for HTTP Sensors -> Password.
        /// </summary>
        public bool HasProxyPassword => !string.IsNullOrEmpty(proxyPassword);

        #endregion
        #region Scanning Interval

        /// <summary>
        /// Whether to inherit Scanning Interval settings from the parent object.<para/>
        /// Corresponds to Scanning Interval -> Inherit Scanning Interval.
        /// </summary>
        [XmlElement("injected_intervalgroup")]
        public bool? InheritInterval { get; set; }

        [XmlElement("injected_interval")]
        internal string intervalStr { get; set; }

        private ScanningInterval interval;

        /// <summary>
        /// The scanning interval of this object.<para/>
        /// Corresponds to Scanning Interval -> Scanning Interval.
        /// </summary>
        public ScanningInterval Interval
        {
            get
            {
                if (interval == null && intervalStr != null)
                    interval = ScanningInterval.Parse(intervalStr);

                return interval;
            }
        }

        /// <summary>
        /// How this sensor should react in the event of a sensor query failure.<para/>
        /// Corresponds to Scanning Interval -> If a Sensor Query Fails.
        /// </summary>
        [XmlElement("injected_errorintervalsdown")]
        public IntervalErrorMode? IntervalErrorMode { get; set; }

        #endregion
        #region Schedules, Dependencies and Maintenance Window

        /// <summary>
        /// Whether to inherit Schedules, Dependencies and Maintenance Window settings from the parent object.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Inherit Settings.
        /// </summary>
        [XmlElement("injected_scheduledependency")]
        public bool? InheritDependency { get; set; }

        [XmlElement("injected_schedule")]
        internal string scheduleStr { get; set; }

        private LazyValue<Schedule> schedule;

        /// <summary>
        /// The schedule during which monitoring is active. If the schedule is not active, sensors will be <see cref="Status.PausedBySchedule"/>.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Schedule.
        /// </summary>
        public Schedule Schedule
        {
            get
            {
                if (schedule == null)
                    schedule = new LazyValue<Schedule>(scheduleStr, () => new Schedule(scheduleStr));

                return schedule.Value;
            }
        }

        /// <summary>
        /// Whether a one-time maintenance window has been defined.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Maintenance Window.
        /// </summary>
        [XmlElement("injected_maintenable")]
        public bool? MaintenanceEnabled { get; set; }

        [XmlElement("injected_maintstart")]
        internal string maintenanceStartStr { get; set; }

        private LazyValue<DateTime?> maintenanceStart;

        /// <summary>
        /// The start time of a one-time maintenance window. If <see cref="MaintenanceEnabled"/> is false, this property will contain the default maintenance start value.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Maintenance Begins.
        /// </summary>
        public DateTime? MaintenanceStart
        {
            get
            {
                if (maintenanceStart == null)
                    maintenanceStart = new LazyValue<DateTime?>(maintenanceStartStr, () => DateTime.ParseExact(maintenanceStartStr, TimeFormat, null));

                return maintenanceStart.Value;
            }
        }

        [XmlElement("injected_maintend")]
        internal string maintenanceEndStr { get; set; }

        private LazyValue<DateTime?> maintenanceEnd;

        /// <summary>
        /// The end time of a one-time maintenance window. If <see cref="MaintenanceEnabled"/> is false, this property will contain the default maintenance end value.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Maintenance Ends.
        /// </summary>
        public DateTime? MaintenanceEnd
        {
            get
            {
                if (maintenanceEnd == null)
                    maintenanceEnd = new LazyValue<DateTime?>(maintenanceEndStr, () => DateTime.ParseExact(maintenanceEndStr, TimeFormat, null));

                return maintenanceEnd.Value;
            }
        }

        /// <summary>
        /// Indicates the type of object this object uses as its dependency. When the dependency object goes down, this object is <see cref="Status.PausedByDependency"/>.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Dependency Type.
        /// </summary>
        [XmlElement("injected_dependencytype")]
        public DependencyType? DependencyType { get; set; }

        /// <summary>
        /// The custom object to use as a dependency of this object.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Dependency.
        /// </summary>
        [XmlElement("injected_dependencyvalue")]
        public int? DependentObjectId { get; set; }

        /// <summary>
        /// Duration (in seconds) to delay resuming this sensor after its master object returns to <see cref="Status.Up"/>. This property only applies when <see cref="DependencyType"/> is <see cref="PrtgAPI.DependencyType.Object"/>.<para/>
        /// Corresponds to Schedules, Dependencies and Maintenance Window -> Dependency Delay.
        /// </summary>
        [XmlElement("injected_depdelay")]
        public int? DependencyDelay { get; set; }

        #endregion
    }
}
