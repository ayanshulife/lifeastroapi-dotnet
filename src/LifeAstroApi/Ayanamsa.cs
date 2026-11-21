using System;

namespace LifeAstroApi
{
    /// <summary>
    /// Sidereal ayanamsa for Vedic calculations, sent as <c>ayanamsa</c>. The API uses
    /// <see cref="Lahiri"/> when none is given.
    /// </summary>
    public enum Ayanamsa
    {
        /// <summary>Lahiri (Chitrapaksha), <c>lahiri</c>, the default.</summary>
        Lahiri,

        /// <summary>Krishnamurti (KP), <c>krishnamurti</c>.</summary>
        Krishnamurti,

        /// <summary>B. V. Raman, <c>raman</c>.</summary>
        Raman,

        /// <summary>Fagan-Bradley, <c>fagan</c>.</summary>
        Fagan,

        /// <summary>Sri Yukteshwar, <c>yukteshwar</c>.</summary>
        Yukteshwar,

        /// <summary>True Chitrapaksha, <c>true_chitrapaksha</c>.</summary>
        TrueChitrapaksha,
    }

    /// <summary>Wire values of <see cref="Ayanamsa"/>.</summary>
    public static class AyanamsaExtensions
    {
        private static readonly string[] Values = { "lahiri", "krishnamurti", "raman", "fagan", "yukteshwar", "true_chitrapaksha" };

        /// <summary>The value sent on the wire, e.g. <c>"krishnamurti"</c>.</summary>
        /// <param name="ayanamsa">The ayanamsa.</param>
        /// <returns>The wire value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Ayanamsa"/>.</exception>
        public static string ToWireValue(this Ayanamsa ayanamsa)
        {
            int i = (int)ayanamsa;
            if (i < 0 || i >= Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(ayanamsa), ayanamsa, "Not a defined Ayanamsa value.");
            }

            return Values[i];
        }

        /// <summary>The ayanamsa for a wire value in any case, e.g. <c>"krishnamurti"</c>.</summary>
        /// <param name="value">The wire value.</param>
        /// <returns>The matching ayanamsa.</returns>
        /// <exception cref="ArgumentException">The value is unknown.</exception>
        public static Ayanamsa FromWireValue(string value)
        {
            if (value != null)
            {
                string v = value.Trim().ToLowerInvariant();
                for (int i = 0; i < Values.Length; i++)
                {
                    if (Values[i] == v)
                    {
                        return (Ayanamsa)i;
                    }
                }
            }

            throw new ArgumentException("Unknown ayanamsa \"" + value + "\"; use one of " + string.Join(", ", Values) + ".", nameof(value));
        }
    }
}
