﻿using System.ComponentModel;

namespace PrtgAPI
{
    /// <summary>
    /// <para type="description">Specifies properties (referred to by PRTG as "columns") that can be retrieved for a PRTG Object.</para>
    /// </summary>
    public enum Property
    {
        /// <summary>
        /// ID of the current object.
        /// Used in: All Object Tables
        /// </summary>
        [Description("objid")]
        Id,

        /// <summary>
        /// Displays the object type (group, device, report etc.) or, in case of sensors, the sensor type (ping, http, etc.).
        /// Used in: All Object Tables
        /// </summary>
        Type,

        /// <summary>
        /// The name of the object or channel, or in case of log messages the name of the associated object, or in case of stored reports the name of the report file.
        /// Used in: All Object Tables
        /// </summary>
        Name,

        /// <summary>
        /// List of all tags. This includes tags from the object itself plus those inherited from parent objects.
        /// Used in: All Object Tables
        /// </summary>
        Tags,

        /// <summary>
        /// Displays true/false depending whether an object is set to paused by a user (for tickets: related object). For notifications which are paused by schedule, it also displays the end of the schedule.
        /// Used in: All Object Tables
        /// </summary>
        Active,

        /// <summary>
        /// Cumulated downtime of a sensor (displayed as percentage of uptime+downtime).
        /// Used in: Sensors
        /// </summary>
        Downtime,

        /// <summary>
        /// Cumulated downtime of a sensor (in minutes/hours).
        /// Used in: Sensors
        /// </summary>
        [Description("downtimetime")]
        TotalDowntime,

        /// <summary>
        /// Elapsed time since last UP of a sensor.
        /// Used in: Sensors
        /// </summary>
        [Description("downtimesince")]
        DownDuration,

        /// <summary>
        /// Cumulated uptime of a sensor (displayed as percentage of uptime+downtime).
        /// Used in: Sensors
        /// </summary>
        Uptime,

        /// <summary>
        /// Cumulated uptime of a sensor (in minutes/hours).
        /// Used in: Sensors
        /// </summary>
        [Description("uptimetime")]
        TotalUptime,

        /// <summary>
        /// Elapsed time since last DOWN of a sensor.
        /// Used in: Sensors
        /// </summary>
        [Description("uptimesince")]
        UpDuration,

        /// <summary>
        /// Sum of cumulated uptime and downtime of a sensor.
        /// Used in: Sensors
        /// </summary>
        [Description("knowntime")]
        TotalMonitorTime,

        /// <summary>
        /// Timestamp when accumulation of uptimes/downtimes began.
        /// Used in: Sensors
        /// </summary>
        [Description("cumsince")]
        DataCollectedSince,

        /// <summary>
        /// Name of the sensor.
        /// Used in: Sensors, TopLists
        /// </summary>
        Sensor,

        /// <summary>
        /// This displays the effective interval setting for a sensor.
        /// Used in: Sensors
        /// </summary>
        Interval,

        /// <summary>
        /// Timestamp of the last sensor result.
        /// Used in: Sensors
        /// </summary>
        LastCheck,

        /// <summary>
        /// Timestamp of the most recent UP status.
        /// Used in: Sensors
        /// </summary>
        LastUp,

        /// <summary>
        /// Timestamp of the most recent DOWN status.
        /// Used in: Sensors
        /// </summary>
        LastDown,

        /// <summary>
        /// Name of the associated device.
        /// Used in: Sensors, Devices
        /// </summary>
        Device,

        /// <summary>
        /// Name of the associated group.
        /// Used in: Sensors, Devices, Groups
        /// </summary>
        Group,

        /// <summary>
        /// Name of the associated probe.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        Probe,

        /// <summary>
        /// Name of associated device and group seperated by slash.
        /// Used in: Sensors, Devices
        /// </summary>
        GrpDev,

        /// <summary>
        /// Number of each trigger type defined for this sensor tree object.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("notifiesx")]
        NotificationTypes,

        /// <summary>
        /// Displays either 'inherited' or the current interval setting of that object.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("intervalx")]
        IntervalInherited,

        /// <summary>
        /// Displays the access rights of the current user for a sensor tree object.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        Access,

        /// <summary>
        /// Displays the name of an associated dependency or 'parent'.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        Dependency,

        /// <summary>
        /// For sensor tree objects: <see cref="PrtgAPI.Status"/> of the object; For messages: category of the log message.
        /// Used  in: Sensors, Devices, Groups, Probes, Messages, Tickets
        /// </summary>
        Status,

        /// <summary>
        /// Detailed message of a sensor tree object (i.e. last error of a sensor) or a history, log, ticket subject.
        /// Used  in: Sensors, Devices, Groups, Probes, Messages, Tickets, TicketData, History
        /// </summary>
        Message,

        /// <summary>
        /// Displays the priority setting of a sensor tree object or the priority of a log entry/ticket.
        /// Used  in: Sensors, Devices, Groups, Probes, Messages, Tickets
        /// </summary>
        Priority,

        /// <summary>
        /// Last sensor result value or channel values. When used with channels the 'lastvalue_' has to be used to automatically display volumes and speed.
        /// Used in: Sensors, Channels
        /// </summary>
        LastValue,

        /// <summary>
        /// Number of sensors currently in an <see cref="PrtgAPI.Status.Up"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("upsens")]
        UpSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.Down"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("downsens")]
        DownSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.DownAcknowledged"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("downacksens")]
        DownAcknowledgedSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.DownPartial"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("partialdownsens")]
        PartialDownSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.Warning"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("warnsens")]
        WarningSensors,

        /// <summary>
        /// Number of sensors currently in a PAUSED state. This includes all PAUSED states (i.e. <see cref="PrtgAPI.Status.PausedByUser"/>, <see cref="PrtgAPI.Status.PausedByDependency"/>, <see cref="PrtgAPI.Status.PausedBySchedule"/> etc.).
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("pausedsens")]
        PausedSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.Unusual"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("unusualsens")]
        UnusualSensors,

        /// <summary>
        /// Number of sensors currently in a <see cref="PrtgAPI.Status.Unknown"/> state. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("undefinedsens")]
        UndefinedSensors,

        /// <summary>
        /// Number of sensors. Only the sensor itself or sensors in the hierarchy below the displayed object are counted.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        [Description("totalsens")]
        TotalSensors,

        /// <summary>
        /// Should only be used as 'value_', because then it will be expanded for all visible channels/toplist columns. Displays a channel value or a toplist value.
        /// Used in: Values, TopData
        /// </summary>
        [Description("value_")]
        Value,

        /// <summary>
        /// Displays the sensor coverage of a time span in a value table.
        /// Used in: Values
        /// </summary>
        Coverage,

        /// <summary>
        /// Displays an exclamation mark when the sensor tree object is marked as favorite.
        /// Used in: Sensors, Devices, Groups, Probes
        /// </summary>
        Favorite,

        /// <summary>
        /// Displays the user responsible for a history entry or the user (or user group) a ticket is assigned to.
        /// Used in: History, Tickets, TicketData
        /// </summary>
        [Description("user")]
        UserName,

        /// <summary>
        /// Name of the parent object of the associated object of a log message.
        /// Used in: Messages
        /// </summary>
        Parent,

        /// <summary>
        /// Timestamp or timespan of an object (for tickets: last modification).
        /// Used in: Messages, Tickets, TicketData, Values, History, StoredReports, TopIDX
        /// todo: what is topidx?------------------------------------------------------------------------------
        /// </summary>
        DateTime,

        /// <summary>
        /// Like 'datetime' but only the date part.
        /// Used in: Messages, Tickets, History, Values
        /// </summary>
        DateOnly,

        /// <summary>
        /// Like 'datetime' but only the date part.
        /// Used in: Messages, Tickets, History, Values
        /// </summary>
        TimeOnly,

        /// <summary>
        /// For sensor tree objects this displays the name of an associated schedule, for reports this displays the report generation schedule.
        /// Used in: Sensors, Devices, Groups, Probes, Reports
        /// </summary>
        Schedule,

        /// <summary>
        /// Displays the period of a report (day, week etc.).
        /// Used in: Reports
        /// </summary>
        Period,

        /// <summary>
        /// Displays the email address of a report.
        /// Used in: Reports
        /// </summary>
        Email,

        /// <summary>
        /// Displays the template used by a report.
        /// Used in: Reports
        /// </summary>
        Template,

        /// <summary>
        /// Timestamp of the last generation of a report.
        /// Used in: Reports
        /// </summary>
        LastRun,

        /// <summary>
        /// Timestamp of the next generation of a report.
        /// Used in: Reports
        /// </summary>
        NextRun,

        /// <summary>
        /// Size of a stored report.
        /// Used in: StoredReports
        /// </summary>
        Size,

        /// <summary>
        /// Numerical data for minigraphs. Numbers are 5 minute averages for the last 24 hours (must be scaled to the maximum of the series). There are two datasets: "|" separates measured value series and error series.
        /// Used in: Sensors
        /// </summary>
        MiniGraph,

        /// <summary>
        /// Device Icon.
        /// Used in: Devices
        /// </summary>
        DeviceIcon,

        /// <summary>
        /// Object comments (for tickets: related object).
        /// Used in: All Objects
        /// </summary>
        Comments,

        /// <summary>
        /// Hostname or IP address.
        /// Used in: Devices
        /// </summary>
        Host,

        /// <summary>
        /// Probe status for probes, auto discovery status for groups.
        /// Used in: Groups, Probes
        /// </summary>
        Condition,

        /// <summary>
        /// Probe connection status. Equivalent to <see cref="Condition"/>.
        /// </summary>
        [Description("condition")]
        ProbeStatus,

        /// <summary>
        /// Object type (string).
        /// Used in: All Tree Objects
        /// </summary>
        BaseType,

        /// <summary>
        /// URL of the object.
        /// Used in: All Tree Objects
        /// </summary>
        [Description("baselink")]
        Url,

        /// <summary>
        /// URL of the device icon.
        /// Used in: Devices
        /// </summary>
        Icon,

        /// <summary>
        /// ID of the parent object or ID of a ticket (e.g. the device of sensor, the probe of a group, etc).
        /// Used in: All Tree Objects, Tickets
        /// </summary>
        ParentId,

        /// <summary>
        /// Location property (used in Geo Maps).
        /// Used in: Groups, Devices
        /// </summary>
        Location,

        /// <summary>
        /// Subobjects are folded up (true) or down (false); tickets: user (or user group) to which ticket is assinged read it since last change.
        /// Used in: Groups, Probes, Tickets
        /// </summary>
        [Description("fold")]
        Collapsed,

        /// <summary>
        /// Number of groups in a probe/group node.
        /// Used in: Groups, Probes
        /// </summary>
        [Description("groupnum")]
        TotalGroups,

        /// <summary>
        /// Number of devices in a probe/group node.
        /// Used in: Groups, Probes
        /// </summary>
        [Description("devicenum")]
        TotalDevices,

        /// <summary>
        /// Type of the ticket: user, notification, todo.
        /// Used in: Tickets
        /// </summary>
        TicketType,

        /// <summary>
        /// User who edited the ticket most recently.
        /// Used in: Tickets, TicketData
        /// </summary>
        ModifiedBy,

        /// <summary>
        /// Types of all ticket edits.
        /// Used in: TicketData
        /// </summary>
        Actions,

        /// <summary>
        /// The text of the ticket that was added with the last edit, or the properties of a notification trigger.
        /// Used in: TicketData, Triggers
        /// </summary>
        Content,

        /// <summary>
        /// Position of the object in PRTG Tables. Internally, this value is represented as position = pos * 10
        /// </summary>
        Position,

        /// <summary>
        /// Start date to retrieve records from. This is the point in time furthest away from now.
        /// Used in: Messages (Logs)
        /// </summary>
        [Description("dstart")]
        StartDate,

        /// <summary>
        /// End date to retrieve records from. This is the point in time closest to now.
        /// Used in: Messages (Logs)
        /// </summary>
        [Description("dend")]
        EndDate,

        /// <summary>
        /// Age of records to retrieve.
        /// Used in: Messages (Logs)
        /// </summary>
        [Description("drel")]
        RecordAge,
    }
}
