using System;

namespace LifeAstroApi
{
    /// <summary>
    /// House system, sent as <c>house_system</c>. Vedic charts default to whole sign, Western charts
    /// to Placidus. Western methods accept <see cref="Placidus"/>, <see cref="WholeSign"/>,
    /// <see cref="Koch"/> and <see cref="Equal"/>.
    /// </summary>
    public enum HouseSystem
    {
        /// <summary>Whole sign, <c>whole_sign</c>.</summary>
        WholeSign,

        /// <summary>Equal house, <c>equal</c>.</summary>
        Equal,

        /// <summary>Placidus, <c>placidus</c>.</summary>
        Placidus,

        /// <summary>Koch, <c>koch</c>.</summary>
        Koch,

        /// <summary>Porphyry, <c>porphyry</c>.</summary>
        Porphyry,

        /// <summary>Campanus, <c>campanus</c>.</summary>
        Campanus,

        /// <summary>Regiomontanus, <c>regiomontanus</c>.</summary>
        Regiomontanus,
    }

    /// <summary>Wire values of <see cref="HouseSystem"/>.</summary>
    public static class HouseSystemExtensions
    {
        // The wire values the API reads (it also accepts "whole" and "w" for whole sign).
        private static readonly string[] Values = { "whole_sign", "equal", "placidus", "koch", "porphyry", "campanus", "regiomontanus" };

        /// <summary>The value sent on the wire, e.g. <c>"placidus"</c>.</summary>
        /// <param name="houseSystem">The house system.</param>
        /// <returns>The wire value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HouseSystem"/>.</exception>
        public static string ToWireValue(this HouseSystem houseSystem)
        {
            int i = (int)houseSystem;
            if (i < 0 || i >= Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(houseSystem), houseSystem, "Not a defined HouseSystem value.");
            }

            return Values[i];
        }

        /// <summary>
        /// The house system for a wire value in any case: <c>"placidus"</c>, <c>"WHOLE_SIGN"</c>,
        /// <c>"whole-sign"</c>.
        /// </summary>
        /// <param name="value">The wire value.</param>
        /// <returns>The matching house system.</returns>
        /// <exception cref="ArgumentException">The value is unknown.</exception>
        public static HouseSystem FromWireValue(string value)
        {
            if (value != null)
            {
                string v = value.Trim().ToLowerInvariant().Replace('-', '_');
                for (int i = 0; i < Values.Length; i++)
                {
                    if (Values[i] == v)
                    {
                        return (HouseSystem)i;
                    }
                }
            }

            throw new ArgumentException("Unknown house system \"" + value + "\"; use one of " + string.Join(", ", Values) + ".", nameof(value));
        }
    }
}
