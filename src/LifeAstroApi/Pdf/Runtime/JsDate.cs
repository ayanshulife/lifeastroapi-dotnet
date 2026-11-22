using System;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// A JavaScript Date, for the "generated at" line: <c>new Date(iso)</c> and the UTC getters, with its
    /// own calendar arithmetic (JavaScript rolls 30 February over to March).
    /// </summary>
    /// <remarks>
    /// It reads ISO 8601, which is what the API sends (<c>2026-11-22T05:42:10.123Z</c>), also with a space
    /// for the T. A time without an offset is read as UTC in both forms, so that the time zone of the machine
    /// never changes a document.
    /// </remarks>
    internal sealed class JsDate
    {
        private const double MillisecondsPerDay = 86400000d;

        internal JsDate(double time)
        {
            Time = Math.Abs(time) <= 8.64e15 ? Math.Truncate(time) : double.NaN;
            if (double.IsNaN(Time))
            {
                Year = Month = Day = Hour = Minute = double.NaN;
                return;
            }

            long days = (long)Math.Floor(Time / MillisecondsPerDay);
            long inDay = (long)(Time - (days * MillisecondsPerDay));
            Hour = inDay / 3600000;
            Minute = inDay / 60000 % 60;

            // Days since 1970-01-01 to a civil date, in 400-year eras that start on 1 March.
            long z = days + 719468;
            long era = (z >= 0 ? z : z - 146096) / 146097;
            long doe = z - (era * 146097);
            long yoe = (doe - (doe / 1460) + (doe / 36524) - (doe / 146096)) / 365;
            long doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
            long mp = ((5 * doy) + 2) / 153;
            Day = doy - (((153 * mp) + 2) / 5) + 1;
            long month = mp < 10 ? mp + 3 : mp - 9;
            Month = month;
            Year = yoe + (era * 400) + (month <= 2 ? 1 : 0);
        }

        internal double Time { get; }

        internal double Year { get; }

        internal double Month { get; }

        internal double Day { get; }

        internal double Hour { get; }

        internal double Minute { get; }

        /// <summary>Milliseconds since 1970 UTC, or NaN when the text is not an ISO date.</summary>
        internal static double Parse(string s)
        {
            int i = 0;
            if (!Read(s, ref i, 4, out int year))
            {
                return double.NaN;
            }

            int month = 1;
            int day = 1;
            bool hasDay = false;
            if (Skip(s, ref i, '-'))
            {
                if (!Read(s, ref i, 2, out month))
                {
                    return double.NaN;
                }

                if (Skip(s, ref i, '-'))
                {
                    hasDay = true;
                    if (!Read(s, ref i, 2, out day))
                    {
                        return double.NaN;
                    }
                }
            }

            if (month < 1 || month > 12 || day < 1 || day > 31)
            {
                return double.NaN;
            }

            int hour = 0;
            int minute = 0;
            int second = 0;
            int millisecond = 0;
            int offset = 0;
            if (i < s.Length)
            {
                // ISO 8601 as JavaScript defines it allows a time after the year or the month as well
                // (2026-11T10:05); the space form needs the whole date.
                bool timeFollows = Skip(s, ref i, 'T') || Skip(s, ref i, 't') || (hasDay && Skip(s, ref i, ' '));
                if (!timeFollows)
                {
                    return double.NaN;
                }

                if (!Read(s, ref i, 2, out hour) || !Skip(s, ref i, ':') || !Read(s, ref i, 2, out minute))
                {
                    return double.NaN;
                }

                if (Skip(s, ref i, ':'))
                {
                    if (!Read(s, ref i, 2, out second))
                    {
                        return double.NaN;
                    }

                    if (Skip(s, ref i, '.'))
                    {
                        int start = i;
                        for (int scale = 100; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++, scale /= 10)
                        {
                            millisecond += (s[i] - '0') * scale;
                        }

                        if (i == start)
                        {
                            return double.NaN;
                        }
                    }
                }

                if (hour > 24 || minute > 59 || second > 59 || (hour == 24 && (minute | second | millisecond) != 0))
                {
                    return double.NaN;
                }

                if (!Skip(s, ref i, 'Z') && !Skip(s, ref i, 'z') && i < s.Length)
                {
                    int sign = Skip(s, ref i, '+') ? 1 : Skip(s, ref i, '-') ? -1 : 0;
                    if (sign == 0 || !Read(s, ref i, 2, out int offsetHours))
                    {
                        return double.NaN;
                    }

                    Skip(s, ref i, ':');
                    if (!Read(s, ref i, 2, out int offsetMinutes) || offsetHours > 23 || offsetMinutes > 59)
                    {
                        return double.NaN;
                    }

                    offset = sign * ((offsetHours * 60) + offsetMinutes);
                }

                if (i != s.Length)
                {
                    return double.NaN;
                }
            }

            long y = month <= 2 ? year - 1 : year;
            long era = (y >= 0 ? y : y - 399) / 400;
            long yoe = y - (era * 400);
            long doy = (((153 * (month > 2 ? month - 3 : month + 9)) + 2) / 5) + day - 1;
            long doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
            long days = (era * 146097) + doe - 719468;
            return (days * MillisecondsPerDay)
                + (((((hour * 60L) + minute - offset) * 60) + second) * 1000)
                + millisecond;
        }

        private static bool Read(string s, ref int i, int count, out int value)
        {
            value = 0;
            if (i + count > s.Length)
            {
                return false;
            }

            for (int end = i + count; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                value = (value * 10) + (c - '0');
            }

            return true;
        }

        private static bool Skip(string s, ref int i, char c)
        {
            if (i < s.Length && s[i] == c)
            {
                i++;
                return true;
            }

            return false;
        }
    }
}
