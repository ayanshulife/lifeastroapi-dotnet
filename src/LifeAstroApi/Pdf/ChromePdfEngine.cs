using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf.Engine;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// Prints with a headless Chrome, Chromium or Microsoft Edge on your machine, over the DevTools protocol.
    /// No package and no Node.js are needed, only the browser, and nothing is sent to LifeAstroAPI.
    /// <code>
    /// using (var engine = new ChromePdfEngine())
    /// {
    ///     byte[] lite = await PdfReports.KundliLitePdfAsync(report, engine: engine);
    ///     byte[] match = await PdfReports.MatchMakingPdfAsync(matchReport, engine: engine);
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The browser starts at the first print with a temporary profile of its own, serves concurrent calls
    /// in separate tabs, and is started again if it has gone away. Create one engine for the application
    /// (a singleton) and dispose it at shutdown: that stops the browser and deletes the profile. The
    /// browser also stops when your process ends without disposing it.
    /// </para>
    /// <para>
    /// The reports need fonts for the Indian scripts: Windows and macOS have their own; on Debian or Ubuntu
    /// install <c>fonts-noto-core</c>.
    /// </para>
    /// </remarks>
    public sealed class ChromePdfEngine : IPdfEngine, IDisposable
#if NET8_0_OR_GREATER
        // Not on netstandard2.0: there the interface is in Microsoft.Bcl.AsyncInterfaces, which an application
        // on .NET Core 3.1 to 7 would not get. DisposeAsync() itself is in both builds.
        , IAsyncDisposable
#endif
    {
        private readonly ChromePdfEngineOptions _options;
        private readonly SemaphoreSlim _starting = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _slots;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _disposed =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<Task> _retiring = new List<Task>();

        private BrowserSession? _session;
        private int _disposing;

        /// <summary>An engine that finds the browser itself.</summary>
        public ChromePdfEngine()
            : this(new ChromePdfEngineOptions())
        {
        }

        /// <summary>An engine with these settings. The browser starts at the first print.</summary>
        /// <param name="options">The settings; copied.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timeout or the concurrency is not positive.</exception>
        public ChromePdfEngine(ChromePdfEngineOptions options)
        {
            Wire.NotNull(options, nameof(options));
            if (options.MaxConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrency must be 1 or more.");
            }

            _options = new ChromePdfEngineOptions
            {
                ExecutablePath = options.ExecutablePath,
                WebSocketDebuggerUrl = options.WebSocketDebuggerUrl,
                NoSandbox = options.NoSandbox,
                LaunchTimeout = PageSetup.Checked(options.LaunchTimeout, "LaunchTimeout"),
                MaxConcurrency = options.MaxConcurrency,
            };
            foreach (string argument in options.Arguments)
            {
                _options.Arguments.Add(argument);
            }

            _slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        }

        /// <summary>The browser an engine would start: from the environment variables, then the usual install locations.</summary>
        /// <returns>The path, or null when none is installed.</returns>
        /// <exception cref="LifeAstroPdfException"><c>LIFEASTRO_CHROME</c> is set to a file that does not exist.</exception>
        public static string? FindExecutable() => BrowserLocator.Find();

        /// <summary>The process id of the browser this engine started, for the tests.</summary>
        internal int? ProcessId => Volatile.Read(ref _session)?.ProcessId;

        /// <summary>The profile directory of that browser, for the tests.</summary>
        internal string? ProfileDirectory => Volatile.Read(ref _session)?.ProfileDirectory;

        /// <summary>Starts the browser now, so that a missing browser fails at start-up and not at the first print.</summary>
        /// <param name="cancellationToken">Cancels the start.</param>
        /// <returns>A task that completes when the browser runs.</returns>
        /// <exception cref="LifeAstroPdfException">No browser, it did not start, or the engine is disposed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await SettleAsync(GetSessionAsync, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        /// <exception cref="LifeAstroPdfException">
        /// No browser, the browser failed, a timeout, or the engine was disposed (<c>engine_closed</c>).
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public async Task<byte[]> HtmlToPdfAsync(string html, PdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            Wire.NotNull(html, nameof(html));
            PageSetup setup = PageSetup.From(options);
            html = PdfPrintCss.Apply(html, options);
            return await SettleAsync(token => PrintAsync(html, setup, token), cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]> PrintAsync(string html, PageSetup setup, CancellationToken cancellationToken)
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                BrowserSession session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
                return await session.PrintAsync(html, setup, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _slots.Release();
            }
        }

        /// <summary>
        /// What every call ends as: its result, a <see cref="LifeAstroPdfException"/>, or the caller's own
        /// cancellation. Nothing else gets out, also when the engine is disposed or the browser dies midway.
        /// </summary>
        private async Task<T> SettleAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken callers)
        {
            callers.ThrowIfCancellationRequested();
            if (IsDisposing)
            {
                throw LifeAstroPdfException.EngineClosed(nameof(ChromePdfEngine));
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(callers, _shutdown.Token))
            {
                try
                {
                    return await body(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException e) when (callers.IsCancellationRequested)
                {
                    throw new OperationCanceledException(e.Message, e, callers);
                }
                catch (Exception e) when (IsDisposing)
                {
                    throw LifeAstroPdfException.EngineClosed(nameof(ChromePdfEngine), e);
                }
                catch (Exception e) when (!(e is LifeAstroPdfException))
                {
                    throw new LifeAstroPdfException("The browser failed: " + e.Message, "browser_error", e);
                }
            }
        }

        private async Task<BrowserSession> GetSessionAsync(CancellationToken cancellationToken)
        {
            BrowserSession? session = Volatile.Read(ref _session);
            if (session != null && session.IsUsable)
            {
                return session;
            }

            await _starting.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                session = _session;
                if (session != null)
                {
                    if (session.IsUsable)
                    {
                        return session;
                    }

                    Volatile.Write(ref _session, null);
                    if (session.IsBusy)
                    {
                        // It stopped answering for new tabs, but prints still run in it: let them end first.
                        _retiring.RemoveAll(task => task.IsCompleted);
                        _retiring.Add(session.CloseWhenIdleAsync(_shutdown.Token));
                    }
                    else
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }
                }

                session = await BrowserSession.StartAsync(_options, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _session, session);
                return session;
            }
            finally
            {
                _starting.Release();
            }
        }

        /// <summary>
        /// Stops the browser and deletes its profile. Prints in progress, waiting or started later fail with a
        /// <see cref="LifeAstroPdfException"/> (<c>engine_closed</c>). Safe to call more than once, and never throws.
        /// </summary>
        /// <returns>A task that completes when the browser is gone.</returns>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposing, 1) != 0)
            {
                await _disposed.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                EngineTasks.CancelQuietly(_shutdown);
                await _starting.WaitAsync().ConfigureAwait(false);
                try
                {
                    BrowserSession? session = _session;
                    Volatile.Write(ref _session, null);
                    if (session != null)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    await Task.WhenAll(_retiring).ConfigureAwait(false);
                }
                finally
                {
                    _starting.Release();
                }
            }
            finally
            {
                // Calls still unwinding use the semaphores and the token, so those are left to the GC.
                _disposed.TrySetResult(true);
            }
        }

        /// <summary>As <see cref="DisposeAsync"/>, waiting for it.</summary>
        public void Dispose()
        {
            // On the thread pool, so that a UI thread or a single-threaded test context cannot deadlock.
            Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }

        private bool IsDisposing => Volatile.Read(ref _disposing) != 0;
    }
}
