﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace PrtgAPI
{
    class SensorHistoryData
    {
        [XmlElement("datetime_raw")]
        public DateTime DateTime { get; set; }

        [XmlElement("value")]
        public List<SensorHistory> Values { get; set; }
    }
}
