﻿using System.Xml.Serialization;
using PrtgAPI.Attributes;
using PrtgAPI.Helpers;

namespace PrtgAPI.Objects.Shared
{
    /// <summary>
    /// Base class for objects that appear in tables.
    /// </summary>
    public class ObjectTable : PrtgObject
    {
        // ################################## All Object Tables ##################################       

        private string type;

        /// <summary>
        /// The type of this object. Certain objects may simply report their <see cref="BaseType"/>, while others may get more specific (e.g. a sensor of type "Ping").
        /// </summary>
        [XmlElement("type")]
        [PropertyParameter(nameof(Property.Type))]
        public string Type
        {
            get { return Lazy(() => type); }
            set { type = value; }
        }

        private string rawType;

        /// <summary>
        /// The raw type name of this object.
        /// </summary>
        [XmlElement("type_raw")]
        public string RawType
        {
            get { return Lazy(() => rawType); }
            set { rawType = value; }
        }

        internal SensorTypeInternal? typeRaw => (SensorTypeInternal?) EnumHelpers.XmlToEnum<XmlEnumAttribute>(RawType, typeof (SensorTypeInternal), false);

        private string[] tags;

        /// <summary>
        /// Tags contained on this object.
        /// </summary>
        [XmlElement("tags")]
        [XmlElement("injected_tags")]
        [SplittableString(' ')]
        [PropertyParameter(nameof(Property.Tags))] //todo: give this some attribute we can use to decide to split it later
        public string[] Tags
        {
            get { return Lazy(() => tags); }
            set { tags = value; }
        }

        private bool active;

        /// <summary>
        /// Whether or not the object is currently active (in a monitoring state). If false, the object is paused.
        /// </summary>
        [XmlElement("active_raw")]
        [PropertyParameter(nameof(Property.Active))]
        public bool Active
        {
            get { return Lazy(() => active); }
            set { active = value; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectTable"/> class.
        /// </summary>
        public ObjectTable()
        {
        }

        internal ObjectTable(string raw) : base(raw)
        {
        }
    }
}
