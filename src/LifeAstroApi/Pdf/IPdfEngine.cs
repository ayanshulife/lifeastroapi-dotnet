using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// Prints a complete HTML document to PDF. The SDK has <see cref="ChromePdfEngine"/> and
    /// <see cref="GotenbergPdfEngine"/>; implement this interface to use anything else.
    /// </summary>
    /// <remarks>
    /// An implementation should be safe to call from several threads at once. The HTML that
    /// <see cref="PdfReports"/> gives an engine already carries, as print CSS, those options that the
    /// document's own CSS would win over (margins, paper, no backgrounds, your footer); an engine passes the
    /// options to its printer as well.
    /// </remarks>
    public interface IPdfEngine
    {
        /// <summary>Prints one document.</summary>
        /// <param name="html">A complete HTML document.</param>
        /// <param name="options">Page and timeout options; null for the defaults.</param>
        /// <param name="cancellationToken">Cancels the print.</param>
        /// <returns>The PDF bytes.</returns>
        Task<byte[]> HtmlToPdfAsync(string html, PdfOptions? options = null, CancellationToken cancellationToken = default);
    }
}
