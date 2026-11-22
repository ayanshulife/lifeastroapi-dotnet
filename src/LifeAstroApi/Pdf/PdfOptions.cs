using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LifeAstroApi.Pdf
{
    /// <summary>Paper sizes. The report templates are A4.</summary>
    public enum PdfPaperFormat
    {
        /// <summary>210 x 297 mm (the default).</summary>
        A4,

        /// <summary>297 x 420 mm.</summary>
        A3,

        /// <summary>148 x 210 mm.</summary>
        A5,

        /// <summary>8.5 x 11 in.</summary>
        Letter,

        /// <summary>8.5 x 14 in.</summary>
        Legal,

        /// <summary>11 x 17 in.</summary>
        Tabloid,
    }

    /// <summary>
    /// Page margins as CSS lengths: <c>10mm</c>, <c>1cm</c>, <c>0.5in</c>, <c>20px</c>, or a number of pixels.
    /// A side you set wins over the document's own CSS. A side left null or blank keeps the document's
    /// margin, and in the Brihad kundli PDF that report's own (26mm, 16mm, 0, 0); <c>"0"</c> is no margin.
    /// </summary>
    public sealed class PdfMargin
    {
        /// <summary>Margins to set by property.</summary>
        public PdfMargin()
        {
        }

        /// <summary>The four margins.</summary>
        /// <param name="top">The top margin.</param>
        /// <param name="bottom">The bottom margin.</param>
        /// <param name="left">The left margin.</param>
        /// <param name="right">The right margin.</param>
        public PdfMargin(string? top, string? bottom, string? left = null, string? right = null)
        {
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
        }

        /// <summary>The top margin.</summary>
        public string? Top { get; set; }

        /// <summary>The bottom margin.</summary>
        public string? Bottom { get; set; }

        /// <summary>The left margin.</summary>
        public string? Left { get; set; }

        /// <summary>The right margin.</summary>
        public string? Right { get; set; }
    }

    /// <summary>
    /// How to print: the options of the Node.js SDK's <c>htmlToPdf</c>. The defaults suit the report templates.
    /// </summary>
    public sealed class PdfOptions
    {
        /// <summary>
        /// The paper size. Default A4, which keeps a document's own CSS page size; any other size, or
        /// <see cref="Landscape"/>, sets the paper (see <see cref="PreferCssPageSize"/>).
        /// </summary>
        public PdfPaperFormat Format { get; set; }

        /// <summary>Landscape orientation. Sets the paper, as a <see cref="Format"/> other than A4 does.</summary>
        public bool Landscape { get; set; }

        /// <summary>
        /// Print background colours and images. Default true (the reports are branded). False saves ink: text
        /// that the reports print in white on a coloured fill then prints dark, and a watermark stays whole.
        /// </summary>
        public bool PrintBackground { get; set; } = true;

        /// <summary>
        /// Leave it null for the usual behaviour: the document's CSS <c>@page</c> size, unless
        /// <see cref="Landscape"/> or a <see cref="Format"/> other than A4 sets the paper. True keeps the CSS
        /// size in any case; false scales the document to the paper.
        /// </summary>
        public bool? PreferCssPageSize { get; set; }

        /// <summary>The margins. A side you set wins over the document's own CSS; the others keep it.</summary>
        public PdfMargin? Margin { get; set; }

        /// <summary>
        /// Print <see cref="HeaderTemplate"/> and <see cref="FooterTemplate"/> in the page margins, the footer
        /// in place of a report's own footer line. Null (the default) is no, except in the Brihad kundli PDF,
        /// which has a running header and footer unless you set this to false.
        /// </summary>
        public bool? DisplayHeaderFooter { get; set; }

        /// <summary>
        /// The running header, with inline styles only. The browser fills elements with the class
        /// <c>pageNumber</c>, <c>totalPages</c>, <c>title</c>, <c>date</c> or <c>url</c>. In the Brihad kundli
        /// PDF yours, unless empty, replaces the report's.
        /// </summary>
        public string? HeaderTemplate { get; set; }

        /// <summary>The running footer, as <see cref="HeaderTemplate"/>.</summary>
        public string? FooterTemplate { get; set; }

        /// <summary>The scale, 0.1 to 2. Default 1.</summary>
        public double? Scale { get; set; }

        /// <summary>The time for the document, its images and its fonts to load. Default 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The time for the print itself, for each of the Brihad kundli's two prints. Default: four times
        /// <see cref="Timeout"/> (2 minutes). Raise it for a long Brihad on a busy server.
        /// </summary>
        public TimeSpan? PrintTimeout { get; set; }

        /// <summary>
        /// These options over the Brihad kundli's own: its running header and footer unless yours or off,
        /// and its margins, side by side replaced by yours.
        /// </summary>
        internal PdfOptions ForBrihad(string header, string footer, PdfMargin defaults) => new PdfOptions
        {
            Format = Format,
            Landscape = Landscape,
            PrintBackground = PrintBackground,
            PreferCssPageSize = PreferCssPageSize,
            Margin = MergeMargin(defaults, Margin),
            DisplayHeaderFooter = DisplayHeaderFooter ?? true,
            HeaderTemplate = string.IsNullOrEmpty(HeaderTemplate) ? header : HeaderTemplate,
            FooterTemplate = string.IsNullOrEmpty(FooterTemplate) ? footer : FooterTemplate,
            Scale = Scale,
            Timeout = Timeout,
            PrintTimeout = PrintTimeout,
        };

        /// <summary>The defaults, side by side replaced by what was given: null and blank keep the default.</summary>
        internal static PdfMargin MergeMargin(PdfMargin defaults, PdfMargin? given)
        {
            string? Side(string? fallback, string? value) =>
                value is null || JsText.Trim(value).Length == 0 ? fallback : value;

            return new PdfMargin(
                Side(defaults.Top, given?.Top),
                Side(defaults.Bottom, given?.Bottom),
                Side(defaults.Left, given?.Left),
                Side(defaults.Right, given?.Right));
        }
    }

    /// <summary>Validated options in what the engines send: inches and milliseconds. A snapshot, safe to share.</summary>
    internal sealed class PageSetup
    {
        private static readonly Regex Length = new Regex(
            "^(-?(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+)) *([pP][xX]|[iI][nN]|[cC][mM]|[mM][mM])?\\z",
            RegexOptions.CultureInvariant);

        private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        private PageSetup(PdfOptions o)
        {
            double width;
            double height;
            switch (o.Format)
            {
                case PdfPaperFormat.A4: width = 8.2677; height = 11.6929; break;
                case PdfPaperFormat.A3: width = 11.6929; height = 16.5354; break;
                case PdfPaperFormat.A5: width = 5.8268; height = 8.2677; break;
                case PdfPaperFormat.Letter: width = 8.5; height = 11; break;
                case PdfPaperFormat.Legal: width = 8.5; height = 14; break;
                case PdfPaperFormat.Tabloid: width = 11; height = 17; break;
                default: throw new ArgumentOutOfRangeException("options", "Unknown PdfOptions.Format: " + o.Format + ".");
            }

            // The orientation is in the width and height, so engines always print "portrait".
            PaperWidth = o.Landscape ? height : width;
            PaperHeight = o.Landscape ? width : height;
            MarginTop = Inches(o.Margin?.Top);
            MarginBottom = Inches(o.Margin?.Bottom);
            MarginLeft = Inches(o.Margin?.Left);
            MarginRight = Inches(o.Margin?.Right);
            PrintBackground = o.PrintBackground;
            DisplayHeaderFooter = o.DisplayHeaderFooter == true;
            PreferCssPageSize = !DisplayHeaderFooter && (o.PreferCssPageSize ?? true);
            Header = string.IsNullOrEmpty(o.HeaderTemplate) ? "<span></span>" : o.HeaderTemplate!;
            Footer = string.IsNullOrEmpty(o.FooterTemplate) ? "<span></span>" : o.FooterTemplate!;
            Scale = o.Scale ?? 1;
            if (!(Scale >= 0.1 && Scale <= 2))
            {
                throw new ArgumentOutOfRangeException("options", "PdfOptions.Scale must be from 0.1 to 2.");
            }

            LoadTimeout = Checked(o.Timeout, "PdfOptions.Timeout");
            PrintTimeout = o.PrintTimeout is TimeSpan print
                ? Checked(print, "PdfOptions.PrintTimeout")
                : Times4(LoadTimeout);
        }

        internal double PaperWidth { get; }

        internal double PaperHeight { get; }

        internal double MarginTop { get; }

        internal double MarginBottom { get; }

        internal double MarginLeft { get; }

        internal double MarginRight { get; }

        internal bool PrintBackground { get; }

        internal bool DisplayHeaderFooter { get; }

        internal bool PreferCssPageSize { get; }

        internal string Header { get; }

        internal string Footer { get; }

        internal double Scale { get; }

        internal TimeSpan LoadTimeout { get; }

        internal TimeSpan PrintTimeout { get; }

        internal static PageSetup From(PdfOptions? options) => new PageSetup(options ?? new PdfOptions());

        internal static TimeSpan Checked(TimeSpan value, string name)
        {
            if (value == System.Threading.Timeout.InfiniteTimeSpan || (value > TimeSpan.Zero && value <= MaxTimeout))
            {
                return value;
            }

            throw new ArgumentOutOfRangeException("options", name + " must be positive, or Timeout.InfiniteTimeSpan.");
        }

        private static TimeSpan Times4(TimeSpan value)
        {
            if (value == System.Threading.Timeout.InfiniteTimeSpan)
            {
                return value;
            }

            return value.Ticks > MaxTimeout.Ticks / 4 ? MaxTimeout : TimeSpan.FromTicks(value.Ticks * 4);
        }

        /// <summary>A CSS length in inches, as Puppeteer reads it (a bare number is pixels).</summary>
        internal static double Inches(string? length)
        {
            if (length is null)
            {
                return 0;
            }

            string text = JsText.Trim(length);
            if (text.Length == 0)
            {
                return 0;
            }

            Match match = Length.Match(text);
            if (!match.Success)
            {
                throw new ArgumentException("Not a CSS length: '" + length + "'. Use px, in, cm or mm.", "options");
            }

            double n = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            switch (match.Groups[2].Value.ToLowerInvariant())
            {
                case "in": return n;
                case "cm": return n / 2.54;
                case "mm": return n / 25.4;
                default: return n / 96;
            }
        }
    }
}
