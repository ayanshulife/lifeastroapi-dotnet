using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeAstroApi
{
    /// <summary>
    /// Query string encoding, byte for byte as the Node.js SDK's <c>encodeQuery</c>: keys sorted
    /// (ordinal, as JavaScript's <c>sort()</c>), null and empty values left out, numbers written the way
    /// JavaScript's <c>String(n)</c> writes them, booleans <c>true</c> / <c>false</c>, and
    /// <c>application/x-www-form-urlencoded</c> escaping as <c>URLSearchParams</c> does it. Always
    /// culture-invariant.
    /// </summary>
    internal static class QueryString
    {
        /// <summary>Encodes wire-formatted pairs: <c>?a=1&amp;b=2</c>, or "" when nothing is left.</summary>
        internal static string Encode(IEnumerable<KeyValuePair<string, string>>? query)
        {
            if (query == null)
            {
                return string.Empty;
            }

            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in query)
            {
                if (pair.Key == null || string.IsNullOrEmpty(pair.Value))
                {
                    continue;
                }

                sorted[pair.Key] = pair.Value;
            }

            if (sorted.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder("?");
            foreach (KeyValuePair<string, string> pair in sorted)
            {
                if (sb.Length > 1)
                {
                    sb.Append('&');
                }

                AppendForm(sb, pair.Key);
                sb.Append('=');
                AppendForm(sb, pair.Value);
            }

            return sb.ToString();
        }

        /// <summary>
        /// The text a value of the escape hatch (<c>RequestAsync</c>) is sent as; null when it is left
        /// out (null or empty).
        /// </summary>
        internal static string? ToWire(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return s.Length == 0 ? null : s;
                case bool b:
                    return b ? "true" : "false";
                case double d:
                    return FormatNumber(d);
                case float f:
                    return FormatNumber(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                case decimal m:
                    return FormatDecimal(m);
                case int or long or short or byte or sbyte or uint or ulong or ushort:
                    return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
                case Lang l:
                    return l.ToCode();
                case Ayanamsa a:
                    return a.ToWireValue();
                case HouseSystem h:
                    return h.ToWireValue();
                case LifeAstroDate ld:
                    return ld.IsEmpty ? null : ld.Value;
                case LifeAstroTime lt:
                    return lt.IsEmpty ? null : lt.Value;
                case DateTime dt:
                    return LifeAstroDate.FromDateTime(dt).Value;
                case DateTimeOffset dto:
                    return LifeAstroDate.FromDateTimeOffset(dto).Value;
                case TimeSpan ts:
                    return LifeAstroTime.FromTimeSpan(ts).Value;
#if NET6_0_OR_GREATER
                case DateOnly donly:
                    return LifeAstroDate.FromDateOnly(donly).Value;
                case TimeOnly tonly:
                    return LifeAstroTime.FromTimeOnly(tonly).Value;
#endif
                case Enum e:
                    return e.ToString();
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    string? text = value.ToString();
                    return string.IsNullOrEmpty(text) ? null : text;
            }
        }

        /// <summary>
        /// A double as JavaScript's <c>Number.prototype.toString</c> writes it: <c>19.076</c>, <c>5</c>
        /// (not <c>5.0</c>), <c>0.000001</c>, <c>1e-7</c>, <c>1.5e+21</c>. Never culture-dependent.
        /// </summary>
        internal static string FormatNumber(double value) => FormatNumber(value, ShortestDigitsIsReliable);

        /// <summary>
        /// "R" formatting gives the shortest round-trip digits on .NET Core 3.0 and later. On .NET
        /// Framework it may not, so the netstandard2.0 build looks for the fewest significant digits
        /// (1 to 17) that read back as the same double.
        /// </summary>
        internal static readonly bool ShortestDigitsIsReliable =
#if NET8_0_OR_GREATER
            true;
#else
            false;
#endif

        internal static string FormatNumber(double value, bool roundTripFormatIsShortest)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsInfinity(value))
            {
                return value > 0 ? "Infinity" : "-Infinity";
            }

            if (value == 0.0)
            {
                return "0";
            }

            string sign = value < 0 ? "-" : string.Empty;
            string repr = Shortest(Math.Abs(value), roundTripFormatIsShortest);

            // Split "d.ddddE+xx" / "ddd.ddd" into the significant digits and the exponent n, where the
            // value is 0.<digits> x 10^n (the ECMAScript Number::toString notation).
            int ePos = repr.IndexOfAny(new[] { 'E', 'e' });
            int exp10 = 0;
            string mantissa = repr;
            if (ePos >= 0)
            {
                exp10 = int.Parse(repr.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                mantissa = repr.Substring(0, ePos);
            }

            int dot = mantissa.IndexOf('.');
            string intPart = dot < 0 ? mantissa : mantissa.Substring(0, dot);
            string fracPart = dot < 0 ? string.Empty : mantissa.Substring(dot + 1);
            string allDigits = intPart + fracPart;
            int pointPos = intPart.Length + exp10;
            int lead = 0;
            while (lead < allDigits.Length - 1 && allDigits[lead] == '0')
            {
                lead++;
            }

            string digits = allDigits.Substring(lead).TrimEnd('0');
            if (digits.Length == 0)
            {
                return "0";
            }

            int n = pointPos - lead;
            int k = digits.Length;
            if (k <= n && n <= 21)
            {
                return sign + digits + new string('0', n - k);
            }

            if (0 < n && n <= 21)
            {
                return sign + digits.Substring(0, n) + "." + digits.Substring(n);
            }

            if (-6 < n && n <= 0)
            {
                return sign + "0." + new string('0', -n) + digits;
            }

            int e = n - 1;
            string m = k == 1 ? digits : digits[0] + "." + digits.Substring(1);
            return sign + m + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        }

        private static string Shortest(double value, bool roundTripFormatIsShortest)
        {
            if (roundTripFormatIsShortest)
            {
                return value.ToString("R", CultureInfo.InvariantCulture);
            }

            for (int precision = 1; precision <= 17; precision++)
            {
                string s = value.ToString("G" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) == value)
                {
                    return s;
                }
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatDecimal(decimal value)
        {
            string s = value.ToString(CultureInfo.InvariantCulture);
            if (s.IndexOf('.') >= 0)
            {
                s = s.TrimEnd('0').TrimEnd('.');
            }

            return s == "-0" ? "0" : s;
        }

        /// <summary><c>application/x-www-form-urlencoded</c>, as JavaScript's <c>URLSearchParams</c>.</summary>
        internal static string Form(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            AppendForm(sb, text);
            return sb.ToString();
        }

        private static void AppendForm(StringBuilder sb, string text)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9')
                    || b == '*' || b == '-' || b == '.' || b == '_')
                {
                    sb.Append((char)b);
                }
                else if (b == ' ')
                {
                    sb.Append('+');
                }
                else
                {
                    AppendPercent(sb, b);
                }
            }
        }

        /// <summary>JavaScript's <c>encodeURIComponent</c>, for path parameters.</summary>
        internal static string EncodeUriComponent(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9')
                    || b == '-' || b == '_' || b == '.' || b == '!' || b == '~' || b == '*' || b == '\'' || b == '(' || b == ')')
                {
                    sb.Append((char)b);
                }
                else
                {
                    AppendPercent(sb, b);
                }
            }

            return sb.ToString();
        }

        private static void AppendPercent(StringBuilder sb, byte b)
        {
            const string Hex = "0123456789ABCDEF";
            sb.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);
        }
    }
}
