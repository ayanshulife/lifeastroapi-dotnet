using System;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// HTML could not be printed to PDF: no browser, the browser or service failed, a timeout, or the engine
    /// was disposed. A print you cancel throws <see cref="OperationCanceledException"/> instead.
    /// </summary>
    /// <remarks>
    /// <see cref="LifeAstroException.Code"/> is <c>browser_not_found</c>, <c>browser_error</c>,
    /// <c>timeout</c>, <c>connection_error</c>, <c>render_failed</c>, <c>invalid_pdf</c> or <c>engine_closed</c>.
    /// <see cref="LifeAstroException.Status"/> is the HTTP status a Gotenberg service answered, else 0.
    /// </remarks>
    public class LifeAstroPdfException : LifeAstroException
    {
        /// <summary>Creates the exception (also for an <see cref="IPdfEngine"/> of your own).</summary>
        /// <param name="message">What went wrong and what to do.</param>
        /// <param name="code">A machine-readable code.</param>
        /// <param name="innerException">The underlying exception, or null.</param>
        /// <param name="status">The HTTP status of a print service, or 0.</param>
        public LifeAstroPdfException(string message, string code = "render_failed", Exception? innerException = null, int status = 0)
            : base(message, code, status, null, null, innerException)
        {
        }

        internal static LifeAstroPdfException EngineClosed(string engine, Exception? cause = null) =>
            new LifeAstroPdfException(
                "The " + engine + " was disposed before this PDF was done. Dispose an engine when the application stops.",
                "engine_closed",
                cause);
    }
}
