using System;
using System.Collections.Generic;

namespace LifeAstroApi.Pdf
{
    /// <summary>How a <see cref="ChromePdfEngine"/> finds and starts its browser.</summary>
    public sealed class ChromePdfEngineOptions
    {
        /// <summary>The time allowed for a browser to start, unless set otherwise: 30 seconds.</summary>
        public static readonly TimeSpan DefaultLaunchTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The browser's executable. Default: the <c>LIFEASTRO_CHROME</c>, <c>CHROME_PATH</c> or
        /// <c>PUPPETEER_EXECUTABLE_PATH</c> environment variable, then the usual install locations
        /// (<see cref="ChromePdfEngine.FindExecutable()"/>).
        /// </summary>
        public string? ExecutablePath { get; set; }

        /// <summary>
        /// A browser that already runs with remote debugging, for example in a container: the
        /// <c>webSocketDebuggerUrl</c> of its <c>/json/version</c>. The engine then starts no browser and
        /// disposing it leaves that browser running.
        /// </summary>
        public string? WebSocketDebuggerUrl { get; set; }

        /// <summary>
        /// Start the browser with <c>--no-sandbox</c>. Default (null): only when the process runs as root,
        /// where Chrome refuses to start otherwise. Set true only where you trust the HTML you print.
        /// </summary>
        public bool? NoSandbox { get; set; }

        /// <summary>
        /// Extra command-line switches. Give paths in full: the browser runs in its own directory, not in
        /// the application's.
        /// </summary>
        public IList<string> Arguments { get; } = new List<string>();

        /// <summary>The time allowed for the browser to start. Default 30 seconds.</summary>
        public TimeSpan LaunchTimeout { get; set; } = DefaultLaunchTimeout;

        /// <summary>How many documents one engine prints at the same time; more calls wait. Default 4.</summary>
        public int MaxConcurrency { get; set; } = 4;
    }
}
