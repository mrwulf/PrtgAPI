﻿using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Management.Automation;

namespace PrtgAPI.PowerShell
{
    [ExcludeFromCodeCoverage]
    class PSVariableEx : PSVariable, IFormattableMultiple
    {
        internal string RawName { get; set; }

        private Action<object> setValue;

        public PSVariableEx(string name, object initial, Action<object> setValue, bool trimName = true) : base(GetName(name, trimName), initial)
        {
            RawName = name;

            this.setValue = setValue;
        }

        private static string GetName(string name, bool trimName)
        {
            if (trimName)
                return name.TrimEnd('_');

            return name;
        }

        public override object Value
        {
            get { return base.Value; }
            set { SetValue(value, false); }
        }

        internal void SetValue(object value, bool safe)
        {
            if (safe)
                base.Value = value;
            else
                setValue(value);
        }

        public override string ToString()
        {
            return Value?.ToString() ?? "";
        }

        public string GetSerializedFormat()
        {
            return GetSerializedFormat(Value);
        }

        private string GetSerializedFormat(object value)
        {
            if (value is IFormattable)
                return ((IFormattable)value).GetSerializedFormat();

            return value?.ToString();
        }

        public string[] GetSerializedFormats()
        {
            if (Value is IEnumerable && !(Value is string))
                return ((IEnumerable) Value).Cast<object>().Select(GetSerializedFormat).ToArray();

            return new[] { GetSerializedFormat(Value) };
        }
    }
}
