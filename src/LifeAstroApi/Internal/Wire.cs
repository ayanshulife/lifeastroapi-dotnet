using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LifeAstroApi
{
    /// <summary>
    /// Helpers of the generated parameter classes. The wire names they write (<c>birth.lat</c>,
    /// <c>boy.date</c>, <c>start_date</c>, ...) are the contract with the API and match
    /// <c>src/params.ts</c> of the Node.js SDK.
    /// </summary>
    internal static class Wire
    {
        internal static T NotNull<T>(T? value, string name)
            where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            return value;
        }

        internal static string RequireText(string? value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            if (value.Trim().Length == 0)
            {
                throw new ArgumentException("A value is required.", name);
            }

            return value;
        }

        internal static LifeAstroDate RequireDate(LifeAstroDate value, string name)
        {
            if (value.IsEmpty)
            {
                throw new ArgumentException("A date is required.", name);
            }

            return value;
        }

        internal static LifeAstroTime RequireTime(LifeAstroTime value, string name)
        {
            if (value.IsEmpty)
            {
                throw new ArgumentException("A time is required.", name);
            }

            return value;
        }

        internal static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Must be a finite number.", name);
            }
        }

        /// <summary>A birth object that must carry coordinates (every chart except the window methods).</summary>
        internal static BirthInput RequirePlace(BirthInput? birth, string name)
        {
            BirthInput b = NotNull(birth, name);
            if (!b.HasPlace)
            {
                throw new ArgumentException("This method needs the birth place: create the BirthInput with lat and lon.", name);
            }

            return b;
        }

        /// <summary>Like <see cref="RequirePlace"/> for an optional birth object.</summary>
        internal static BirthInput? OptionalPlace(BirthInput? birth, string name)
        {
            if (birth != null && !birth.HasPlace)
            {
                throw new ArgumentException("This method needs the birth place: create the BirthInput with lat and lon.", name);
            }

            return birth;
        }

        /// <summary>
        /// Fills the <c>{name}</c> placeholders of a path template, percent-encoding each value as
        /// JavaScript's <c>encodeURIComponent</c> does.
        /// </summary>
        internal static string Path(string template, params string[] namesAndValues)
        {
            string path = template;
            for (int i = 0; i + 1 < namesAndValues.Length; i += 2)
            {
                string name = namesAndValues[i];
                string value = namesAndValues[i + 1];
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("Missing required path parameter \"" + name + "\" for " + template + ".", name);
                }

                path = path.Replace("{" + name + "}", QueryString.EncodeUriComponent(value));
            }

            return path;
        }

        /// <summary>
        /// Today's date (<c>YYYY-MM-DD</c>) in <paramref name="tz"/>. The API has no "today" default:
        /// every moment endpoint rejects a request without <c>date</c>, so the SDK sends one, as the
        /// Node.js SDK does. Falls back to the UTC date when the zone is unknown.
        /// </summary>
        internal static LifeAstroDate TodayIn(string? tz) => TodayIn(tz, DateTimeOffset.UtcNow);

        internal static LifeAstroDate TodayIn(string? tz, DateTimeOffset now)
        {
            TimeSpan? offset = TimeZones.OffsetAt(tz, now);
            DateTimeOffset local = offset.HasValue ? now.ToOffset(offset.Value) : now.ToUniversalTime();
            return LifeAstroDate.FromDateTime(local.DateTime);
        }
    }

    /// <summary>
    /// Time zone lookup for the "today" default. Uses <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>,
    /// which reads IANA ids (<c>Asia/Kolkata</c>) on Linux, macOS and, from .NET 6, on Windows (with ICU).
    /// On .NET Framework, which knows only Windows ids, it maps the common IANA ids to their Windows
    /// zones; UTC offsets (<c>+05:30</c>) work everywhere. An unknown zone gives the UTC date.
    /// </summary>
    internal static class TimeZones
    {
        private static readonly Regex OffsetPattern = new Regex(
            @"^(?:UTC|GMT)?\s*([+-])(\d{1,2})(?::?(\d{2}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>IANA id to Windows id, for runtimes whose time zone database is Windows-only.</summary>
        private static readonly Dictionary<string, string> IanaToWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Asia/Kolkata"] = "India Standard Time",
            ["Asia/Calcutta"] = "India Standard Time",
            ["Asia/Kathmandu"] = "Nepal Standard Time",
            ["Asia/Katmandu"] = "Nepal Standard Time",
            ["Asia/Dhaka"] = "Bangladesh Standard Time",
            ["Asia/Colombo"] = "Sri Lanka Standard Time",
            ["Asia/Karachi"] = "Pakistan Standard Time",
            ["Asia/Kabul"] = "Afghanistan Standard Time",
            ["Asia/Thimphu"] = "Bangladesh Standard Time",
            ["Indian/Maldives"] = "West Asia Standard Time",
            ["Asia/Dubai"] = "Arabian Standard Time",
            ["Asia/Muscat"] = "Arabian Standard Time",
            ["Asia/Qatar"] = "Arab Standard Time",
            ["Asia/Riyadh"] = "Arab Standard Time",
            ["Asia/Kuwait"] = "Arab Standard Time",
            ["Asia/Bahrain"] = "Arab Standard Time",
            ["Asia/Singapore"] = "Singapore Standard Time",
            ["Asia/Kuala_Lumpur"] = "Singapore Standard Time",
            ["Asia/Bangkok"] = "SE Asia Standard Time",
            ["Asia/Jakarta"] = "SE Asia Standard Time",
            ["Asia/Hong_Kong"] = "China Standard Time",
            ["Asia/Shanghai"] = "China Standard Time",
            ["Asia/Tokyo"] = "Tokyo Standard Time",
            ["Asia/Yangon"] = "Myanmar Standard Time",
            ["Indian/Mauritius"] = "Mauritius Standard Time",
            ["Africa/Nairobi"] = "E. Africa Standard Time",
            ["Africa/Johannesburg"] = "South Africa Standard Time",
            ["Africa/Lagos"] = "W. Central Africa Standard Time",
            ["Europe/London"] = "GMT Standard Time",
            ["Europe/Dublin"] = "GMT Standard Time",
            ["Europe/Paris"] = "Romance Standard Time",
            ["Europe/Berlin"] = "W. Europe Standard Time",
            ["Europe/Amsterdam"] = "W. Europe Standard Time",
            ["Europe/Rome"] = "W. Europe Standard Time",
            ["Europe/Zurich"] = "W. Europe Standard Time",
            ["Europe/Moscow"] = "Russian Standard Time",
            ["America/New_York"] = "Eastern Standard Time",
            ["America/Toronto"] = "Eastern Standard Time",
            ["America/Chicago"] = "Central Standard Time",
            ["America/Denver"] = "Mountain Standard Time",
            ["America/Phoenix"] = "US Mountain Standard Time",
            ["America/Los_Angeles"] = "Pacific Standard Time",
            ["America/Vancouver"] = "Pacific Standard Time",
            ["America/Port_of_Spain"] = "SA Western Standard Time",
            ["America/Guyana"] = "SA Western Standard Time",
            ["America/Paramaribo"] = "SA Eastern Standard Time",
            ["Pacific/Fiji"] = "Fiji Standard Time",
            ["Pacific/Auckland"] = "New Zealand Standard Time",
            ["Australia/Sydney"] = "AUS Eastern Standard Time",
            ["Australia/Melbourne"] = "AUS Eastern Standard Time",
            ["Australia/Brisbane"] = "E. Australia Standard Time",
            ["Australia/Perth"] = "W. Australia Standard Time",
            ["Etc/UTC"] = "UTC",
            ["UTC"] = "UTC",
        };

        /// <summary>
        /// Old and current names of the same IANA zone, both ways. Some systems have only one of them:
        /// Ubuntu 24.04 and slim container images ship the old names (<c>Asia/Calcutta</c>) only in the
        /// optional tzdata-legacy package, and ICU turns the Windows id back into the old name.
        /// </summary>
        private static readonly Dictionary<string, string> IanaAliases = BuildAliases(
            "Asia/Calcutta", "Asia/Kolkata",
            "Asia/Katmandu", "Asia/Kathmandu",
            "Asia/Dacca", "Asia/Dhaka",
            "Asia/Thimbu", "Asia/Thimphu",
            "Asia/Rangoon", "Asia/Yangon",
            "Asia/Saigon", "Asia/Ho_Chi_Minh",
            "Asia/Ujung_Pandang", "Asia/Makassar",
            "Europe/Kiev", "Europe/Kyiv",
            "America/Buenos_Aires", "America/Argentina/Buenos_Aires",
            "America/Indianapolis", "America/Indiana/Indianapolis",
            "Pacific/Truk", "Pacific/Chuuk",
            "US/Eastern", "America/New_York",
            "US/Central", "America/Chicago",
            "US/Mountain", "America/Denver",
            "US/Pacific", "America/Los_Angeles",
            "GB", "Europe/London");

        private static Dictionary<string, string> BuildAliases(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                map[pairs[i]] = pairs[i + 1];
                if (!map.ContainsKey(pairs[i + 1]))
                {
                    map[pairs[i + 1]] = pairs[i];
                }
            }

            return map;
        }

        /// <summary>The UTC offset of <paramref name="tz"/> at <paramref name="now"/>, or null when it is unknown.</summary>
        internal static TimeSpan? OffsetAt(string? tz, DateTimeOffset now)
        {
            if (tz == null)
            {
                return null;
            }

            string id = tz.Trim();
            if (id.Length == 0)
            {
                return null;
            }

            if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase) || id.Equals("GMT", StringComparison.OrdinalIgnoreCase)
                || id.Equals("Z", StringComparison.OrdinalIgnoreCase) || id.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.Zero;
            }

            Match m = OffsetPattern.Match(id);
            if (m.Success)
            {
                int hours = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int minutes = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                if (hours > 14 || minutes > 59)
                {
                    return null;
                }

                var offset = new TimeSpan(hours, minutes, 0);
                return m.Groups[1].Value == "-" ? offset.Negate() : offset;
            }

            TimeZoneInfo? zone = Find(id);
            return zone?.GetUtcOffset(now);
        }

        internal static TimeZoneInfo? Find(string id)
        {
            TimeZoneInfo? zone = TryFind(id);
            if (zone != null)
            {
                return zone;
            }

            // The other name of the same zone (Asia/Calcutta <-> Asia/Kolkata), then the Windows id.
            if (IanaAliases.TryGetValue(id, out string? alias) && (zone = TryFind(alias)) != null)
            {
                return zone;
            }

            if (IanaToWindows.TryGetValue(id, out string? windowsId)
                || (alias != null && IanaToWindows.TryGetValue(alias, out windowsId)))
            {
                return TryFind(windowsId);
            }

            return null;
        }

        private static TimeZoneInfo? TryFind(string id)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }

            return null;
        }
    }
}
