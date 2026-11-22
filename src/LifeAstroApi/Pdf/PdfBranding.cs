namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// Your branding for the reports, over the report's own <c>Branding</c>: each property you set wins,
    /// each one left null or blank keeps the report's value or the default.
    /// </summary>
    public sealed class PdfBranding
    {
        /// <summary>The company name on the cover and in the Brihad kundli's running header, printed as you write it.</summary>
        public string? CompanyName { get; set; }

        /// <summary>
        /// The logo at the top of the cover: an http(s) URL, a <c>data:image/...</c> URI or a relative URL
        /// (<c>//host/logo.png</c> becomes https; any other scheme is dropped). The browser that prints the
        /// PDF fetches it.
        /// </summary>
        public string? LogoUrl { get; set; }

        /// <summary>
        /// The accent colour as a CSS colour (hex, name, rgb or hsl), for example <c>#6b21a8</c>: headings and
        /// table headers, and the whole Brihad kundli's banners, charts, contents, header and footer. A colour
        /// too pale to read prints in a darker shade of it.
        /// </summary>
        public string? PrimaryColor { get; set; }

        /// <summary>
        /// The colour of secondary text and of the footer, and of the Brihad kundli's small details. Read as
        /// <see cref="PrimaryColor"/> is.
        /// </summary>
        public string? SecondaryColor { get; set; }

        /// <summary>
        /// The footer line of every page, printed as you write it. Default: <c>Generated using LifeAstroAPI</c>.
        /// </summary>
        public string? FooterText { get; set; }

        /// <summary>
        /// A faint diagonal watermark on every page, sized to fit, for example <c>SAMPLE</c>. In the Brihad
        /// kundli it is behind the content.
        /// </summary>
        public string? WatermarkText { get; set; }
    }
}
