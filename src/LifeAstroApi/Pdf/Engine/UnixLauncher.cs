using System.Collections.Generic;
using System.IO;

namespace LifeAstroApi.Pdf.Engine
{
    /// <summary>
    /// Starts the browser on Linux and macOS so that it cannot outlive the application, however that ends
    /// (SIGTERM, which no longer raises ProcessExit on .NET 10, SIGKILL or a crash).
    /// </summary>
    /// <remarks>
    /// A small <c>/bin/sh</c> script gives the browser a pipe from this process as its DevTools pipe. When this process ends, for whatever reason, the kernel closes that pipe and
    /// Chrome quits; the script then deletes the profile. The commands themselves go over the WebSocket.
    /// </remarks>
    internal static class UnixLauncher
    {
        internal const string Shell = "/bin/sh";

        /// <summary>The script's first line of output: the browser's own process id follows.</summary>
        internal const string PidAnnouncement = "lifeastro-browser-pid ";

        /// <summary>Makes Chrome watch file descriptors 3 and 4 and quit when they close.</summary>
        internal const string PipeSwitch = "--remote-debugging-pipe";

        // $1 is the profile, the rest is the browser and its arguments. A signal to the script stops only the
        // child it started: after `wait` has reaped that child, its id is never used again.
        private const string Script =
            "profile=$1; shift; "
            + "exec 3<&0 4>&1; "
            + "\"$@\" 3<&3 4>&4 </dev/null >&2 & browser=$!; " // named again: ksh closes them on exec
            + "exec 3<&- 4>&-; "
            + "trap '' INT HUP PIPE; "
            + "echo \"" + PidAnnouncement + "$browser\" >&2; "
            + "trap 'kill -KILL \"$browser\" 2>/dev/null' TERM; "
            + "wait \"$browser\"; code=$?; "
            + "trap '' TERM; "
            + "wait \"$browser\" 2>/dev/null; "
            + "n=0; rm -rf -- \"$profile\" 2>/dev/null; "
            + "while [ -e \"$profile\" ] && [ $n -lt 50 ]; do sleep 0.1; rm -rf -- \"$profile\" 2>/dev/null; n=$((n+1)); done; "
            + "exit $code";

        internal static bool IsAvailable(BrowserPlatform platform) => platform != BrowserPlatform.Windows && File.Exists(Shell);

        internal static List<string> Arguments(string executable, string profile, List<string> browserArguments)
        {
            var arguments = new List<string> { "-c", Script, "lifeastro-browser", profile, executable, PipeSwitch };
            arguments.AddRange(browserArguments);
            return arguments;
        }
    }
}
