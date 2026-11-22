using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf.Engine;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// Prints through a <a href="https://gotenberg.dev">Gotenberg</a> service that you run, which brings its
    /// own Chromium: for servers that cannot run a browser.
    /// <code>
    /// // docker run --rm -p 3000:3000 gotenberg/gotenberg:8
    /// var engine = new GotenbergPdfEngine("http://localhost:3000");
    /// byte[] pdf = await PdfReports.KundliLitePdfAsync(report, engine: engine);
    /// </code>
    /// </summary>
    /// <remarks>
    /// Thread-safe. The report's HTML, with the personal data in it, goes to that service: use one you control.
    /// </remarks>
    public sealed class GotenbergPdfEngine : IPdfEngine, IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly Uri _endpoint;
        private readonly KeyValuePair<string, string>[] _headers;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private int _disposed;

        /// <summary>An engine for the service at this URL.</summary>
        /// <param name="baseUrl">The service's base URL, for example <c>http://localhost:3000</c>.</param>
        /// <param name="headers">Extra request headers, for example <c>Authorization</c>.</param>
        /// <param name="httpClient">Your HTTP client, which stays yours. Null: the engine makes and disposes its own.</param>
        /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is not an http(s) URL.</exception>
        public GotenbergPdfEngine(string baseUrl, IEnumerable<KeyValuePair<string, string>>? headers = null, HttpClient? httpClient = null)
        {
            Wire.NotNull(baseUrl, nameof(baseUrl));
            if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/forms/chromium/convert/html", UriKind.Absolute, out Uri? endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("The Gotenberg base URL must be an http(s) URL.", nameof(baseUrl));
            }

            _endpoint = endpoint;
            _headers = headers is null ? new KeyValuePair<string, string>[0] : new List<KeyValuePair<string, string>>(headers).ToArray();
            _ownsHttp = httpClient is null;
            _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">
        /// The service failed, could not be reached or timed out, or the engine was disposed (<c>engine_closed</c>).
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public async Task<byte[]> HtmlToPdfAsync(string html, PdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            Wire.NotNull(html, nameof(html));
            PageSetup setup = PageSetup.From(options);
            html = PdfPrintCss.Apply(html, options);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed)
            {
                throw LifeAstroPdfException.EngineClosed(nameof(GotenbergPdfEngine));
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token))
            {
                try
                {
                    return await EngineTasks.WithTimeoutAsync(
                        Total(setup),
                        linked.Token,
                        "Gotenberg did not answer in time (PdfOptions.Timeout and PrintTimeout).",
                        token => PostAsync(html, setup, token)).ConfigureAwait(false);
                }
                catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(e.Message, e, cancellationToken);
                }
                catch (Exception e) when (IsDisposed)
                {
                    throw LifeAstroPdfException.EngineClosed(nameof(GotenbergPdfEngine), e);
                }
                catch (Exception e) when (!(e is LifeAstroPdfException))
                {
                    throw new LifeAstroPdfException("The request to Gotenberg failed: " + e.Message, "connection_error", e);
                }
            }
        }

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        private async Task<byte[]> PostAsync(string html, PageSetup setup, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            using (var form = new MultipartFormDataContent())
            {
                AddField(form, "paperWidth", Number(setup.PaperWidth));
                AddField(form, "paperHeight", Number(setup.PaperHeight));
                AddField(form, "marginTop", Number(setup.MarginTop));
                AddField(form, "marginBottom", Number(setup.MarginBottom));
                AddField(form, "marginLeft", Number(setup.MarginLeft));
                AddField(form, "marginRight", Number(setup.MarginRight));
                AddField(form, "preferCssPageSize", setup.PreferCssPageSize ? "true" : "false");
                AddField(form, "printBackground", setup.PrintBackground ? "true" : "false");
                AddField(form, "landscape", "false"); // the orientation is in the width and height
                AddField(form, "scale", Number(setup.Scale));
                AddFile(form, "index.html", html);
                if (setup.DisplayHeaderFooter)
                {
                    AddFile(form, "header.html", Document(setup.Header));
                    AddFile(form, "footer.html", Document(setup.Footer));
                }

                request.Content = form;
                foreach (KeyValuePair<string, string> header in _headers)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        form.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                try
                {
                    using (HttpResponseMessage response = await _http
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (status != 200)
                        {
                            string detail = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 500)).Trim();
                            throw new LifeAstroPdfException(
                                "Gotenberg returned " + status.ToString(CultureInfo.InvariantCulture) + ": " + detail,
                                "render_failed",
                                null,
                                status);
                        }

                        if (body.Length < 5 || body[0] != '%' || body[1] != 'P' || body[2] != 'D' || body[3] != 'F')
                        {
                            throw new LifeAstroPdfException("Gotenberg answered with something that is not a PDF.", "render_failed", null, status);
                        }

                        return body;
                    }
                }
                catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    // The timeout of an HttpClient that was passed in.
                    throw new LifeAstroPdfException("The request to Gotenberg hit the HttpClient's timeout.", "timeout", e);
                }
                catch (HttpRequestException e)
                {
                    Exception cause = e.GetBaseException();
                    throw new LifeAstroPdfException(
                        "Cannot reach Gotenberg at " + _endpoint.GetLeftPart(UriPartial.Authority) + ": " + cause.Message,
                        "connection_error",
                        e);
                }
            }
        }

        private static TimeSpan Total(PageSetup setup)
        {
            if (setup.LoadTimeout == Timeout.InfiniteTimeSpan || setup.PrintTimeout == Timeout.InfiniteTimeSpan)
            {
                return Timeout.InfiniteTimeSpan;
            }

            return TimeSpan.FromMilliseconds(Math.Min(int.MaxValue, setup.LoadTimeout.TotalMilliseconds + setup.PrintTimeout.TotalMilliseconds));
        }

        private static string Number(double value) => QueryString.FormatNumber(value);

        private static string Document(string fragment) =>
            "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0\">" + fragment + "</body></html>";

        // .NET writes name=paperWidth; quoted names are what every multipart reader accepts.
        private static void AddField(MultipartFormDataContent form, string name, string value)
        {
            var part = new StringContent(value, Encoding.UTF8);
            part.Headers.ContentType = null;
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + name + "\"" };
            form.Add(part);
        }

        private static void AddFile(MultipartFormDataContent form, string fileName, string html)
        {
            var part = new StringContent(html, Encoding.UTF8, "text/html");
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"files\"",
                FileName = "\"" + fileName + "\"",
            };
            form.Add(part);
        }

        /// <summary>
        /// Disposes the HTTP client the engine made; one you passed in is left open. Prints in progress or started
        /// later fail with a <see cref="LifeAstroPdfException"/> (<c>engine_closed</c>).
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            EngineTasks.CancelQuietly(_shutdown);
            if (_ownsHttp)
            {
                _http.Dispose();
            }
        }
    }
}
