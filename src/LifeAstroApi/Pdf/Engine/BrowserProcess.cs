using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Pdf.Engine
{
    /// <summary>
    /// A browser this SDK started, with its own temporary profile. Only this process tree is ever stopped,
    /// and only this directory is ever deleted. The browser does not outlive the application either:
    /// <see cref="UnixLauncher"/> on Linux and macOS, <see cref="WindowsJob"/> on Windows.
    /// </summary>
    internal sealed class BrowserProcess
    {
        internal const string ProfilePrefix = "lifeastro-dotnet-chrome-";

        private const string Announcement = "DevTools listening on ";
        private const string PipeRefused = "Remote debugging pipe file descriptors are not open";
        private const int DeleteAttempts = 40;

        private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(10);

        private readonly Process _process;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly EventHandler _onExit;
        private readonly bool _hasLifeline;
        private WindowsJob? _job;
        private volatile string? _url;
        private volatile BrowserIdentity? _browser;
        private int _stopped;

        private BrowserProcess(Process process, string profileDirectory, bool hasLifeline)
        {
            _process = process;
            _hasLifeline = hasLifeline;
            ProfileDirectory = profileDirectory;
            _onExit = (sender, args) => StopNow();
        }

        internal string ProfileDirectory { get; }

        internal int Id { get; private set; }

        /// <summary>The browser quit because the lifeline pipe did not reach it.</summary>
        internal bool LifelineRefused
        {
            get
            {
                lock (_log)
                {
                    return _hasLifeline && _log.ToString().IndexOf(PipeRefused, StringComparison.Ordinal) >= 0;
                }
            }
        }

        internal static BrowserProcess Launch(ChromePdfEngineOptions options, bool lifeline = true)
        {
            BrowserPlatform platform = BrowserLocator.Current;
            string? executable = options.ExecutablePath;
            if (executable != null && executable.Trim().Length > 0)
            {
                if (!File.Exists(executable))
                {
                    throw new LifeAstroPdfException(
                        "No browser at '" + executable + "' (ChromePdfEngineOptions.ExecutablePath). It must be the executable of "
                            + "Chrome, Chromium or Microsoft Edge; on macOS the file inside the .app (Contents/MacOS/...).",
                        "browser_not_found");
                }
            }
            else
            {
                executable = BrowserLocator.Find() ?? throw BrowserLocator.NotFound(platform);
            }

            // File.Exists reads a bare name in the working directory, /bin/sh would search the PATH for it.
            executable = Path.GetFullPath(executable);

            // Chrome creates the directory, readable by this user only.
            string profile = Path.Combine(Path.GetFullPath(Path.GetTempPath()), ProfilePrefix + Guid.NewGuid().ToString("N"));
            List<string> arguments = Arguments(options, profile, options.NoSandbox ?? IsRoot(platform));
            if (platform == BrowserPlatform.Windows)
            {
                // Started as administrator (as on GitHub's runners), Chrome and Edge otherwise relaunch
                // themselves without elevation, away from this process and its job.
                arguments.Insert(0, "--do-not-de-elevate");
            }

            lifeline = lifeline && UnixLauncher.IsAvailable(platform);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = lifeline ? UnixLauncher.Shell : executable,
                    Arguments = CommandLine(lifeline ? UnixLauncher.Arguments(executable, profile, arguments) : arguments),
                    UseShellExecute = false,
                    CreateNoWindow = true,

                    // The browser's detached helpers (crash handler, updater) outlive it and keep their working
                    // directory busy: never the application's, which could then not be redeployed or deleted.
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    RedirectStandardInput = lifeline,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            var browser = new BrowserProcess(process, profile, lifeline);

            // Both pipes are read for as long as the browser runs: a full pipe would stall it.
            process.ErrorDataReceived += browser.OnOutput;
            process.OutputDataReceived += browser.OnOutput;
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("the process did not start");
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                process.Dispose();
                throw new LifeAstroPdfException("Cannot start the browser '" + executable + "': " + e.Message, "browser_error", e);
            }

            browser.Id = process.Id;
            if (platform == BrowserPlatform.Windows)
            {
                browser._job = WindowsJob.TryAssign(process);
            }

            AppDomain.CurrentDomain.ProcessExit += browser._onExit;
            AppDomain.CurrentDomain.DomainUnload += browser._onExit;
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            return browser;
        }

        internal static List<string> Arguments(ChromePdfEngineOptions options, string profile, bool noSandbox)
        {
            var arguments = new List<string>
            {
                "--headless=new",
                "--remote-debugging-port=0",
                "--user-data-dir=" + profile,
                "--no-first-run",
                "--no-default-browser-check",
                "--no-service-autorun",
                "--disable-background-networking",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-extensions",
                "--disable-sync",
                "--disable-dev-shm-usage",
                "--disable-background-timer-throttling",
                "--disable-features=Translate,MediaRouter",
                "--password-store=basic",
                "--use-mock-keychain",
                "--hide-scrollbars",
                "--mute-audio",
                "--edge-skip-compat-layer-relaunch",
            };
            if (noSandbox)
            {
                arguments.Add("--no-sandbox");
            }

            arguments.AddRange(options.Arguments);
            arguments.Add("about:blank");
            return arguments;
        }

        /// <summary>Arguments as one command line (netstandard2.0 has no ArgumentList), quoted as .NET and Windows read it.</summary>
        internal static string CommandLine(IReadOnlyList<string> arguments)
        {
            var line = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                {
                    line.Append(argument);
                    continue;
                }

                line.Append('"');
                int backslashes = 0;
                foreach (char c in argument)
                {
                    if (c == '\\')
                    {
                        backslashes++;
                        continue;
                    }

                    line.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
                    backslashes = 0;
                }

                line.Append('\\', backslashes * 2).Append('"');
            }

            return line.ToString();
        }

        /// <summary>Root on Linux or macOS. An elevated Windows process is not root: its sandbox works.</summary>
        private static bool IsRoot(BrowserPlatform platform)
        {
            if (platform == BrowserPlatform.Windows)
            {
                return false;
            }

#if NET8_0_OR_GREATER
            return Environment.IsPrivilegedProcess;
#else
            try
            {
                // The effective user id is the second value; a container's root may have no user name.
                if (platform == BrowserPlatform.Linux && File.Exists("/proc/self/status"))
                {
                    foreach (string line in File.ReadAllLines("/proc/self/status"))
                    {
                        if (line.StartsWith("Uid:", StringComparison.Ordinal))
                        {
                            string[] ids = line.Substring(4).Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            return ids.Length > 1 && ids[1] == "0";
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Falls back to the user name.
            }

            return Environment.UserName == "root";
#endif
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            string? line = e.Data;
            if (line is null)
            {
                return;
            }

            if (_hasLifeline && _browser is null && line.StartsWith(UnixLauncher.PidAnnouncement, StringComparison.Ordinal))
            {
                _browser = BrowserIdentity.Read(line.Substring(UnixLauncher.PidAnnouncement.Length));
                return;
            }

            int at = line.IndexOf(Announcement, StringComparison.Ordinal);
            if (at >= 0 && _url is null)
            {
                _url = line.Substring(at + Announcement.Length).Trim();
            }

            lock (_log)
            {
                if (_log.Length < 4000)
                {
                    _log.Append(line.Length > 500 ? line.Substring(0, 500) : line).Append('\n');
                }
            }
        }

        internal async Task<Uri> WaitForDevToolsAsync(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            TimeSpan? exitedAt = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? address = _url ?? ReadPortFile();
                if (address != null && Uri.TryCreate(address, UriKind.Absolute, out Uri? url))
                {
                    return url;
                }

                if (HasExited())
                {
                    // On Windows a launcher may hand over to a second process, which then writes the port file.
                    exitedAt ??= watch.Elapsed;
                    if (watch.Elapsed - exitedAt.Value > (MayHaveHandedOver() ? HandOverWait : ExitWait))
                    {
                        throw ExitedEarly();
                    }
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The second way to learn the address: the file Chrome writes into its profile.</summary>
        private string? ReadPortFile()
        {
            try
            {
                string path = Path.Combine(ProfileDirectory, "DevToolsActivePort");
                if (!File.Exists(path))
                {
                    return null;
                }

                string[] lines;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(file))
                {
                    lines = reader.ReadToEnd().Split('\n');
                }

                if (lines.Length < 2
                    || !int.TryParse(lines[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                    || !lines[1].Trim().StartsWith("/devtools/browser/", StringComparison.Ordinal))
                {
                    return null;
                }

                return "ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + lines[1].Trim();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null; // still being written
            }
        }

        private LifeAstroPdfException ExitedEarly()
        {
            string log;
            lock (_log)
            {
                log = _log.ToString().Trim();
            }

            string code;
            try
            {
                code = " (exit code " + _process.ExitCode.ToString(CultureInfo.InvariantCulture) + ")";
            }
            catch (InvalidOperationException)
            {
                code = string.Empty;
            }

            string hint = string.Empty;
            if (log.IndexOf("sandbox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hint = " The sandbox could not start (usual in containers and on Ubuntu 24.04 CI machines): allow it, or, when you "
                    + "print only HTML you trust, set ChromePdfEngineOptions.NoSandbox = true.";
            }

            return new LifeAstroPdfException(
                "The browser exited before it was ready" + code + "." + hint + (log.Length > 0 ? "\n" + log : string.Empty),
                "browser_error");
        }

        private bool MayHaveHandedOver()
        {
            try
            {
                return BrowserLocator.Current == BrowserPlatform.Windows && _process.ExitCode == 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool HasExited()
        {
            try
            {
                return _process.HasExited;
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception)
            {
                return true;
            }
        }

        /// <summary>Lets go of the pipe that keeps the browser alive: it quits and its profile is deleted.</summary>
        internal void CloseLifeline()
        {
            if (!_hasLifeline)
            {
                return;
            }

            try
            {
                _process.StandardInput.Dispose();
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException)
            {
                // It is closed already.
            }
        }

        /// <summary>Waits <paramref name="grace"/> for the browser to quit, then kills it, then deletes the profile.</summary>
        internal async Task StopAsync(TimeSpan grace)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            try
            {
                CloseLifeline();
                if (_hasLifeline && grace < TimeSpan.FromSeconds(2))
                {
                    grace = TimeSpan.FromSeconds(2); // closing the pipe is itself a request to quit
                }

                await WaitForExitAsync(grace).ConfigureAwait(false);
                if (!HasStopped())
                {
                    Kill();
                    await WaitForExitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }

                _job?.Close();

                // Helper processes can hold files for a moment after the browser is gone (always on Windows).
                for (int attempt = 0; attempt < DeleteAttempts && !TryDeleteProfile(); attempt++)
                {
                    await Task.Delay(150).ConfigureAwait(false);
                }
            }
            finally
            {
                Release();
            }
        }

        internal async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (!HasStopped())
            {
                if (watch.Elapsed >= timeout)
                {
                    return false;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>The application ends without having disposed the engine.</summary>
        private void StopNow()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            try
            {
                CloseLifeline();
                var watch = Stopwatch.StartNew();
                while (_hasLifeline && !HasStopped() && watch.Elapsed < TimeSpan.FromSeconds(1.5))
                {
                    Thread.Sleep(25);
                }

                if (!HasStopped())
                {
                    Kill();
                }

                _job?.Close();
                for (int attempt = 0; attempt < 10 && !TryDeleteProfile(); attempt++)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception)
            {
                // An exception in a ProcessExit handler would be an unhandled one in the application.
            }
            finally
            {
                Release();
            }
        }

        /// <summary>
        /// The shell and the browser under it are both gone. The shell alone is not enough: if it was killed,
        /// the browser still shuts down, and would write the profile again after it was deleted.
        /// </summary>
        private bool HasStopped() => HasExited() && !(_browser?.IsRunning() ?? false);

        private void Kill()
        {
            KillTree();
            _browser?.Kill(); // in case the shell was gone, and the browser no longer in its tree
        }

        private void KillTree()
        {
            try
            {
#if NET8_0_OR_GREATER
                _process.Kill(entireProcessTree: true);
#else
                // netstandard2.0 cannot name Kill(entireProcessTree), which .NET Core 3.0 and later have. On
                // Windows (.NET Framework) closing the job ends the helpers.
                System.Reflection.MethodInfo? killTree = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
                if (killTree != null)
                {
                    killTree.Invoke(_process, new object[] { true });
                }
                else
                {
                    _process.Kill();
                }
#endif
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException
                || e is AggregateException || e is System.Reflection.TargetInvocationException)
            {
                // It ended meanwhile.
            }
        }

        /// <summary>True when the directory is gone.</summary>
        private bool TryDeleteProfile()
        {
            try
            {
                if (!Directory.Exists(ProfileDirectory))
                {
                    return true;
                }

                Directory.Delete(ProfileDirectory, true);
                return !Directory.Exists(ProfileDirectory);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ClearReadOnly();
                return false;
            }
        }

        private void ClearReadOnly()
        {
            if (BrowserLocator.Current != BrowserPlatform.Windows)
            {
                return;
            }

            try
            {
                foreach (string file in Directory.GetFiles(ProfileDirectory, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Tried again on the next attempt.
            }
        }

        /// <summary>The browser below the shell: its process id, and its start time against the reuse of that id.</summary>
        private sealed class BrowserIdentity
        {
            private readonly int _id;
            private readonly DateTime _started;

            private BrowserIdentity(int id, DateTime started)
            {
                _id = id;
                _started = started;
            }

            internal static BrowserIdentity? Read(string text)
            {
                if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 1)
                {
                    return null;
                }

                try
                {
                    using (Process process = Process.GetProcessById(id))
                    {
                        return new BrowserIdentity(id, process.StartTime);
                    }
                }
                catch (Exception)
                {
                    return null; // gone already. This runs in an output event, where nothing may be thrown.
                }
            }

            internal bool IsRunning() => WithProcess(process => { });

            internal void Kill() => WithProcess(process => process.Kill());

            /// <summary>Acts on the process only while it is the one that was started. False when it is gone.</summary>
            private bool WithProcess(Action<Process> act)
            {
                try
                {
                    using (Process process = Process.GetProcessById(_id))
                    {
                        if (Math.Abs((process.StartTime - _started).TotalSeconds) >= 1)
                        {
                            return false;
                        }

                        act(process);
                        return true;
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception
                    || e is NotSupportedException)
                {
                    return false;
                }
            }
        }

        private void Release()
        {
            AppDomain.CurrentDomain.ProcessExit -= _onExit;
            AppDomain.CurrentDomain.DomainUnload -= _onExit;
            _job?.Close();
            _process.Dispose();
        }
    }
}
