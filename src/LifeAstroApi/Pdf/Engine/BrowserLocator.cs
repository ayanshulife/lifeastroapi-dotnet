using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LifeAstroApi.Pdf.Engine
{
    internal enum BrowserPlatform
    {
        Linux,
        MacOS,
        Windows,
    }

    /// <summary>Finds Chrome, Chromium or Microsoft Edge on Windows, macOS and Linux.</summary>
    internal static class BrowserLocator
    {
        internal const string SdkVariable = "LIFEASTRO_CHROME";

        private static readonly string[] OtherVariables = { "CHROME_PATH", "PUPPETEER_EXECUTABLE_PATH" };

        private static readonly string[] LinuxNames =
        {
            "google-chrome-stable", "google-chrome", "chromium", "chromium-browser", "microsoft-edge-stable", "microsoft-edge",
        };

        internal static BrowserPlatform Current =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? BrowserPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? BrowserPlatform.MacOS
            : BrowserPlatform.Linux;

        internal static string? Find() => Find(Current, Environment.GetEnvironmentVariable, File.Exists);

        internal static string? Find(BrowserPlatform platform, Func<string, string?> environment, Func<string, bool> exists)
        {
            string? chosen = environment(SdkVariable);
            if (chosen != null && chosen.Trim().Length > 0)
            {
                if (exists(chosen))
                {
                    return chosen;
                }

                throw new LifeAstroPdfException(
                    SdkVariable + " points to '" + chosen + "', which does not exist. Set it to the browser's executable "
                        + "(on macOS the file inside the .app: .../Contents/MacOS/Google Chrome), or unset it.",
                    "browser_not_found");
            }

            foreach (string name in OtherVariables)
            {
                string? path = environment(name);
                if (path != null && path.Trim().Length > 0 && exists(path))
                {
                    return path;
                }
            }

            foreach (string candidate in Candidates(platform, environment))
            {
                if (exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static List<string> Candidates(BrowserPlatform platform, Func<string, string?> environment)
        {
            var found = new List<string>();
            if (platform == BrowserPlatform.Windows)
            {
                var roots = new List<string>();
                foreach (string variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" })
                {
                    string? root = environment(variable);
                    if (root != null && root.Length > 0 && !roots.Contains(root))
                    {
                        roots.Add(root.TrimEnd('\\'));
                    }
                }

                // Chrome first; Edge, which Windows 10 and 11 come with, last.
                foreach (string application in new[]
                {
                    "\\Google\\Chrome\\Application\\chrome.exe",
                    "\\Chromium\\Application\\chrome.exe",
                    "\\Microsoft\\Edge\\Application\\msedge.exe",
                })
                {
                    foreach (string root in roots)
                    {
                        found.Add(root + application);
                    }
                }
            }
            else if (platform == BrowserPlatform.MacOS)
            {
                string? home = environment("HOME");
                var roots = new List<string> { "/Applications" };
                if (home != null && home.Length > 0)
                {
                    roots.Add(home + "/Applications");
                }

                foreach (string application in new[]
                {
                    "/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Chromium.app/Contents/MacOS/Chromium",
                    "/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                })
                {
                    foreach (string root in roots)
                    {
                        found.Add(root + application);
                    }
                }
            }
            else
            {
                var directories = new List<string>((environment("PATH") ?? string.Empty).Split(':'));
                directories.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/snap/bin" });
                foreach (string name in LinuxNames)
                {
                    foreach (string directory in directories)
                    {
                        // Relative PATH entries are skipped: never run a program from the working directory.
                        if (directory.StartsWith("/", StringComparison.Ordinal))
                        {
                            found.Add(directory.TrimEnd('/') + "/" + name);
                        }
                    }
                }

                found.Add("/opt/google/chrome/chrome");
                found.Add("/opt/microsoft/msedge/msedge");
            }

            return found;
        }

        internal static LifeAstroPdfException NotFound(BrowserPlatform platform)
        {
            string install;
            switch (platform)
            {
                case BrowserPlatform.Windows:
                    install = "install Google Chrome (https://www.google.com/chrome/) or Microsoft Edge (https://www.microsoft.com/edge)";
                    break;
                case BrowserPlatform.MacOS:
                    install = "install Google Chrome (https://www.google.com/chrome/) or Microsoft Edge into /Applications";
                    break;
                default:
                    install = "install Chromium and the fonts for Indian scripts (Debian: 'apt-get install chromium fonts-noto-core'; "
                        + "Ubuntu: install Google Chrome from https://www.google.com/chrome/ and 'apt-get install fonts-noto-core'; "
                        + "Alpine: 'apk add chromium font-noto-devanagari font-noto-tamil' and the other Noto scripts)";
                    break;
            }

            return new LifeAstroPdfException(
                "No Chrome, Chromium or Microsoft Edge found, which makes the PDF. Either " + install
                    + "; or set LIFEASTRO_CHROME (or ChromePdfEngineOptions.ExecutablePath) to the browser's executable; "
                    + "or print with a GotenbergPdfEngine, which needs no browser on this machine.",
                "browser_not_found");
        }
    }
}
