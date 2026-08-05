using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Web.Spa
{
    /// <summary>
    /// Launches the Angular CLI dev server for local development and resolves its base URI
    /// once the server accepts TCP connections. Replaces UseAngularCliServer(), which detects
    /// readiness by parsing stdout for a banner the Vite-based dev server of Angular 21 no
    /// longer prints, and therefore always times out.
    /// </summary>
    public static class AngularDevServer
    {
        private const int Port = 4200;
        private static readonly Uri BaseUri = new Uri($"http://localhost:{Port}");
        private static readonly object Sync = new object();
        private static Task<Uri> _startTask;

        /// <summary>
        /// Returns the dev server base URI, launching "npm start" in the SPA source folder if
        /// nothing is listening on the port yet. The result is cached so the launch happens at
        /// most once per host lifetime, and the launched process tree is terminated when the
        /// host shuts down.
        /// </summary>
        public static Task<Uri> EnsureStarted(string sourcePath, TimeSpan startupTimeout, CancellationToken stoppingToken)
        {
            lock (Sync)
            {
                _startTask ??= StartAndWaitForPort(sourcePath, startupTimeout, stoppingToken);
                return _startTask;
            }
        }

        #region private methods

        private static async Task<Uri> StartAndWaitForPort(string sourcePath, TimeSpan startupTimeout, CancellationToken stoppingToken)
        {
            if (await IsPortListening())
            {
                return BaseUri;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd" : "npm",
                Arguments = OperatingSystem.IsWindows() ? "/c npm start" : "start",
                WorkingDirectory = Path.GetFullPath(sourcePath),
                UseShellExecute = false,
            };

            var process = Process.Start(startInfo);
            stoppingToken.Register(() => TryKillProcessTree(process));

            var deadline = DateTime.UtcNow + startupTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await IsPortListening())
                {
                    return BaseUri;
                }
                await Task.Delay(500, stoppingToken);
            }

            throw new TimeoutException(
                $"The Angular dev server did not start listening on port {Port} within {startupTimeout.TotalSeconds:0} seconds. " +
                "Check the npm output for errors, or start it manually with 'npm start' in the ClientApp folder.");
        }

        private static async Task<bool> IsPortListening()
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("localhost", Port);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // The process may have exited on its own between the check and the kill.
            }
        }

        #endregion
    }
}
