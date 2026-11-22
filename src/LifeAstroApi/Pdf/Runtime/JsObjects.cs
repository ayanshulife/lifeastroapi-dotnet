using System;
using System.Collections.Generic;

namespace LifeAstroApi.Pdf
{
    /// <summary>A JavaScript TypeError: the input does not have the shape a template needs.</summary>
    internal sealed class JsTypeException : Exception
    {
        internal JsTypeException(string message)
            : base(message)
        {
        }
    }

    /// <summary>A plain object, with JavaScript's key order: integer-like keys ascending, then insertion order.</summary>
    internal sealed class JsObject
    {
        private readonly Dictionary<string, Js> _values;
        private readonly List<string> _order;
        private bool _hasIndexKey;

        internal JsObject(int capacity = 0)
        {
            _values = new Dictionary<string, Js>(capacity, StringComparer.Ordinal);
            _order = new List<string>(capacity);
        }

        internal Js Get(string key) => _values.TryGetValue(key, out Js value) ? value : default;

        internal bool Has(string key) => _values.ContainsKey(key);

        internal void Set(string key, Js value)
        {
            if (!_values.ContainsKey(key))
            {
                _order.Add(key);
                _hasIndexKey = _hasIndexKey || JsText.TryIndex(key, out _);
            }

            _values[key] = value;
        }

        internal List<string> Keys()
        {
            if (!_hasIndexKey)
            {
                return new List<string>(_order);
            }

            var indices = new List<KeyValuePair<uint, string>>();
            var others = new List<string>();
            foreach (string key in _order)
            {
                if (JsText.TryIndex(key, out uint index))
                {
                    indices.Add(new KeyValuePair<uint, string>(index, key));
                }
                else
                {
                    others.Add(key);
                }
            }

            indices.Sort((a, b) => a.Key.CompareTo(b.Key));
            var keys = new List<string>(_order.Count);
            foreach (KeyValuePair<uint, string> pair in indices)
            {
                keys.Add(pair.Value);
            }

            keys.AddRange(others);
            return keys;
        }
    }

    /// <summary>A JavaScript Map: any value as key, in insertion order.</summary>
    internal sealed class JsMap
    {
        private readonly Dictionary<Js, int> _slots = new Dictionary<Js, int>(KeyComparer.Instance);
        private readonly List<KeyValuePair<Js, Js>> _entries = new List<KeyValuePair<Js, Js>>();

        internal Js Get(Js key) => _slots.TryGetValue(key, out int slot) ? _entries[slot].Value : default;

        internal void Set(Js key, Js value)
        {
            if (_slots.TryGetValue(key, out int slot))
            {
                _entries[slot] = new KeyValuePair<Js, Js>(_entries[slot].Key, value);
                return;
            }

            _slots.Add(key, _entries.Count);
            _entries.Add(new KeyValuePair<Js, Js>(key, value));
        }

        internal IEnumerable<Js> Entries()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                yield return Js.Arr(_entries[i].Key, _entries[i].Value);
            }
        }

        private sealed class KeyComparer : IEqualityComparer<Js>
        {
            internal static readonly KeyComparer Instance = new KeyComparer();

            public bool Equals(Js x, Js y) => Js.SameValueZero(x, y);

            public int GetHashCode(Js obj) => obj.SameValueZeroHash();
        }
    }
}
