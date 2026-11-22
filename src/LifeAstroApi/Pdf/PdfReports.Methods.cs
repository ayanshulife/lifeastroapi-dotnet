using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf.Templates;

namespace LifeAstroApi.Pdf
{
    // One Render*Html and one *PdfAsync per report, each from a JsonElement or from JSON text.
    public static partial class PdfReports
    {
        /// <summary>The kundli lite report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.KundliLiteAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderKundliLiteHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, KundliLite.renderKundliLiteHtml, "kundli lite report");

        /// <summary>The kundli lite report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderKundliLiteHtml(string reportJson, PdfBranding? branding = null) =>
            RenderKundliLiteHtml(ParseReport(reportJson), branding);

        /// <summary>The detailed kundli report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.KundliDetailedAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderKundliDetailedHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, KundliDetailed.renderKundliDetailedHtml, "detailed kundli report");

        /// <summary>The detailed kundli report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderKundliDetailedHtml(string reportJson, PdfBranding? branding = null) =>
            RenderKundliDetailedHtml(ParseReport(reportJson), branding);

        /// <summary>The Brihad (comprehensive) kundli report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.KundliBrihadAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderKundliBrihadHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, KundliBrihad.renderKundliBrihadHtml, "Brihad (comprehensive) kundli report");

        /// <summary>The Brihad (comprehensive) kundli report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderKundliBrihadHtml(string reportJson, PdfBranding? branding = null) =>
            RenderKundliBrihadHtml(ParseReport(reportJson), branding);

        /// <summary>The match-making (kundli milan) report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.MatchMakingAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderMatchMakingHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, MatchMaking.renderMatchMakingHtml, "match-making (kundli milan) report");

        /// <summary>The match-making (kundli milan) report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderMatchMakingHtml(string reportJson, PdfBranding? branding = null) =>
            RenderMatchMakingHtml(ParseReport(reportJson), branding);

        /// <summary>The dasha analysis report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.DashaAnalysisAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderDashaAnalysisHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, DashaAnalysis.renderDashaAnalysisHtml, "dasha analysis report");

        /// <summary>The dasha analysis report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderDashaAnalysisHtml(string reportJson, PdfBranding? branding = null) =>
            RenderDashaAnalysisHtml(ParseReport(reportJson), branding);

        /// <summary>The numerology report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.NumerologyAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderNumerologyHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, Numerology.renderNumerologyHtml, "numerology report");

        /// <summary>The numerology report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderNumerologyHtml(string reportJson, PdfBranding? branding = null) =>
            RenderNumerologyHtml(ParseReport(reportJson), branding);

        /// <summary>The Sade Sati report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.SadeSatiAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderSadeSatiHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, SadeSati.renderSadeSatiHtml, "Sade Sati report");

        /// <summary>The Sade Sati report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderSadeSatiHtml(string reportJson, PdfBranding? branding = null) =>
            RenderSadeSatiHtml(ParseReport(reportJson), branding);

        /// <summary>The Mangal Dosha (Manglik) report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.MangalDoshaAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderMangalDoshaHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, MangalDosha.renderMangalDoshaHtml, "Mangal Dosha (Manglik) report");

        /// <summary>The Mangal Dosha (Manglik) report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderMangalDoshaHtml(string reportJson, PdfBranding? branding = null) =>
            RenderMangalDoshaHtml(ParseReport(reportJson), branding);

        /// <summary>The Varshaphal (annual chart) report as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.VarshaphalAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderVarshaphalHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, Varshaphal.renderVarshaphalHtml, "Varshaphal (annual chart) report");

        /// <summary>The Varshaphal (annual chart) report as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderVarshaphalHtml(string reportJson, PdfBranding? branding = null) =>
            RenderVarshaphalHtml(ParseReport(reportJson), branding);

        /// <summary>The horoscope report (daily, weekly or monthly) as a complete, branded HTML document.</summary>
        /// <param name="report">The JSON that <c>client.Reports.HoroscopeDailyAsync</c>, <c>HoroscopeWeeklyAsync</c> or <c>HoroscopeMonthlyAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report.</exception>
        public static string RenderHoroscopeHtml(JsonElement report, PdfBranding? branding = null) =>
            Render(report, branding, Horoscope.renderHoroscopeHtml, "horoscope report (daily, weekly or monthly)");

        /// <summary>The horoscope report (daily, weekly or monthly) as HTML, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <returns>The HTML.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON.</exception>
        public static string RenderHoroscopeHtml(string reportJson, PdfBranding? branding = null) =>
            RenderHoroscopeHtml(ParseReport(reportJson), branding);

        /// <summary>The kundli lite report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.KundliLiteAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> KundliLitePdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderKundliLiteHtml, options, engine, cancellationToken);

        /// <summary>The kundli lite report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> KundliLitePdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await KundliLitePdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The detailed kundli report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.KundliDetailedAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> KundliDetailedPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderKundliDetailedHtml, options, engine, cancellationToken);

        /// <summary>The detailed kundli report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> KundliDetailedPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await KundliDetailedPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// The Brihad (comprehensive) kundli report as PDF bytes, with a running header and footer, a table of contents with the real page
        /// numbers and PDF bookmarks.
        /// </summary>
        /// <remarks>
        /// It prints twice and runs to a few hundred pages: make it in a background job rather than while a
        /// request waits.
        /// </remarks>
        /// <param name="report">The JSON that <c>client.Reports.KundliBrihadAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> KundliBrihadPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            BrihadPdfAsync(report, branding, options, engine, cancellationToken);

        /// <summary>The Brihad (comprehensive) kundli report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> KundliBrihadPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await KundliBrihadPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The match-making (kundli milan) report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.MatchMakingAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> MatchMakingPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderMatchMakingHtml, options, engine, cancellationToken);

        /// <summary>The match-making (kundli milan) report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> MatchMakingPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await MatchMakingPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The dasha analysis report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.DashaAnalysisAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> DashaAnalysisPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderDashaAnalysisHtml, options, engine, cancellationToken);

        /// <summary>The dasha analysis report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> DashaAnalysisPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await DashaAnalysisPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The numerology report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.NumerologyAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> NumerologyPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderNumerologyHtml, options, engine, cancellationToken);

        /// <summary>The numerology report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> NumerologyPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await NumerologyPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The Sade Sati report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.SadeSatiAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> SadeSatiPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderSadeSatiHtml, options, engine, cancellationToken);

        /// <summary>The Sade Sati report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> SadeSatiPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await SadeSatiPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The Mangal Dosha (Manglik) report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.MangalDoshaAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> MangalDoshaPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderMangalDoshaHtml, options, engine, cancellationToken);

        /// <summary>The Mangal Dosha (Manglik) report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> MangalDoshaPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await MangalDoshaPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The Varshaphal (annual chart) report as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.VarshaphalAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> VarshaphalPdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderVarshaphalHtml, options, engine, cancellationToken);

        /// <summary>The Varshaphal (annual chart) report as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> VarshaphalPdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await VarshaphalPdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);

        /// <summary>The horoscope report (daily, weekly or monthly) as PDF bytes.</summary>
        /// <param name="report">The JSON that <c>client.Reports.HoroscopeDailyAsync</c>, <c>HoroscopeWeeklyAsync</c> or <c>HoroscopeMonthlyAsync</c> returns.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="report"/> is not that report, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static Task<byte[]> HoroscopePdfAsync(JsonElement report, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            PrintAsync(report, branding, RenderHoroscopeHtml, options, engine, cancellationToken);

        /// <summary>The horoscope report (daily, weekly or monthly) as PDF bytes, from the report's JSON text.</summary>
        /// <param name="reportJson">The report as JSON text.</param>
        /// <param name="branding">Your branding, over the report's own.</param>
        /// <param name="options">Page and timeout options.</param>
        /// <param name="engine">The engine that prints. Null launches a browser for this one call.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF.</returns>
        /// <exception cref="ArgumentException"><paramref name="reportJson"/> is not that report's JSON, or an option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">The engine could not print.</exception>
        public static async Task<byte[]> HoroscopePdfAsync(string reportJson, PdfBranding? branding = null, PdfOptions? options = null, IPdfEngine? engine = null, CancellationToken cancellationToken = default) =>
            await HoroscopePdfAsync(ParseReport(reportJson), branding, options, engine, cancellationToken).ConfigureAwait(false);
    }
}
