using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Pdf.Engine
{
    /// <summary>A running browser and what is done in it: open a tab, load the HTML, print, close the tab.</summary>
    internal sealed class BrowserSession
    {
        // Waits for the document, its images (lazy ones too) and its fonts. Reading offsetHeight lays the
        // page out, which is what starts the font downloads that document.fonts.ready waits for.
        private const string WaitForLoad =
            "(async () => {"
            + "if (document.readyState !== 'complete') await new Promise((r) => window.addEventListener('load', r, { once: true }));"
            + "await Promise.all(Array.from(document.images).map((img) => { img.loading = 'eager'; return img.complete ? null :"
            + " new Promise((r) => { img.addEventListener('load', r, { once: true }); img.addEventListener('error', r, { once: true }); }); }));"
            + "if (document.body) void document.body.offsetHeight;"
            + "if (document.fonts && document.fonts.ready) await document.fonts.ready;"
            + "return true;"
            + "})()";

        private const int StreamChunk = 1 << 20;

        private static readonly TimeSpan OpenTabTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan CloseTabTimeout = TimeSpan.FromSeconds(15);

        private readonly DevToolsConnection _connection;
        private readonly BrowserProcess? _process;
        private readonly string? _context;
        private volatile bool _unresponsive;
        private int _printing;

        private BrowserSession(DevToolsConnection connection, BrowserProcess? process, string? context = null)
        {
            _connection = connection;
            _process = process;
            _context = context;
        }

        internal bool IsUsable => !_unresponsive && _connection.IsOpen;

        internal bool IsBusy => _connection.IsOpen && Volatile.Read(ref _printing) > 0;

        internal int? ProcessId => _process?.Id;

        internal string? ProfileDirectory => _process?.ProfileDirectory;

        internal static Task<BrowserSession> StartAsync(ChromePdfEngineOptions options, CancellationToken cancellationToken)
        {
            string waited = options.LaunchTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            return EngineTasks.WithTimeoutAsync(
                options.LaunchTimeout,
                cancellationToken,
                "The browser did not start within " + waited + " seconds (ChromePdfEngineOptions.LaunchTimeout).",
                token => StartCoreAsync(options, token));
        }

        private static async Task<BrowserSession> StartCoreAsync(ChromePdfEngineOptions options, CancellationToken cancellationToken)
        {
            string? external = options.WebSocketDebuggerUrl;
            if (external != null && external.Trim().Length > 0)
            {
                if (!Uri.TryCreate(external.Trim(), UriKind.Absolute, out Uri? url) || (url.Scheme != "ws" && url.Scheme != "wss"))
                {
                    throw new LifeAstroPdfException("WebSocketDebuggerUrl must be a ws:// or wss:// URL.", "connection_error");
                }

                DevToolsConnection remote = await DevToolsConnection.ConnectAsync(url, false, cancellationToken).ConfigureAwait(false);
                try
                {
                    // That browser outlives the connection, so the tabs go into a context that lives only as
                    // long as it: disposing, a lost connection or this process dying leaves none open there.
                    JsonElement created = await remote.SendAsync(
                        "Target.createBrowserContext",
                        json => json.WriteBoolean("disposeOnDetach", true),
                        null,
                        cancellationToken).ConfigureAwait(false);
                    return new BrowserSession(remote, null, Text(created, "browserContextId"));
                }
                catch
                {
                    remote.Close();
                    throw;
                }
            }

            BrowserProcess process = BrowserProcess.Launch(options);
            try
            {
                Uri url;
                try
                {
                    url = await process.WaitForDevToolsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (LifeAstroPdfException) when (process.LifelineRefused)
                {
                    // A wrapper around the browser (some packages have one) that does not pass the pipe on.
                    await process.StopAsync(TimeSpan.Zero).ConfigureAwait(false);
                    process = BrowserProcess.Launch(options, lifeline: false);
                    url = await process.WaitForDevToolsAsync(cancellationToken).ConfigureAwait(false);
                }

                DevToolsConnection connection = await DevToolsConnection.ConnectAsync(url, true, cancellationToken).ConfigureAwait(false);
                return new BrowserSession(connection, process);
            }
            catch
            {
                await process.StopAsync(TimeSpan.Zero).ConfigureAwait(false);
                throw;
            }
        }

        internal async Task<byte[]> PrintAsync(string html, PageSetup setup, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _printing);
            try
            {
                return await PrintInTabAsync(html, setup, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _printing);
            }
        }

        private async Task<byte[]> PrintInTabAsync(string html, PageSetup setup, CancellationToken cancellationToken)
        {
            Task<string> opening = OpenTabAsync();
            string target;
            try
            {
                target = await EngineTasks.UnlessCancelledAsync(opening, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Nobody waits for the tab any more, but it must not stay open.
                _ = CloseWhenOpenedAsync(opening);
                throw;
            }

            try
            {
                string session = await EngineTasks.WithTimeoutAsync(
                    setup.LoadTimeout,
                    cancellationToken,
                    "The document did not load in time (PdfOptions.Timeout). An image or font it links to may be unreachable.",
                    token => LoadAsync(target, html, token)).ConfigureAwait(false);

                return await EngineTasks.WithTimeoutAsync(
                    setup.PrintTimeout,
                    cancellationToken,
                    "The browser did not finish printing in time (PdfOptions.PrintTimeout).",
                    token => PrintPageAsync(session, setup, token)).ConfigureAwait(false);
            }
            finally
            {
                await CloseTabAsync(target).ConfigureAwait(false);
            }
        }

        private async Task<string> OpenTabAsync()
        {
            try
            {
                JsonElement created = await EngineTasks.WithTimeoutAsync(
                    OpenTabTimeout,
                    CancellationToken.None,
                    "The browser did not open a tab: it does not answer.",
                    token => _connection.SendAsync(
                        "Target.createTarget",
                        json =>
                        {
                            json.WriteString("url", "about:blank");
                            if (_context != null)
                            {
                                json.WriteString("browserContextId", _context);
                            }
                        },
                        null,
                        token))
                    .ConfigureAwait(false);
                return Text(created, "targetId");
            }
            catch (LifeAstroPdfException)
            {
                _unresponsive = true; // the next call starts a new browser
                throw;
            }
        }

        private async Task CloseWhenOpenedAsync(Task<string> opening)
        {
            try
            {
                await CloseTabAsync(await opening.ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Nobody awaits this: a tab that never opened, or a browser that is gone, needs nothing more.
            }
        }

        private async Task<string> LoadAsync(string target, string html, CancellationToken cancellationToken)
        {
            JsonElement attached = await _connection.SendAsync(
                "Target.attachToTarget",
                json =>
                {
                    json.WriteString("targetId", target);
                    json.WriteBoolean("flatten", true);
                },
                null,
                cancellationToken).ConfigureAwait(false);
            string session = Text(attached, "sessionId");

            await _connection.SendAsync("Inspector.enable", null, session, cancellationToken).ConfigureAwait(false); // reports a crash
            await _connection.SendAsync("Page.enable", null, session, cancellationToken).ConfigureAwait(false);
            JsonElement tree = await _connection.SendAsync("Page.getFrameTree", null, session, cancellationToken).ConfigureAwait(false);
            string frame = Text(tree.GetProperty("frameTree").GetProperty("frame"), "id");
            await _connection.SendAsync(
                "Page.setDocumentContent",
                json =>
                {
                    json.WriteString("frameId", frame);
                    json.WriteString("html", html);
                },
                session,
                cancellationToken).ConfigureAwait(false);
            JsonElement waited = await _connection.SendAsync(
                "Runtime.evaluate",
                json =>
                {
                    json.WriteString("expression", WaitForLoad);
                    json.WriteBoolean("awaitPromise", true);
                    json.WriteBoolean("returnByValue", true);
                },
                session,
                cancellationToken).ConfigureAwait(false);
            if (waited.TryGetProperty("exceptionDetails", out _))
            {
                throw new LifeAstroPdfException("The document could not be loaded in the browser.", "render_failed");
            }

            return session;
        }

        private async Task<byte[]> PrintPageAsync(string session, PageSetup setup, CancellationToken cancellationToken)
        {
            JsonElement printed = await _connection.SendAsync(
                "Page.printToPDF",
                json =>
                {
                    json.WriteBoolean("landscape", false); // the orientation is in the width and height
                    json.WriteBoolean("printBackground", setup.PrintBackground);
                    json.WriteBoolean("preferCSSPageSize", setup.PreferCssPageSize);
                    json.WriteNumber("paperWidth", setup.PaperWidth);
                    json.WriteNumber("paperHeight", setup.PaperHeight);
                    json.WriteNumber("marginTop", setup.MarginTop);
                    json.WriteNumber("marginBottom", setup.MarginBottom);
                    json.WriteNumber("marginLeft", setup.MarginLeft);
                    json.WriteNumber("marginRight", setup.MarginRight);
                    json.WriteNumber("scale", setup.Scale);
                    json.WriteBoolean("displayHeaderFooter", setup.DisplayHeaderFooter);
                    if (setup.DisplayHeaderFooter)
                    {
                        json.WriteString("headerTemplate", setup.Header);
                        json.WriteString("footerTemplate", setup.Footer);
                    }

                    // A long report's PDF is many MB: read it in pieces rather than as one message.
                    json.WriteString("transferMode", "ReturnAsStream");
                },
                session,
                cancellationToken).ConfigureAwait(false);

            byte[] pdf;
            if (printed.TryGetProperty("stream", out JsonElement stream) && stream.ValueKind == JsonValueKind.String)
            {
                pdf = await ReadStreamAsync(stream.GetString()!, session, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                pdf = Convert.FromBase64String(Text(printed, "data"));
            }

            return pdf.Length > 0 ? pdf : throw new LifeAstroPdfException("The browser returned an empty PDF.", "render_failed");
        }

        private async Task<byte[]> ReadStreamAsync(string handle, string session, CancellationToken cancellationToken)
        {
            using (var pdf = new MemoryStream())
            {
                while (true)
                {
                    JsonElement part = await _connection.SendAsync(
                        "IO.read",
                        json =>
                        {
                            json.WriteString("handle", handle);
                            json.WriteNumber("size", StreamChunk);
                        },
                        session,
                        cancellationToken).ConfigureAwait(false);
                    string data = Text(part, "data");
                    bool base64 = part.TryGetProperty("base64Encoded", out JsonElement encoded) && encoded.ValueKind == JsonValueKind.True;
                    byte[] bytes = base64 ? Convert.FromBase64String(data) : System.Text.Encoding.UTF8.GetBytes(data);
                    pdf.Write(bytes, 0, bytes.Length);
                    if (part.TryGetProperty("eof", out JsonElement eof) && eof.ValueKind == JsonValueKind.True)
                    {
                        break;
                    }
                }

                await _connection.SendAsync("IO.close", json => json.WriteString("handle", handle), session, cancellationToken)
                    .ConfigureAwait(false);
                return pdf.ToArray();
            }
        }

        private async Task CloseTabAsync(string target)
        {
            if (!_connection.IsOpen)
            {
                return;
            }

            try
            {
                await EngineTasks.WithTimeoutAsync(
                    CloseTabTimeout,
                    CancellationToken.None,
                    "The browser did not close the tab.",
                    token => _connection.SendAsync("Target.closeTarget", json => json.WriteString("targetId", target), null, token))
                    .ConfigureAwait(false);
            }
            catch (LifeAstroPdfException e) when (e.Code == "timeout")
            {
                _unresponsive = true;
            }
            catch (LifeAstroPdfException)
            {
                // The tab or the browser is gone already.
            }
        }

        private static string Text(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new LifeAstroPdfException("An unexpected answer from the browser (no " + name + ").", "browser_error");
        }

        internal async Task CloseWhenIdleAsync(CancellationToken shutdown)
        {
            try
            {
                while (IsBusy)
                {
                    await Task.Delay(250, shutdown).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The engine is being disposed, which ends those prints.
            }

            await CloseAsync().ConfigureAwait(false);
        }

        /// <summary>Never throws: closing runs in Dispose, and in tasks that nobody awaits.</summary>
        internal async Task CloseAsync()
        {
            try
            {
                await CloseCoreAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Closing goes as far as it can; the process handler and the pipe stop what is left.
            }
            finally
            {
                _connection.Close();
            }
        }

        private async Task CloseCoreAsync()
        {
            TimeSpan grace = TimeSpan.Zero;
            if (_process != null && _connection.IsOpen)
            {
                try
                {
                    await EngineTasks.WithTimeoutAsync(
                        TimeSpan.FromSeconds(5),
                        CancellationToken.None,
                        "The browser did not close.",
                        token => _connection.SendAsync("Browser.close", null, null, token)).ConfigureAwait(false);
                    grace = TimeSpan.FromSeconds(5);
                }
                catch (LifeAstroPdfException e)
                {
                    // It may have quit before it answered; give it the time to finish unless it hangs.
                    grace = e.Code == "timeout" ? TimeSpan.Zero : TimeSpan.FromSeconds(2);
                }
            }

            if (_process != null)
            {
                await _process.StopAsync(grace).ConfigureAwait(false);
            }
        }
    }
}
