using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlKesser
{
    internal class Bin2shellService
    {
        /// <summary>
        /// Runs a Python script and returns its STDOUT as a single string (newlines preserved).
        /// Throws if the process exits with a non-zero code (includes STDERR in the message).
        /// </summary>
        /// <param name="scriptPath">Full path to the .py script</param>
        /// <param name="args">Arguments to pass to the script (each item is one argv)</param>
        /// <param name="pythonExe">
        /// Path to the python executable. If null, uses "python" (on Windows) or "python3" (elsewhere).
        /// </param>
        public static async Task<string> RunPythonAsync(
            string scriptPath,
            IEnumerable<string> args,
            string? pythonExe = null)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
                throw new ArgumentException("scriptPath is required.", nameof(scriptPath));
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Python script not found.", scriptPath);

            // Choose a python executable if not provided
            pythonExe ??= Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3";

            // Build the full argument list: [scriptPath] + args
            var allArgs = new List<string> { scriptPath };
            if (args != null) allArgs.AddRange(args);

            // Prefer ProcessStartInfo.ArgumentList when available; otherwise build a quoted string
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            #if NET5_0_OR_GREATER
            foreach (var a in allArgs) psi.ArgumentList.Add(a);
            #else
            psi.Arguments = string.Join(" ", allArgs.Select(QuoteArg));
            #endif

            using var proc = new Process { StartInfo = psi };

            // Start process
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start python process.");

            // Read both streams fully (preserves line breaks, supports MBs of text)
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

            #if NET5_0_OR_GREATER
            await proc.WaitForExitAsync().ConfigureAwait(false);
            #else
            await Task.Run(() => proc.WaitForExit()).ConfigureAwait(false);
            #endif

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0)
                throw new ApplicationException(
                    $"Python exited with code {proc.ExitCode}.\n\nSTDERR:\n{stderr}");

            return stdout;

            // --- helpers ---

        }
    }
}
