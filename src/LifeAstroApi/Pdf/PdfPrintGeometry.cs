namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// A page size with margins in PDF points (1/72 inch), and the same in inches: what a web view's print
    /// API needs (WebView2's <c>CoreWebView2PrintSettings</c>, for example, takes inches).
    /// </summary>
    public sealed class PdfPrintGeometry
    {
        private const double PointsPerInch = 72d;

        internal PdfPrintGeometry(int width, int height, int top, int right, int bottom, int left)
        {
            Width = width;
            Height = height;
            MarginTop = top;
            MarginRight = right;
            MarginBottom = bottom;
            MarginLeft = left;
        }

        /// <summary>The page width in points.</summary>
        public int Width { get; }

        /// <summary>The page height in points.</summary>
        public int Height { get; }

        /// <summary>The top margin in points.</summary>
        public int MarginTop { get; }

        /// <summary>The right margin in points.</summary>
        public int MarginRight { get; }

        /// <summary>The bottom margin in points.</summary>
        public int MarginBottom { get; }

        /// <summary>The left margin in points.</summary>
        public int MarginLeft { get; }

        /// <summary>The page width in inches.</summary>
        public double WidthInches => Width / PointsPerInch;

        /// <summary>The page height in inches.</summary>
        public double HeightInches => Height / PointsPerInch;

        /// <summary>The top margin in inches.</summary>
        public double MarginTopInches => MarginTop / PointsPerInch;

        /// <summary>The right margin in inches.</summary>
        public double MarginRightInches => MarginRight / PointsPerInch;

        /// <summary>The bottom margin in inches.</summary>
        public double MarginBottomInches => MarginBottom / PointsPerInch;

        /// <summary>The left margin in inches.</summary>
        public double MarginLeftInches => MarginLeft / PointsPerInch;
    }
}
