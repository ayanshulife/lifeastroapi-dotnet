using System;
using System.Collections.Generic;
using System.Globalization;

namespace LifeAstroApi.Pdf
{
    // Property access, calls and the built-in methods the templates use. Where JavaScript throws a TypeError
    // (a property of null, map() of something that is not an array), a JsTypeException is thrown.
    internal readonly partial struct Js
    {
        internal Js Get(string key)
        {
            switch (_ref)
            {
                case JsObject o:
                    return o.Get(key);
                case List<Js> list:
                    if (key == "length")
                    {
                        return N(list.Count);
                    }

                    return JsText.TryIndex(key, out uint at) && at < list.Count ? list[(int)at] : default;
                case string s:
                    if (key == "length")
                    {
                        return N(s.Length);
                    }

                    return JsText.TryIndex(key, out uint i) && i < s.Length ? S(s.Substring((int)i, 1)) : default;
                default:
                    if (IsNullish)
                    {
                        throw new JsTypeException("cannot read '" + key + "' of " + Str());
                    }

                    return default;
            }
        }

        internal Js Get(Js key)
        {
            if (key.IsNumber && _ref is List<Js> list)
            {
                double i = key._number;
                return i >= 0 && i < list.Count && Math.Floor(i) == i ? list[(int)i] : default;
            }

            return Get(key.Str());
        }

        internal Js GetOptional(string key) => IsNullish ? default : Get(key);

        internal Js GetOptional(Js key) => IsNullish ? default : Get(key);

        internal Js Set(Js key, Js value) => Set(key.Str(), value);

        internal Js Set(string key, Js value)
        {
            if (_ref is JsObject o)
            {
                o.Set(key, value);
                return value;
            }

            throw new JsTypeException("cannot set '" + key + "'");
        }

        internal Js Call(params Js[] args) =>
            _ref is Func<Js[], Js> f ? f(args) : throw new JsTypeException("not a function");

        private List<Js> ArrayFor(string method) =>
            _ref as List<Js> ?? throw new JsTypeException(method + "() needs an array, got " + TypeOf(this).Str());

        private string TextFor(string method) =>
            _ref as string ?? throw new JsTypeException(method + "() needs a string, got " + TypeOf(this).Str());

        private JsRegex RegexFor(string method) =>
            _ref as JsRegex ?? throw new JsTypeException(method + "() needs a regular expression");

        private static int RelativeIndex(Js value, int length, int ifUndefined)
        {
            if (value._ref is null)
            {
                return ifUndefined;
            }

            double n = value.ToNumber();
            if (double.IsNaN(n))
            {
                return 0;
            }

            n = Math.Truncate(n);
            if (n < 0)
            {
                n += length;
            }

            return n < 0 ? 0 : n > length ? length : (int)n;
        }

        /// <summary>What <c>for ... of</c> walks.</summary>
        internal IEnumerable<Js> Iterate()
        {
            switch (_ref)
            {
                case List<Js> list:
                    return list;
                case JsMap map:
                    return map.Entries();
                case string s:
                    return JsText.CodePoints(s);
                default:
                    throw new JsTypeException(TypeOf(this).Str() + " is not iterable");
            }
        }

        // ---- Array ----

        internal Js Join(Js separator) => S(JoinList(ArrayFor("join"), separator._ref is null ? "," : separator.Str()));

        internal Js Map(Js callback)
        {
            List<Js> list = ArrayFor("map");
            var result = new List<Js>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                result.Add(callback.Call(list[i], N(i), this));
            }

            return new Js(result);
        }

        internal Js Filter(Js callback)
        {
            List<Js> list = ArrayFor("filter");
            var result = new List<Js>();
            for (int i = 0; i < list.Count; i++)
            {
                if (callback.Call(list[i], N(i), this))
                {
                    result.Add(list[i]);
                }
            }

            return new Js(result);
        }

        internal Js ForEach(Js callback)
        {
            List<Js> list = ArrayFor("forEach");
            for (int i = 0; i < list.Count; i++)
            {
                callback.Call(list[i], N(i), this);
            }

            return default;
        }

        internal Js Some(Js callback)
        {
            List<Js> list = ArrayFor("some");
            for (int i = 0; i < list.Count; i++)
            {
                if (callback.Call(list[i], N(i), this))
                {
                    return True;
                }
            }

            return False;
        }

        internal Js Find(Js callback)
        {
            List<Js> list = ArrayFor("find");
            for (int i = 0; i < list.Count; i++)
            {
                if (callback.Call(list[i], N(i), this))
                {
                    return list[i];
                }
            }

            return default;
        }

        internal Js Push(Js value)
        {
            List<Js> list = ArrayFor("push");
            list.Add(value);
            return N(list.Count);
        }

        internal Js IndexOf(Js value)
        {
            if (_ref is string s)
            {
                return N(s.IndexOf(value.Str(), StringComparison.Ordinal));
            }

            List<Js> list = ArrayFor("indexOf");
            for (int i = 0; i < list.Count; i++)
            {
                if (AreEqual(list[i], value))
                {
                    return N(i);
                }
            }

            return N(-1);
        }

        internal Js Includes(Js value)
        {
            if (_ref is string s)
            {
                return B(s.IndexOf(value.Str(), StringComparison.Ordinal) >= 0);
            }

            foreach (Js item in ArrayFor("includes"))
            {
                if (SameValueZero(item, value))
                {
                    return True;
                }
            }

            return False;
        }

        internal Js Slice(Js start, Js end = default)
        {
            if (_ref is string s)
            {
                int first = RelativeIndex(start, s.Length, 0);
                int last = RelativeIndex(end, s.Length, s.Length);
                return S(last > first ? s.Substring(first, last - first) : string.Empty);
            }

            List<Js> list = ArrayFor("slice");
            int from = RelativeIndex(start, list.Count, 0);
            int to = RelativeIndex(end, list.Count, list.Count);
            return new Js(to > from ? list.GetRange(from, to - from) : new List<Js>());
        }

        /// <summary><c>Array.from</c> of something iterable: a string gives its code points.</summary>
        internal static Js ArrayFrom(Js iterable) => new Js(new List<Js>(iterable.Iterate()));

        internal static Js ArrayFrom(Js arrayLike, Js map)
        {
            double length = arrayLike.Get("length").ToNumber();
            var result = new List<Js>();
            for (int i = 0; i < length; i++)
            {
                result.Add(map.Call(arrayLike.Get(N(i)), N(i)));
            }

            return new Js(result);
        }

        // ---- Object ----

        internal static Js ObjectKeys(Js value)
        {
            var result = new List<Js>();
            foreach (string key in value.OwnKeys())
            {
                result.Add(S(key));
            }

            return new Js(result);
        }

        internal static Js ObjectValues(Js value)
        {
            var result = new List<Js>();
            foreach (string key in value.OwnKeys())
            {
                result.Add(value.Get(key));
            }

            return new Js(result);
        }

        internal static Js ObjectEntries(Js value)
        {
            var result = new List<Js>();
            foreach (string key in value.OwnKeys())
            {
                result.Add(Arr(S(key), value.Get(key)));
            }

            return new Js(result);
        }

        /// <summary><c>Object.prototype.hasOwnProperty.call(target, key)</c>.</summary>
        internal static Js HasOwn(Js target, Js key)
        {
            string name = key.Str();
            switch (target._ref)
            {
                case JsObject o:
                    return B(o.Has(name));
                case List<Js> list:
                    return B(name == "length" || (JsText.TryIndex(name, out uint at) && at < list.Count));
                case string s:
                    return B(name == "length" || (JsText.TryIndex(name, out uint i) && i < s.Length));
                default:
                    if (target.IsNullish)
                    {
                        throw new JsTypeException("cannot look '" + name + "' up in " + target.Str());
                    }

                    return False;
            }
        }

        private List<string> OwnKeys()
        {
            if (IsNullish)
            {
                throw new JsTypeException("cannot list the keys of " + Str());
            }

            if (_ref is JsObject o)
            {
                return o.Keys();
            }

            int length = _ref is List<Js> list ? list.Count : _ref is string s ? s.Length : 0;
            var keys = new List<string>(length);
            for (int i = 0; i < length; i++)
            {
                keys.Add(i.ToString(CultureInfo.InvariantCulture));
            }

            return keys;
        }

        // ---- String ----

        internal Js Trim() => S(JsText.Trim(TextFor("trim")));

        internal Js ToLowerCase() => S(TextFor("toLowerCase").ToLowerInvariant());

        internal Js StartsWith(Js prefix) => B(TextFor("startsWith").StartsWith(prefix.Str(), StringComparison.Ordinal));

        internal Js EndsWith(Js suffix) => B(TextFor("endsWith").EndsWith(suffix.Str(), StringComparison.Ordinal));

        /// <summary><c>toUpperCase</c>, for ASCII: elsewhere JavaScript's mapping is not .NET's (the sharp s becomes SS).</summary>
        internal Js ToUpperCase()
        {
            string s = TextFor("toUpperCase");
            char[] upper = s.ToCharArray();
            for (int i = 0; i < upper.Length; i++)
            {
                if (upper[i] > 0x7F)
                {
                    throw new JsTypeException("toUpperCase() is translated for ASCII only");
                }

                if (upper[i] >= 'a' && upper[i] <= 'z')
                {
                    upper[i] = (char)(upper[i] - ('a' - 'A'));
                }
            }

            return S(new string(upper));
        }

        internal Js CharAt(Js index)
        {
            string s = TextFor("charAt");
            double i = index.ToNumber();
            i = double.IsNaN(i) ? 0 : Math.Truncate(i);
            return S(i >= 0 && i < s.Length ? s.Substring((int)i, 1) : string.Empty);
        }

        /// <summary><c>split</c> by a regular expression that has no groups and matches no empty text.</summary>
        internal Js Split(Js pattern) => new Js(pattern.RegexFor("split").Split(TextFor("split")));

        internal Js CharCodeAt(Js index)
        {
            string s = TextFor("charCodeAt");
            double i = index.ToNumber();
            i = double.IsNaN(i) ? 0 : Math.Truncate(i);
            return N(i >= 0 && i < s.Length ? s[(int)i] : double.NaN);
        }

        internal Js PadStart(Js width, Js fill) =>
            S(JsText.PadStart(TextFor("padStart"), width.ToNumber(), fill._ref is null ? " " : fill.Str()));

        internal Js Replace(Js pattern, string replacement) =>
            S(pattern.RegexFor("replace").Replace(TextFor("replace"), replacement));

        internal Js Replace(Js pattern, Js callback) =>
            S(pattern.RegexFor("replace").Replace(TextFor("replace"), callback));

        /// <summary><c>replace</c> with a string pattern: the first occurrence only.</summary>
        internal Js ReplaceText(string pattern, string replacement)
        {
            string text = TextFor("replace");
            int at = text.IndexOf(pattern, StringComparison.Ordinal);
            return S(at < 0 ? text : text.Substring(0, at) + replacement + text.Substring(at + pattern.Length));
        }

        // ---- RegExp ----

        internal Js Test(Js text) => B(RegexFor("test").IsMatch(text.Str()));

        internal Js Exec(Js text) => RegexFor("exec").Exec(text.Str());

        // ---- Number ----

        private double NumberFor(string method) =>
            IsNumber ? _number : throw new JsTypeException(method + "() needs a number, got " + TypeOf(this).Str());

        internal Js ToFixed(Js digits) => S(JsText.ToFixed(NumberFor("toFixed"), (int)digits.ToNumber()));

        internal Js ToRadix(Js radix) => S(JsText.ToRadix(NumberFor("toString"), (int)radix.ToNumber()));

        // ---- Map ----

        private JsMap MapFor(string method) => _ref as JsMap ?? throw new JsTypeException(method + "() needs a Map");

        internal Js MapGet(Js key) => MapFor("get").Get(key);

        internal Js MapSet(Js key, Js value)
        {
            MapFor("set").Set(key, value);
            return this;
        }

        // ---- Date ----

        private JsDate DateFor(string method) => _ref as JsDate ?? throw new JsTypeException(method + "() needs a Date");

        internal Js GetTime() => N(DateFor("getTime").Time);

        internal Js GetUtcFullYear() => N(DateFor("getUTCFullYear").Year);

        internal Js GetUtcMonth() => N(DateFor("getUTCMonth").Month - 1);

        internal Js GetUtcDate() => N(DateFor("getUTCDate").Day);

        internal Js GetUtcHours() => N(DateFor("getUTCHours").Hour);

        internal Js GetUtcMinutes() => N(DateFor("getUTCMinutes").Minute);
    }
}
