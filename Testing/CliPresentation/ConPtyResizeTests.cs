using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;
using Washmachine.Cli.Ui;
using CatalogProgram = Washmachine.Cli.Program;

/// <summary>Real, headless Windows terminal resizing; no shell or application workflows.</summary>
internal static class ConPtyResizeTests
{
    private sealed record Report(int Id, int WindowWidth, int ProfileWidth, int HelperWidth,
        bool Redirected, string Help, string Choices, string Options, string Error);

    public static async Task RunChildAsync(string reportPath, CommandUsage usage)
    {
        // The verification launcher itself has redirected stdio. Bind this test
        // child to its attached console devices rather than those inherited pipes.
        using var consoleInput = new SafeFileHandle(CreateFile("CONIN$", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero), true);
        using var consoleOutput = new SafeFileHandle(CreateFile("CONOUT$", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero), true);
        if (consoleInput.IsInvalid || consoleOutput.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        Require(SetStdHandle(-10, consoleInput.DangerousGetHandle()));
        Require(SetStdHandle(-11, consoleOutput.DangerousGetHandle()));
        Require(SetStdHandle(-12, consoleOutput.DangerousGetHandle()));
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        Console.OutputEncoding = new UTF8Encoding(false);
        var tee = new TeeWriter(Console.Out);
        Console.SetOut(tee);
        var console = AnsiConsole.Console; // Use default native width detection, never set Profile.Width.
        int id = 0;
        while (await Console.In.ReadLineAsync() is { } command && command != "quit")
        {
            string help = "", choices = "", options = "", error = "";
            try
            {
                tee.Text.Clear();
                UsageFormatter.Print(usage);
                help = HelpWriter.Sanitize(tee.Text.ToString());
                tee.Text.Clear();
                CatalogProgram.RenderSessionChoiceCatalog("Choices",
                    [new("alpha", new string('x', 120)), new("beta", "Second choice")]);
                choices = HelpWriter.Sanitize(tee.Text.ToString());
                tee.Text.Clear();
                CatalogProgram.RenderSessionOptionCatalog("Options",
                    [new(7, "display-value", new string('z', 120), true, "Literal [brackets]", "first | second")], "End of options.");
                options = HelpWriter.Sanitize(tee.Text.ToString());
            }
            catch (Exception ex) { error = ex.ToString(); }
            var report = new Report(++id, Console.WindowWidth, console.Profile.Width,
                TerminalLayout.Width(), Console.IsOutputRedirected, help, choices, options, error);
            File.WriteAllText(reportPath + ".tmp", JsonSerializer.Serialize(report));
            File.Move(reportPath + ".tmp", reportPath, overwrite: true);
        }
    }

    public static async Task VerifyAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ConPTY resize verification requires Windows.");
        string app = Path.ChangeExtension(typeof(ConPtyResizeTests).Assembly.Location, ".exe");
        string reportPath = Path.Combine(AppContext.BaseDirectory, "resize-report.json");
        File.Delete(reportPath);
        IntPtr pc = IntPtr.Zero, attributes = IntPtr.Zero;
        IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
        ProcessInfo child = default;
        StreamWriter? input = null;
        Task? pump = null;
        var transcript = new StringBuilder();
        try
        {
            Require(CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0));
            Require(CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0));
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(new Coord(120, 60), inputRead, outputWrite, 0, out pc));
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            Require(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            Require(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020016, pc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
            var start = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            Require(CreateProcess(app, new StringBuilder($"\"{app}\" --resize-child \"{reportPath}\""),
                IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, null, ref start, out child));
            CloseHandle(inputRead); inputRead = IntPtr.Zero;
            CloseHandle(outputWrite); outputWrite = IntPtr.Zero;
            input = new StreamWriter(new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
            inputWrite = IntPtr.Zero;
            var output = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read);
            outputRead = IntPtr.Zero;
            // Drain output independently so resize and teardown cannot block on a full pipe.
            pump = Task.Run(() =>
            {
                using (output)
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = output.Read(buffer, 0, buffer.Length)) != 0)
                        lock (transcript) transcript.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }
            });
            using var process = Process.GetProcessById(child.ProcessId);
            var results = new List<object>();
            int id = 0;
            foreach (short width in new short[] { 120, 40, 80, 20, 160, 2, 80 })
            {
                Marshal.ThrowExceptionForHR(ResizePseudoConsole(pc, new Coord(width, 60)));
                await input.WriteAsync("render\r");
                await input.FlushAsync();
                Report report = await ReadReportAsync(reportPath, ++id, process, child.Process, transcript);
                if (report.Error != "") throw new InvalidOperationException(report.Error);
                if (report.Redirected || report.WindowWidth != width || report.ProfileWidth != width || report.HelperWidth != width)
                    throw new InvalidOperationException($"Native width detection failed: {JsonSerializer.Serialize(report)}");
                foreach (string text in new[] { report.Help, report.Choices, report.Options })
                    if (text.Split('\n').Any(line => line.GetCellWidth() > Math.Max(1, width - 1)))
                        throw new InvalidOperationException($"Rendered line exceeds native width {width}: {text}");
                if (report.Choices.Count(c => c == 'x') != 120 || report.Options.Count(c => c == 'z') != 120)
                    throw new InvalidOperationException($"Resize lost choice/option data at width {width}.");
                string layout = report.Options.Contains("# Name") ? "table" : "labeled rows";
                Console.WriteLine($"PASS native width {width}: Console, Spectre and helper agree; help/choices/options fit; data preserved ({layout}).");
                results.Add(new { width, report.ProfileWidth, report.HelperWidth, layout, passed = true });
            }
            await input.WriteAsync("quit\r");
            await input.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            Require(GetExitCodeProcess(child.Process, out uint exitCode));
            if (exitCode != 0) throw new InvalidOperationException($"Resize child exited {exitCode}.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "resize-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("7 real ConPTY resize transitions passed in one running process. No manual Profile.Width overrides.");
        }
        finally
        {
            if (pc != IntPtr.Zero) ClosePseudoConsole(pc);
            input?.Dispose();
            if (pump != null) await pump.WaitAsync(TimeSpan.FromSeconds(5));
            if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            foreach (var handle in new[] { inputRead, inputWrite, outputRead, outputWrite, child.Thread, child.Process })
                if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    private static async Task<Report> ReadReportAsync(string path, int id, Process process, IntPtr handle, StringBuilder transcript)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (process.HasExited)
            {
                Require(GetExitCodeProcess(handle, out uint exitCode));
                lock (transcript) throw new InvalidOperationException($"Resize child exited early ({exitCode:x}): {transcript}");
            }
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var report = await JsonSerializer.DeserializeAsync<Report>(file);
                if (report?.Id == id) return report;
            }
            catch (IOException) { }
            catch (JsonException) { }
            await Task.Delay(25);
        }
        throw new TimeoutException($"No child report for resize {id}.");
    }

    private sealed class TeeWriter(TextWriter output) : TextWriter
    {
        public StringBuilder Text { get; } = new();
        public override Encoding Encoding => output.Encoding;
        public override void Write(char value) { Text.Append(value); output.Write(value); }
        public override void Write(string? value) { Text.Append(value); output.Write(value); }
        public override void Flush() => output.Flush();
    }

    private static void Require(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private readonly struct Coord(short x, short y) { public readonly short X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short Show, ReservedCount; public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attributes, int size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, int flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr console, Coord size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, int flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, int flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFile(string name, uint access, int share, IntPtr attributes, int disposition, int flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetStdHandle(int id, IntPtr handle);
}
