using System;
using System.Globalization;

namespace LifeAstroApi
{
    /// <summary>
    /// A location and its time zone, for panchang-style methods (<c>lat</c>, <c>lon</c>, <c>tz</c>).
    /// Immutable, with value equality.
    /// <code>
    /// var mumbai = new Place(19.076, 72.8777, "Asia/Kolkata");
    /// await client.Panchang.BasicAsync(new PanchangBasicParams(mumbai));
    /// </code>
    /// </summary>
    public sealed class Place : IEquatable<Place>
    {
        /// <summary>A place from its coordinates and time zone.</summary>
        /// <param name="lat">Latitude in decimal degrees, north positive.</param>
        /// <param name="lon">Longitude in decimal degrees, east positive.</param>
        /// <param name="tz">Time zone: an IANA name (<c>Asia/Kolkata</c>) or a UTC offset (<c>+05:30</c>).</param>
        /// <exception cref="ArgumentNullException"><paramref name="tz"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="tz"/> is blank, or a coordinate is not a finite number.</exception>
        public Place(double lat, double lon, string tz)
        {
            Wire.RequireFinite(lat, nameof(lat));
            Wire.RequireFinite(lon, nameof(lon));
            Lat = lat;
            Lon = lon;
            Tz = Wire.RequireText(tz, nameof(tz));
        }

        /// <summary>Latitude in decimal degrees, north positive.</summary>
        public double Lat { get; }

        /// <summary>Longitude in decimal degrees, east positive.</summary>
        public double Lon { get; }

        /// <summary>Time zone: an IANA name (<c>Asia/Kolkata</c>) or a UTC offset (<c>+05:30</c>).</summary>
        public string Tz { get; }

        /// <inheritdoc/>
        public bool Equals(Place? other) =>
            other != null && Lat.Equals(other.Lat) && Lon.Equals(other.Lon) && string.Equals(Tz, other.Tz, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as Place);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Lat.GetHashCode();
                h = (h * 397) ^ Lon.GetHashCode();
                return (h * 397) ^ StringComparer.Ordinal.GetHashCode(Tz);
            }
        }

        /// <summary>The place as <c>Place { Lat = 19.076, Lon = 72.8777, Tz = Asia/Kolkata }</c>.</summary>
        /// <returns>A readable form.</returns>
        public override string ToString() =>
            "Place { Lat = " + Lat.ToString("R", CultureInfo.InvariantCulture) + ", Lon = " + Lon.ToString("R", CultureInfo.InvariantCulture) + ", Tz = " + Tz + " }";
    }
}
