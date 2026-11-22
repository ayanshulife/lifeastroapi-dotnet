using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// A JavaScript value, with the JavaScript behaviour the report templates rely on. The templates in
    /// <c>Pdf/Generated</c> are the Node.js SDK's, translated statement by statement (tool/generate-pdf.mjs);
    /// truthiness, <c>String(x)</c>, number formatting, key order and UTF-16 string functions must be
    /// JavaScript's for the HTML to be byte-identical to Node's. Nothing here reads the current culture or
    /// the time zone.
    /// </summary>
    /// <remarks>
    /// Sixteen bytes: numbers are not boxed. <c>default</c> is <c>undefined</c>. A value is one of:
    /// undefined, null, boolean, number, string, array, object, Map, function, RegExp, Date.
    /// </remarks>
    internal readonly partial struct Js
    {
        private static readonly object NullTag = new object();
        private static readonly object TrueTag = new object();
        private static readonly object FalseTag = new object();
        private static readonly object NumberTag = new object();

        /// <summary><c>Object.getOwnPropertyNames(Object.prototype)</c>: the names that every JavaScript object has.</summary>
        private static readonly HashSet<string> ObjectPrototypeKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "__defineGetter__", "__defineSetter__", "__lookupGetter__", "__lookupSetter__", "__proto__", "constructor",
            "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable", "toLocaleString", "toString", "valueOf",
        };

        private readonly object? _ref;
        private readonly double _number;

        private Js(object? reference, double number = 0)
        {
            _ref = reference;
            _number = number;
        }

        internal static Js Undefined => default;

        internal static Js Null => new Js(NullTag);

        internal static Js True => new Js(TrueTag);

        internal static Js False => new Js(FalseTag);

        internal static Js Pi => N(Math.PI);

        internal static Js N(double value) => new Js(NumberTag, value);

        internal static Js S(string? value) => new Js(value);

        internal static Js B(bool value) => new Js(value ? TrueTag : FalseTag);

        private bool IsNumber => ReferenceEquals(_ref, NumberTag);

        private bool IsNullish => _ref is null || ReferenceEquals(_ref, NullTag);

        internal bool IsObject => _ref is JsObject;

        /// <summary>The value, or null when it is null or undefined: <c>(a.NonNullish ?? b)</c> is <c>a ?? b</c>.</summary>
        internal Js? NonNullish => IsNullish ? (Js?)null : this;

        /// <summary>A parameter's or a destructured property's default: used for undefined only.</summary>
        internal Js OrDefault(Js fallback) => _ref is null ? fallback : this;

        /// <summary>The string itself, or null for a value of another type.</summary>
        internal string? AsString => _ref as string;

        // ---- truthiness: C#'s own if, ?:, && and || then behave as JavaScript's ----

        private bool Truthy
        {
            get
            {
                if (_ref is null || ReferenceEquals(_ref, NullTag) || ReferenceEquals(_ref, FalseTag))
                {
                    return false;
                }

                if (IsNumber)
                {
                    return _number != 0 && !double.IsNaN(_number);
                }

                return !(_ref is string s) || s.Length > 0;
            }
        }

        public static bool operator true(Js value) => value.Truthy;

        public static bool operator false(Js value) => !value.Truthy;

        public static Js operator !(Js value) => B(!value.Truthy);

        /// <summary>The non-short-circuit half of <c>a || b</c>: gives an operand, as JavaScript does.</summary>
        public static Js operator |(Js left, Js right) => left.Truthy ? left : right;

        /// <summary>The non-short-circuit half of <c>a &amp;&amp; b</c>.</summary>
        public static Js operator &(Js left, Js right) => left.Truthy ? right : left;

        // ---- conversions ----

        /// <summary><c>String(value)</c> and <c>${value}</c>.</summary>
        internal string Str()
        {
            if (_ref is string s)
            {
                return s;
            }

            if (_ref is null)
            {
                return "undefined";
            }

            if (IsNumber)
            {
                return NumberToString(_number);
            }

            if (ReferenceEquals(_ref, NullTag))
            {
                return "null";
            }

            if (ReferenceEquals(_ref, TrueTag))
            {
                return "true";
            }

            if (ReferenceEquals(_ref, FalseTag))
            {
                return "false";
            }

            if (_ref is List<Js> list)
            {
                return JoinList(list, ",");
            }

            if (_ref is JsRegex regex)
            {
                return regex.ToString();
            }

            return _ref is JsMap ? "[object Map]" : "[object Object]";
        }

        private static string NumberToString(double value)
        {
            // Integers are most of the numbers in a report; -0 prints as "0" in JavaScript too.
            if (value >= -999999999999999d && value <= 999999999999999d && Math.Floor(value) == value)
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            return QueryString.FormatNumber(value);
        }

        private double ToNumber()
        {
            if (IsNumber)
            {
                return _number;
            }

            if (_ref is null)
            {
                return double.NaN;
            }

            if (ReferenceEquals(_ref, NullTag) || ReferenceEquals(_ref, FalseTag))
            {
                return 0;
            }

            if (ReferenceEquals(_ref, TrueTag))
            {
                return 1;
            }

            if (_ref is JsDate date)
            {
                return date.Time;
            }

            return JsText.ToNumber(Str());
        }

        internal string? CaseKey() => _ref as string;

        internal double CaseNumber() => IsNumber ? _number : double.NaN;

        internal static Js ToStringValue(Js value) => S(value.Str());

        internal static Js ToNumberValue(Js value) => N(value.ToNumber());

        internal static Js BooleanFunction { get; } = Fn(args => B(Arg(args, 0).Truthy));

        internal static Js StringFunction { get; } = Fn(args => S(Arg(args, 0).Str()));

        // ---- operators ----

        internal static Js TypeOf(Js value)
        {
            object? r = value._ref;
            if (r is null)
            {
                return S("undefined");
            }

            if (r is string)
            {
                return S("string");
            }

            if (value.IsNumber)
            {
                return S("number");
            }

            if (ReferenceEquals(r, TrueTag) || ReferenceEquals(r, FalseTag))
            {
                return S("boolean");
            }

            return S(r is Func<Js[], Js> ? "function" : "object");
        }

        internal static bool SameValueZero(Js a, Js b) =>
            a.IsNumber && b.IsNumber && double.IsNaN(a._number) && double.IsNaN(b._number) ? true : AreEqual(a, b);

        private static bool AreEqual(Js a, Js b)
        {
            if (a.IsNumber)
            {
                return b.IsNumber && a._number == b._number;
            }

            if (a._ref is string x)
            {
                return b._ref is string y && string.Equals(x, y, StringComparison.Ordinal);
            }

            return ReferenceEquals(a._ref, b._ref);
        }

        internal int SameValueZeroHash()
        {
            if (_ref is string s)
            {
                return StringComparer.Ordinal.GetHashCode(s);
            }

            if (IsNumber)
            {
                return _number == 0 || double.IsNaN(_number) ? 0 : _number.GetHashCode();
            }

            return _ref is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_ref);
        }

        internal static Js StrictEquals(Js a, Js b) => B(AreEqual(a, b));

        internal static Js StrictNotEquals(Js a, Js b) => B(!AreEqual(a, b));

        internal static Js Less(Js a, Js b) =>
            B(a._ref is string x && b._ref is string y ? string.CompareOrdinal(x, y) < 0 : a.ToNumber() < b.ToNumber());

        internal static Js LessOrEqual(Js a, Js b) =>
            B(a._ref is string x && b._ref is string y ? string.CompareOrdinal(x, y) <= 0 : a.ToNumber() <= b.ToNumber());

        internal static Js Greater(Js a, Js b) => Less(b, a);

        internal static Js GreaterOrEqual(Js a, Js b) => LessOrEqual(b, a);

        internal static Js Add(Js a, Js b) =>
            a.AddsAsText || b.AddsAsText ? S(a.Str() + b.Str()) : N(a.ToNumber() + b.ToNumber());

        /// <summary>Strings, and objects and arrays (whose primitive value is a string), make <c>+</c> a concatenation.</summary>
        private bool AddsAsText => !IsNullish && !IsNumber && !ReferenceEquals(_ref, TrueTag) && !ReferenceEquals(_ref, FalseTag);

        internal static Js Subtract(Js a, Js b) => N(a.ToNumber() - b.ToNumber());

        internal static Js Multiply(Js a, Js b) => N(a.ToNumber() * b.ToNumber());

        internal static Js Divide(Js a, Js b) => N(a.ToNumber() / b.ToNumber());

        internal static Js Remainder(Js a, Js b) => N(a.ToNumber() % b.ToNumber());

        internal static Js Negate(Js a) => N(-a.ToNumber());

        internal static Js Sin(Js a) => N(Math.Sin(a.ToNumber()));

        internal static Js Cos(Js a) => N(Math.Cos(a.ToNumber()));

        /// <summary><c>value in Object.prototype</c>.</summary>
        internal static Js IsObjectPrototypeKey(Js value) => B(ObjectPrototypeKeys.Contains(value.Str()));

        /// <summary><c>Math.round</c>: halves go up, towards +Infinity (2.5 gives 3, -2.5 gives -2).</summary>
        internal static Js Round(Js a) => N(JsText.Round(a.ToNumber()));

        internal static Js Floor(Js a) => N(Math.Floor(a.ToNumber()));

        internal static Js Sqrt(Js a) => N(Math.Sqrt(a.ToNumber()));

        internal static Js Pow(Js a, Js b) => N(Math.Pow(a.ToNumber(), b.ToNumber()));

        /// <summary><c>Math.min</c> of any number of values: NaN when one is NaN.</summary>
        internal static Js Min(params Js[] values)
        {
            double result = double.PositiveInfinity;
            foreach (Js value in values)
            {
                result = Math.Min(result, value.ToNumber());
            }

            return N(result);
        }

        /// <summary><c>Math.max</c>, as <see cref="Min"/>.</summary>
        internal static Js Max(params Js[] values)
        {
            double result = double.NegativeInfinity;
            foreach (Js value in values)
            {
                result = Math.Max(result, value.ToNumber());
            }

            return N(result);
        }

        /// <summary><c>parseInt(text, radix)</c>.</summary>
        internal static Js ParseInt(Js value, Js radix) => N(JsText.ParseInt(value.Str(), (int)radix.ToNumber()));

        /// <summary><c>Number.isNaN</c>: true only for the number NaN.</summary>
        internal static Js IsNaN(Js a) => B(a.IsNumber && double.IsNaN(a._number));

        internal static Js IsArray(Js a) => B(a._ref is List<Js>);

        // ---- literals ----

        /// <summary>A template literal.</summary>
        internal static Js Cat(params string[] parts) => S(string.Concat(parts));

        internal static Js Fn(Func<Js[], Js> body) => new Js(body);

        internal static Js Arg(Js[] args, int index) => index < args.Length ? args[index] : default;

        internal static Js Rest(Js[] args, int from)
        {
            var list = new List<Js>();
            for (int i = from; i < args.Length; i++)
            {
                list.Add(args[i]);
            }

            return new Js(list);
        }

        internal static Js Arr(params Js[] items) => new Js(new List<Js>(items));

        /// <summary>An array of strings.</summary>
        internal static Js List(params string[] items)
        {
            var list = new List<Js>(items.Length);
            foreach (string item in items)
            {
                list.Add(S(item));
            }

            return new Js(list);
        }

        /// <summary><c>[...a, ...b]</c>.</summary>
        internal static Js Concat(params Js[] arrays)
        {
            var list = new List<Js>();
            foreach (Js array in arrays)
            {
                list.AddRange(array.Iterate());
            }

            return new Js(list);
        }

        /// <summary>An object of strings, given as key, value, key, value...</summary>
        internal static Js Table(params string[] pairs)
        {
            var o = new JsObject(pairs.Length / 2);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                o.Set(pairs[i], S(pairs[i + 1]));
            }

            return new Js(o);
        }

        internal static JsMember Prop(string key, Js value) => new JsMember(key, value);

        internal static JsMember Spread(Js value) => new JsMember(null, value);

        internal static Js Obj(params JsMember[] members)
        {
            var o = new JsObject(members.Length);
            foreach (JsMember member in members)
            {
                if (member.Key != null)
                {
                    o.Set(member.Key, member.Value);
                }
                else if (member.Value._ref is JsObject source)
                {
                    foreach (string key in source.Keys())
                    {
                        o.Set(key, source.Get(key));
                    }
                }
            }

            return new Js(o);
        }

        internal static Js Regex(string pattern, bool global, string required) => new Js(new JsRegex(pattern, global, required));

        internal static Js NewDate(Js value) =>
            new Js(new JsDate(value.AddsAsText ? JsDate.Parse(value.Str()) : value.ToNumber()));

        internal static Js NewMap() => new Js(new JsMap());

        internal static Js NewMap(Js entries)
        {
            var map = new JsMap();
            foreach (Js entry in entries.Iterate())
            {
                map.Set(entry.Get(N(0)), entry.Get(N(1)));
            }

            return new Js(map);
        }

        internal static Js FromObject(JsObject value) => new Js(value);

        internal static Js FromArray(List<Js> value) => new Js(value);

        private static string JoinList(List<Js> list, string separator)
        {
            var text = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator);
                }

                if (!list[i].IsNullish)
                {
                    text.Append(list[i].Str());
                }
            }

            return text.ToString();
        }
    }

    /// <summary>One member of an object literal: a property, or a spread (no key).</summary>
    internal readonly struct JsMember
    {
        internal JsMember(string? key, Js value)
        {
            Key = key;
            Value = value;
        }

        internal string? Key { get; }

        internal Js Value { get; }
    }
}
