using System;
using System.Collections.Generic;

namespace Iop.Api
{
    public class IopDictionary : Dictionary<string, string>
    {
        public IopDictionary() { }

        public IopDictionary(IDictionary<string, string> dictionary) : base(dictionary) { }

        public void Add(string key, object? value)
        {
            string? strValue = value switch
            {
                null => null,
                string s => s,
                DateTime dt => dt.Ticks.ToString(),
                bool b => b.ToString().ToLower(),
                _ => value.ToString()
            };
            Add(key, strValue);
        }

        public new void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                base[key] = value;
        }

        public void AddAll(IDictionary<string, string>? dict)
        {
            if (dict == null || dict.Count == 0) return;
            foreach (var kv in dict)
                Add(kv.Key, kv.Value);
        }
    }
}
