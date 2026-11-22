using LifeAstroApi.Pdf.Templates;

namespace LifeAstroApi.Pdf
{
    /// <summary>The text helpers of the report templates, for HTML of your own.</summary>
    public static class PdfText
    {
        /// <summary>HTML-escapes text, after <see cref="Humanize"/>, as the templates do for every value.</summary>
        /// <param name="value">The text; null gives an empty string.</param>
        /// <returns>Text that is safe inside HTML.</returns>
        public static string Escape(string? value) => Shared.esc(Js.S(value)).Str();

        /// <summary>A reading's body: <see cref="CleanNarrative"/>, then <see cref="Escape"/>.</summary>
        /// <param name="value">The text; null gives an empty string.</param>
        /// <param name="locale">The language code, default <c>en</c>.</param>
        /// <returns>Text that is safe inside HTML.</returns>
        public static string EscapeBody(string? value, string? locale = "en") =>
            Shared.escBody(Js.S(value), Js.S(locale)).Str();

        /// <summary>Turns dashes used in prose, and arrows, into commas (a lone dash stays).</summary>
        /// <param name="text">The text; null gives an empty string.</param>
        /// <returns>The text.</returns>
        public static string Humanize(string? text) => Shared.humanize(Js.S(text ?? string.Empty)).Str();

        /// <summary>Makes a reading address the reader ("you") and drops its repeated filler paragraph.</summary>
        /// <param name="text">The text; null gives an empty string.</param>
        /// <param name="locale">The language code, default <c>en</c>.</param>
        /// <returns>The text.</returns>
        public static string CleanNarrative(string? text, string? locale = "en") =>
            Shared.cleanNarrative(Js.S(text ?? string.Empty), Js.S(locale)).Str();
    }
}
