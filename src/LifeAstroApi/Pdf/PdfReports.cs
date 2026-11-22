using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf.Templates;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// The branded PDF reports, made on your side from the JSON that <c>client.Reports</c> returns. Nothing
    /// is sent to LifeAstroAPI for it.
    /// <code>
    /// JsonElement report = await client.Reports.KundliLiteAsync(parameters);
    /// byte[] pdf = await PdfReports.KundliLitePdfAsync(report, new PdfBranding { CompanyName = "Acme Astro" });
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Render*Html</c> is pure: no browser, process, file or network access, and the same output in every
    /// culture and time zone. Use it in a desktop or phone app that prints with the platform's web view,
    /// with <see cref="A4PrintOptions"/>.
    /// </para>
    /// <para>
    /// <c>*PdfAsync</c> prints that HTML with an <see cref="IPdfEngine"/>: a <see cref="ChromePdfEngine"/>
    /// (Chrome, Chromium or Microsoft Edge on your machine, the default) or a <see cref="GotenbergPdfEngine"/>.
    /// For many PDFs, create one engine and pass it to every call. All methods are thread-safe.
    /// </para>
    /// </remarks>
    public static partial class PdfReports
    {
        private const string MarkerUrl = "https://toc.lifeastroapi.invalid/T";

        private static readonly Regex Marker = new Regex(
            "<span style=\"color:#fff;font-size:1px;\">⟦T([0-9]+)⟧</span>",
            RegexOptions.CultureInvariant);

        private static readonly Regex MarkerToken = new Regex("⟦T[0-9]+⟧", RegexOptions.CultureInvariant);

        /// <summary>
        /// A4 and the reports' page margins, for print APIs that ignore the CSS <c>@page</c> rule (the
        /// Node.js SDK's <c>A4_PRINT_OPTIONS</c>).
        /// </summary>
        public static PdfPrintGeometry A4PrintOptions { get; } = new PdfPrintGeometry(595, 842, 51, 43, 62, 43);

        /// <summary>Prints a complete HTML document, your own or a <c>Render*Html</c> result, to PDF bytes.</summary>
        /// <param name="html">The document.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> HtmlToPdfAsync(
            string html,
            PdfOptions? options = null,
            IPdfEngine? engine = null,
            CancellationToken cancellationToken = default)
        {
            Wire.NotNull(html, nameof(html));
            if (engine != null)
            {
                return await PrintWithAsync(engine, html, options, cancellationToken).ConfigureAwait(false);
            }

            var own = new ChromePdfEngine();
            try
            {
                return await PrintWithAsync(own, html, options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The one way to every engine, the Node.js SDK's <c>htmlToPdf</c>: the options that need CSS go into
        /// the document here, so that an engine of your own honours them too.
        /// </summary>
        private static Task<byte[]> PrintWithAsync(
            IPdfEngine engine,
            string html,
            PdfOptions? options,
            CancellationToken cancellationToken) =>
            engine.HtmlToPdfAsync(PdfPrintCss.Apply(html, options), options, cancellationToken);

        private static async Task<byte[]> PrintAsync(
            JsonElement report,
            PdfBranding? branding,
            Func<JsonElement, PdfBranding?, string> render,
            PdfOptions? options,
            IPdfEngine? engine,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string html = render(report, branding);
            return await HtmlToPdfAsync(html, options, engine, cancellationToken).ConfigureAwait(false);
        }

        private static JsonElement ParseReport(string reportJson)
        {
            Wire.NotNull(reportJson, nameof(reportJson));
            try
            {
                // U+FEFF: text read from a file that starts with a byte order mark.
                using (JsonDocument document = JsonDocument.Parse(reportJson.TrimStart((char)0xFEFF)))
                {
                    return document.RootElement.Clone();
                }
            }
            catch (JsonException e)
            {
                throw new ArgumentException("Not valid JSON: " + e.Message, nameof(reportJson), e);
            }
        }

        private static Js ToReport(JsonElement report)
        {
            if (report.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "The report must be a JSON object, as client.Reports returns it; got " + report.ValueKind + ".",
                    nameof(report));
            }

            try
            {
                return JsJson.Convert(report);
            }
            catch (JsTypeException e)
            {
                throw new ArgumentException("Not a report: " + e.Message + ".", nameof(report), e);
            }
            catch (InvalidOperationException e)
            {
                throw new ArgumentException("The report holds a string that is not valid Unicode.", nameof(report), e);
            }
        }

        private static string Render(JsonElement report, PdfBranding? branding, Func<Js, Js, Js> template, string name) =>
            RenderTemplate(ToReport(report), branding, template, name);

        private static string RenderTemplate(Js data, PdfBranding? branding, Func<Js, Js, Js> template, string name)
        {
            try
            {
                return template(data, BrandingValue(branding)).Str();
            }
            catch (JsTypeException e)
            {
                throw new ArgumentException(
                    "The JSON does not have the shape of a " + name + " (" + e.Message + ").",
                    "report",
                    e);
            }
        }

        private static Js BrandingValue(PdfBranding? branding)
        {
            if (branding is null)
            {
                return Js.Undefined;
            }

            return Js.Obj(
                Js.Prop("companyName", Js.S(branding.CompanyName)),
                Js.Prop("logoUrl", Js.S(branding.LogoUrl)),
                Js.Prop("primaryColor", Js.S(branding.PrimaryColor)),
                Js.Prop("secondaryColor", Js.S(branding.SecondaryColor)),
                Js.Prop("footerText", Js.S(branding.FooterText)),
                Js.Prop("watermarkText", Js.S(branding.WatermarkText)));
        }

        private static async Task<byte[]> BrihadPdfAsync(
            JsonElement report,
            PdfBranding? branding,
            PdfOptions? options,
            IPdfEngine? engine,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Js data = ToReport(report);
            string baseHtml = RenderTemplate(data, branding, KundliBrihad.renderKundliBrihadHtml, "Brihad kundli report");
            Js reportBranding = data.Get("Branding");
            Js given = BrandingValue(branding);
            Js margin = KundliBrihadSections.BRIHAD_PDF_MARGIN;

            // The templates take both brandings and resolve them as the pages do: only so can the palette
            // know which colours are the caller's.
            PdfOptions page = (options ?? new PdfOptions()).ForBrihad(
                KundliBrihadSections.brihadHeaderTemplate(reportBranding, given).Str(),
                KundliBrihadSections.brihadFooterTemplate(reportBranding, given).Str(),
                new PdfMargin(
                    margin.Get("top").Str(),
                    margin.Get("bottom").Str(),
                    margin.Get("left").Str(),
                    margin.Get("right").Str()));
            string heading = TocHeading(data);

            if (engine != null)
            {
                return await PrintBrihadAsync(baseHtml, heading, page, engine, cancellationToken).ConfigureAwait(false);
            }

            var browser = new ChromePdfEngine();
            try
            {
                return await PrintBrihadAsync(baseHtml, heading, page, browser, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal static string TocHeading(Js data)
        {
            Js locale = Shared.safeLocale(data.Get("Locale")).NonNullish ?? Js.S("en");
            return (Locales.own(Locales.TOC_HEADING, locale).NonNullish ?? Locales.TOC_HEADING.Get("en")).Str();
        }

        private static async Task<byte[]> PrintBrihadAsync(
            string baseHtml,
            string heading,
            PdfOptions options,
            IPdfEngine engine,
            CancellationToken cancellationToken)
        {
            Js tagged = KundliBrihadSections.tagBrihadSectionsForToc(Js.S(baseHtml));
            string taggedHtml = tagged.Get("html").Str();
            var titles = new List<string>();
            foreach (Js title in tagged.Get("titles").Iterate())
            {
                titles.Add(title.Str());
            }

            if (titles.Count == 0)
            {
                return await PrintWithAsync(engine, baseHtml, options, cancellationToken).ConfigureAwait(false);
            }

            // Pass 1: a table of contents with the right number of rows, and every section marker turned
            // into a link, so that the PDF itself tells on which page each section starts.
            var placeholder = new List<TocEntry>();
            for (int i = 0; i < titles.Count; i++)
            {
                placeholder.Add(new TocEntry(titles[i], i + 1));
            }

            string first = LinkMarkers(InsertTocAfterCover(taggedHtml, BuildToc(placeholder, heading)));
            byte[] firstPdf = await PrintWithAsync(engine, first, options, cancellationToken).ConfigureAwait(false);

            var entries = new List<TocEntry>();
            try
            {
                Dictionary<string, int> pages = PdfOutline.LinkTargetPages(firstPdf);
                for (int i = 0; i < titles.Count; i++)
                {
                    if (pages.TryGetValue(MarkerUrl + i.ToString(CultureInfo.InvariantCulture), out int page))
                    {
                        entries.Add(new TocEntry(titles[i], page));
                    }
                }
            }
            catch (LifeAstroPdfException)
            {
                // A PDF whose objects cannot be read: handled as "no page numbers" below.
                entries.Clear();
            }

            // Without page numbers, no table of contents rather than a wrong one.
            if (entries.Count == 0)
            {
                return await PrintWithAsync(engine, baseHtml, options, cancellationToken).ConfigureAwait(false);
            }

            // Pass 2: the real page numbers, then the bookmarks from the same entries. The markers have done
            // their job: a no-break space keeps the same line box without leaving a token in the PDF's text.
            string second = InsertTocAfterCover(MarkerToken.Replace(taggedHtml, "&nbsp;"), BuildToc(entries, heading));
            byte[] pdf = await PrintWithAsync(engine, second, options, cancellationToken).ConfigureAwait(false);

            var bookmarks = new List<PdfOutlineItem>();
            foreach (TocEntry entry in entries)
            {
                bool part = Locales.PART_PREFIX_RE.Test(Js.S(entry.Title).Trim()) ? true : false;
                bookmarks.Add(new PdfOutlineItem(entry.Title, entry.Page, part ? 1 : 2));
            }

            try
            {
                return PdfOutline.AddOutline(pdf, bookmarks);
            }
            catch (LifeAstroPdfException)
            {
                // The report is complete; only the sidebar would be missing.
                return pdf;
            }
        }

        private static string BuildToc(List<TocEntry> entries, string heading)
        {
            var rows = new Js[entries.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = Js.Obj(Js.Prop("title", Js.S(entries[i].Title)), Js.Prop("page", Js.N(entries[i].Page)));
            }

            return KundliBrihadSections.buildBrihadToc(Js.Arr(rows), Js.S(heading)).Str();
        }

        private static string LinkMarkers(string html) => Marker.Replace(
            html,
            "<span style=\"color:#fff;font-size:1px;\"><a href=\"" + MarkerUrl
                + "$1\" style=\"color:#fff;text-decoration:none;\">⟦T$1⟧</a></span>");

        private static string InsertTocAfterCover(string html, string toc)
        {
            const string End = "</section>";
            int coverEnd = html.IndexOf(End, StringComparison.Ordinal);
            if (coverEnd < 0)
            {
                return toc + html;
            }

            int at = coverEnd + End.Length;
            return html.Substring(0, at) + "\n" + toc + "\n" + html.Substring(at);
        }

        private readonly struct TocEntry
        {
            internal TocEntry(string title, int page)
            {
                Title = title;
                Page = page;
            }

            internal string Title { get; }

            internal int Page { get; }
        }
    }
}
