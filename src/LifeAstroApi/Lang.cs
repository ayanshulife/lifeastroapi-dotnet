using System;

namespace LifeAstroApi
{
    /// <summary>
    /// Response language, passed as <c>lang</c> on every Vedic method that has it (panchang, calendar,
    /// festivals, eclipses, planet moments, transit, milan, muhurta, chart, prashna, numerology, dasha,
    /// varshaphal, ashtakavarga, Lal Kitab, horoscope, reports, narrative, remedies). The SDK sends the
    /// wire code (<see cref="LangExtensions.ToCode(Lang)"/>), as <c>lang</c> and <c>locale</c> where the
    /// Node.js SDK does.
    /// </summary>
    /// <remarks>
    /// Names, terms and readings come back in the language; ids, dates, numbers and explicit
    /// <c>*_en</c> / <c>*_hi</c> fields stay as they are. All ten languages are complete. Western, tarot,
    /// geo and meta methods answer in English (Western narrative also in Hindi and Marathi).
    /// </remarks>
    public enum Lang
    {
        /// <summary>English, <c>en</c> (the default).</summary>
        En,

        /// <summary>Hindi, <c>hi</c>.</summary>
        Hi,

        /// <summary>Marathi, <c>mr</c>.</summary>
        Mr,

        /// <summary>Gujarati, <c>gu</c>.</summary>
        Gu,

        /// <summary>Bengali, <c>bn</c>.</summary>
        Bn,

        /// <summary>Telugu, <c>te</c>.</summary>
        Te,

        /// <summary>Tamil, <c>ta</c>.</summary>
        Ta,

        /// <summary>Kannada, <c>kn</c>.</summary>
        Kn,

        /// <summary>Malayalam, <c>ml</c>.</summary>
        Ml,

        /// <summary>Punjabi, <c>pa</c>.</summary>
        Pa,
    }

    /// <summary>Wire codes and names of <see cref="Lang"/>.</summary>
    public static class LangExtensions
    {
        private static readonly string[] Codes = { "en", "hi", "mr", "gu", "bn", "te", "ta", "kn", "ml", "pa" };

        private static readonly string[] EnglishNames =
        {
            "English", "Hindi", "Marathi", "Gujarati", "Bengali", "Telugu", "Tamil", "Kannada", "Malayalam", "Punjabi",
        };

        /// <summary>The code sent on the wire, e.g. <c>"gu"</c>.</summary>
        /// <param name="lang">The language.</param>
        /// <returns>The ISO 639-1 code.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Lang"/>.</exception>
        public static string ToCode(this Lang lang) => Codes[Index(lang)];

        /// <summary>The language's English name, e.g. <c>"Gujarati"</c>.</summary>
        /// <param name="lang">The language.</param>
        /// <returns>The English name.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Lang"/>.</exception>
        public static string EnglishName(this Lang lang) => EnglishNames[Index(lang)];

        /// <summary>
        /// Whether every reading is available in this language. True for all ten languages since
        /// Punjabi became complete (2026-11-21).
        /// </summary>
        /// <param name="lang">The language.</param>
        /// <returns><c>true</c> when names, terms and readings are all translated.</returns>
        public static bool IsComplete(this Lang lang)
        {
            Index(lang);
            return true;
        }

        /// <summary>The language for a code in any case: <c>"gu"</c>, <c>"GU"</c>, <c>" hi "</c>.</summary>
        /// <param name="code">A language code such as <c>"hi"</c>.</param>
        /// <returns>The matching language.</returns>
        /// <exception cref="ArgumentException">The code is not one of the ten languages.</exception>
        public static Lang FromCode(string code)
        {
            if (TryFromCode(code, out Lang lang))
            {
                return lang;
            }

            throw new ArgumentException(
                "Unknown language \"" + code + "\"; use one of " + string.Join(", ", Codes) + ".", nameof(code));
        }

        /// <summary>The language for a code in any case, without throwing.</summary>
        /// <param name="code">A language code such as <c>"hi"</c>, or null.</param>
        /// <param name="lang">The matching language, or <see cref="Lang.En"/> when there is none.</param>
        /// <returns><c>true</c> when the code is one of the ten languages.</returns>
        public static bool TryFromCode(string? code, out Lang lang)
        {
            if (code != null)
            {
                string c = code.Trim().ToLowerInvariant();
                for (int i = 0; i < Codes.Length; i++)
                {
                    if (Codes[i] == c)
                    {
                        lang = (Lang)i;
                        return true;
                    }
                }
            }

            lang = Lang.En;
            return false;
        }

        private static int Index(Lang lang)
        {
            int i = (int)lang;
            if (i < 0 || i >= Codes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(lang), lang, "Not a defined Lang value.");
            }

            return i;
        }
    }
}
