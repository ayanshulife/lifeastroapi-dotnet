using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace LifeAstroApi.Pdf
{
    /// <summary>The JavaScript string and number functions that differ from .NET's.</summary>
    internal static class JsText
    {
        /// <summary>JavaScript's WhiteSpace and LineTerminator (unlike char.IsWhiteSpace: U+FEFF yes, U+0085 no).</summary>
        internal static bool IsSpace(char c)
        {
            int code = c;
            if (code <= 0x20)
            {
                return code == 0x20 || (code >= 0x09 && code <= 0x0D);
            }

            return code >= 0xA0 && (code == 0xA0 || code == 0x1680 || (code >= 0x2000 && code <= 0x200A)
                || code == 0x2028 || code == 0x2029 || code == 0x202F || code == 0x205F || code == 0x3000 || code == 0xFEFF);
        }

        internal static string Trim(string s)
        {
            int start = 0;
            int end = s.Length;
            while (start < end && IsSpace(s[start]))
            {
                start++;
            }

            while (end > start && IsSpace(s[end - 1]))
            {
                end--;
            }

            return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
        }

        /// <summary>An array index: a canonical decimal integer below 2^32 - 1.</summary>
        internal static bool TryIndex(string key, out uint index)
        {
            index = 0;
            if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0'))
            {
                return false;
            }

            ulong value = 0;
            foreach (char c in key)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }

                value = (value * 10) + (ulong)(c - '0');
            }

            if (value >= uint.MaxValue)
            {
                return false;
            }

            index = (uint)value;
            return true;
        }

        internal static IEnumerable<Js> CodePoints(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                int length = i + 1 < s.Length && char.IsSurrogatePair(s[i], s[i + 1]) ? 2 : 1;
                yield return Js.S(s.Substring(i, length));
                i += length - 1;
            }
        }

        /// <summary>
        /// <c>Math.round</c>. Not <c>Math.Floor(x + 0.5)</c>, which turns 0.49999999999999994 into 1, and not
        /// <c>Math.Round</c>, which sends halves to even or away from zero.
        /// </summary>
        internal static double Round(double x)
        {
            double floor = Math.Floor(x);
            double rounded = x - floor >= 0.5 ? floor + 1 : floor;
            return rounded == 0 && x < 0 ? -0.0 : rounded;
        }

        /// <summary><c>padStart</c>, counted in UTF-16 units.</summary>
        internal static string PadStart(string s, double width, string fill)
        {
            if (!(width > s.Length) || fill.Length == 0)
            {
                return s;
            }

            int needed = checked((int)width) - s.Length;
            var text = new StringBuilder(needed + s.Length);
            while (text.Length < needed)
            {
                text.Append(fill, 0, Math.Min(fill.Length, needed - text.Length));
            }

            return text.Append(s).ToString();
        }

        /// <summary><c>Number(text)</c>.</summary>
        internal static double ToNumber(string text)
        {
            string s = Trim(text);
            if (s.Length == 0)
            {
                return 0;
            }

            if (s.Length > 2 && s[0] == '0')
            {
                int bits = s[1] == 'x' || s[1] == 'X' ? 4 : s[1] == 'o' || s[1] == 'O' ? 3 : s[1] == 'b' || s[1] == 'B' ? 1 : 0;
                if (bits > 0)
                {
                    return FromRadix(s, 2, 1 << bits);
                }
            }

            int from = s[0] == '+' || s[0] == '-' ? 1 : 0;
            if (string.CompareOrdinal(s, from, "Infinity", 0, 8) == 0 && s.Length == from + 8)
            {
                return s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }

            int i = from;
            int digits = SkipDigits(s, ref i);
            if (i < s.Length && s[i] == '.')
            {
                i++;
                digits += SkipDigits(s, ref i);
            }

            if (digits == 0)
            {
                return double.NaN;
            }

            bool negativeExponent = false;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                {
                    negativeExponent = s[i] == '-';
                    i++;
                }

                if (SkipDigits(s, ref i) == 0)
                {
                    return double.NaN;
                }
            }

            if (i != s.Length)
            {
                return double.NaN;
            }

            try
            {
                double value = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                return value == 0 && s[0] == '-' ? -0.0 : value;
            }
            catch (OverflowException)
            {
                // .NET Framework throws where JavaScript gives Infinity.
                return negativeExponent ? 0 : s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        private static int SkipDigits(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }

            return i - start;
        }

        /// <summary><c>parseInt(text, radix)</c> for a radix of 10 or 16: the digits the text starts with.</summary>
        internal static double ParseInt(string text, int radix)
        {
            if (radix != 10 && radix != 16)
            {
                throw new JsTypeException("parseInt() is translated for radix 10 and 16");
            }

            string s = Trim(text);
            int start = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? 1 : 0;
            int from = start;
            if (radix == 16 && from + 1 < s.Length && s[from] == '0' && (s[from + 1] == 'x' || s[from + 1] == 'X'))
            {
                from += 2;
            }

            int end = from;
            while (end < s.Length && ((s[end] >= '0' && s[end] <= '9')
                || (radix == 16 && ((s[end] >= 'a' && s[end] <= 'f') || (s[end] >= 'A' && s[end] <= 'F')))))
            {
                end++;
            }

            if (end == from)
            {
                return double.NaN;
            }

            double value = FromRadix(s.Substring(0, end), from, radix);
            return start == 1 && s[0] == '-' ? -value : value;
        }

        private static double FromRadix(string s, int from, int radix)
        {
            double value = 0;
            for (int i = from; i < s.Length; i++)
            {
                char c = s[i];
                int digit = c >= '0' && c <= '9' ? c - '0'
                    : c >= 'a' && c <= 'f' ? c - 'a' + 10
                    : c >= 'A' && c <= 'F' ? c - 'A' + 10
                    : radix;
                if (digit >= radix)
                {
                    return double.NaN;
                }

                value = (value * radix) + digit;
            }

            return value;
        }

        /// <summary>
        /// <c>toFixed</c>: the exact binary value rounded half up (2.5 gives "3", 1.005 gives "1.00"), with
        /// the sign of any negative number (-0.001 gives "-0.00").
        /// </summary>
        internal static string ToFixed(double value, int digits)
        {
            if (digits < 0 || digits > 100)
            {
                throw new JsTypeException("toFixed() digits out of range");
            }

            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsInfinity(value) || Math.Abs(value) >= 1e21)
            {
                return Js.N(value).Str();
            }

            long raw = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            int exponent = (int)(raw >> 52) & 0x7FF;
            long mantissa = raw & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
            {
                exponent = 1;
            }
            else
            {
                mantissa |= 1L << 52;
            }

            exponent -= 1075; // |value| = mantissa * 2^exponent
            BigInteger scaled = new BigInteger(mantissa) * BigInteger.Pow(10, digits);
            if (exponent >= 0)
            {
                scaled <<= exponent;
            }
            else
            {
                scaled = (scaled + (BigInteger.One << (-exponent - 1))) >> -exponent;
            }

            string all = scaled.ToString(CultureInfo.InvariantCulture);
            if (digits > 0)
            {
                all = all.PadLeft(digits + 1, '0');
                all = all.Substring(0, all.Length - digits) + "." + all.Substring(all.Length - digits);
            }

            return value < 0 ? "-" + all : all;
        }

        /// <summary><c>n.toString(radix)</c> for the integers the templates pass (character codes).</summary>
        internal static string ToRadix(double value, int radix)
        {
            if (radix < 2 || radix > 36 || Math.Floor(value) != value || Math.Abs(value) > long.MaxValue)
            {
                throw new JsTypeException("toString(radix) is for integers");
            }

            const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            long n = Math.Abs((long)value);
            var text = new StringBuilder();
            do
            {
                text.Insert(0, Digits[(int)(n % radix)]);
                n /= radix;
            }
            while (n > 0);

            return value < 0 ? text.Insert(0, '-').ToString() : text.ToString();
        }
    }
}
