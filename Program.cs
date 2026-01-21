using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Washmachine;

internal static class Program
{
    private const string ElevationArg = "--elevated";
    private const uint TokenQuery = 0x0008;

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // Request elevation up-front so downstream discovery can read protected paths.
        if (!EnsureElevated())
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static bool EnsureElevated()
    {
        if (IsElevated())
            return true;

        var args = Environment.GetCommandLineArgs();
        if (args.Any(arg => string.Equals(arg, ElevationArg, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                "Failed to obtain administrator access. Please restart the app and approve the UAC prompt.",
                "Elevation required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        MessageBox.Show(
            "Administrator access is required to locate Visual Studio C++ compilers.\r\n\r\n" +
            "The app will request elevation now.",
            "Administrator Required",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                MessageBox.Show(
                    "Unable to locate the executable path needed to relaunch with elevation.",
                    "Elevation error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            var argList = args
                .Skip(1)
                .Where(arg => !string.Equals(arg, ElevationArg, StringComparison.OrdinalIgnoreCase))
                .ToList();
            argList.Add(ElevationArg);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = argList.Count > 0
                    ? string.Join(" ", argList.Select(QuoteArg))
                    : string.Empty
            };

            var elevated = Process.Start(psi);
            if (elevated == null)
            {
                MessageBox.Show(
                    "Failed to launch the elevated instance.",
                    "Elevation error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user cancelled
        {
            MessageBox.Show("This app needs administrator rights to continue.", "Elevation required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to elevate: {ex.Message}", "Elevation error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return false;
    }

    private static bool IsElevated()
    {
        if (TryGetElevationState(out bool elevated))
            return elevated;

        return IsAdministrator();
    }

    private static bool TryGetElevationState(out bool elevated)
    {
        elevated = false;
        try
        {
            using var process = Process.GetCurrentProcess();
            if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
                return false;

            try
            {
                if (!GetTokenInformation(
                        token,
                        TokenInformationClass.TokenElevation,
                        out var tokenInfo,
                        Marshal.SizeOf<TokenElevation>(),
                        out _))
                {
                    return false;
                }

                elevated = tokenInfo.TokenIsElevated != 0;
                return true;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrWhiteSpace(mainModule?.FileName))
                return mainModule.FileName;
        }
        catch
        {
            // ignored
        }

        return string.IsNullOrWhiteSpace(Application.ExecutablePath)
            ? null
            : Application.ExecutablePath;
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
