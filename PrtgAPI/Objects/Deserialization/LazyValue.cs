﻿using System;

namespace PrtgAPI.Objects.Deserialization
{
    class LazyValue<T> : Lazy<T>
    {
        public LazyValue(string raw, Func<T> valueFactory) : base(GetFunc(raw, valueFactory))
        {
        }

        private static Func<T> GetFunc(string raw, Func<T> valueFactory)
        {
            if (raw == null)
                return () => default(T);

            return valueFactory;
        }
    }
}
