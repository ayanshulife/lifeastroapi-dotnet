using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// The options that a document's own print CSS would otherwise win over, as CSS that wins over it: the
    /// Node.js SDK's <c>printOptionsCss</c> (src/pdf/render.ts), character for character.
    /// </summary>
    internal static class PdfPrintCss
    {
        // JavaScript's /^([0-9]+(?:\.[0-9]+)?|\.[0-9]+)(px|in|cm|mm)?$/i
        private static readonly Regex Length = new Regex(
            "^([0-9]+(?:\\.[0-9]+)?|\\.[0-9]+)([pP][xX]|[iI][nN]|[cC][mM]|[mM][mM])?\\z",
            RegexOptions.CultureInvariant);

        // The first rule: economy printing paints a see-through fill solid white, which cut the watermark into strips.
        private const string NoBackgrounds =
            "body *, body *::before, body *::after { background: transparent !important; box-shadow: none !important; }\n"
            + "html { -webkit-print-color-adjust: economy !important; print-color-adjust: economy !important; }\n"
            + "th, .part-banner .part-h2, .toc-banner, .summary-card, .num-chip, .number-block .number, .moon-sign-badge, "
            + ".year-badge, .remedy-block .category, .dosha-block .severity, .yoga-block .name::before "
            + "{ color: #1a1a1a !important; }\n";

        /// <summary>
        /// The document as it is printed: unchanged when no option needs CSS, and not extended a second time
        /// when <see cref="PdfReports"/> has already handed it over so.
        /// </summary>
        internal static string Apply(string html, PdfOptions? options)
        {
            string css = options is null ? string.Empty : Build(options);
            if (css.Length == 0)
            {
                return html;
            }

            string style = "\n<style>" + css + "</style>";
            return html.EndsWith(style, StringComparison.Ordinal) ? html : html + style;
        }

        /// <summary>The CSS, or empty when no option needs any.</summary>
        internal static string Build(PdfOptions options)
        {
            var page = new List<string>();
            AddMargin(page, "top", options.Margin?.Top);
            AddMargin(page, "right", options.Margin?.Right);
            AddMargin(page, "bottom", options.Margin?.Bottom);
            AddMargin(page, "left", options.Margin?.Left);

            // An explicit PreferCssPageSize keeps its meaning: true = the CSS size, false = scale to the paper.
            // Portrait A4 is the default, so it never overrides a CSS size.
            if (options.PreferCssPageSize is null && (options.Format != PdfPaperFormat.A4 || options.Landscape))
            {
                string? size = PaperSize(options.Format, options.Landscape);
                if (size != null)
                {
                    page.Add("size: " + size + " !important;");
                }
            }

            if (options.DisplayHeaderFooter == true && options.FooterTemplate != null)
            {
                page.Add("@bottom-center { content: none !important; }");
            }

            string css = page.Count > 0 ? "@page { " + string.Join(" ", page) + " }\n" : string.Empty;

            // The reports ask for exact colours, which prints backgrounds anyway; without them white text needs ink.
            return options.PrintBackground ? css : css + NoBackgrounds;
        }

        private static void AddMargin(List<string> page, string side, string? value)
        {
            if (value is null)
            {
                return;
            }

            // No unit is px, as the engines read it; anything else is no CSS at all.
            Match match = Length.Match(JsText.Trim(value));
            if (match.Success)
            {
                string unit = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : "px";
                page.Add("margin-" + side + ": " + match.Groups[1].Value + unit + " !important;");
            }
        }

        private static string? PaperSize(PdfPaperFormat format, bool landscape)
        {
            switch (format)
            {
                case PdfPaperFormat.A3: return landscape ? "420mm 297mm" : "297mm 420mm";
                case PdfPaperFormat.A4: return landscape ? "297mm 210mm" : "210mm 297mm";
                case PdfPaperFormat.A5: return landscape ? "210mm 148mm" : "148mm 210mm";
                case PdfPaperFormat.Letter: return landscape ? "11in 8.5in" : "8.5in 11in";
                case PdfPaperFormat.Legal: return landscape ? "14in 8.5in" : "8.5in 14in";
                case PdfPaperFormat.Tabloid: return landscape ? "17in 11in" : "11in 17in";
                default: return null;
            }
        }
    }
}
