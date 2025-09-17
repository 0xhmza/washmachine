using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AlKesser.ShellcodeEncodingListsPopulator;
using System.Globalization;


namespace AlKesser
{
    internal static class ShellcodeEncodingListsPopulator
    {
        // --- Models ----------------------------------------------------------

        public sealed class AlgoItem
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public string Text => $"{Index} - {Name}";
        }

        private sealed class AlgoSets
        {
            public List<AlgoItem> Encoders { get; } = new();
            public List<AlgoItem> Compressors { get; } = new();
            public List<AlgoItem> Envelopes { get; } = new();
        }

        // --- Public API ------------------------------------------------------

        /// <summary>
        /// Runs: python main.py -y &lt;algos.yaml&gt; -h, parses the output, and binds three ComboBoxes.
        /// Pass a UI control (usually the Form) for Invoke() marshalling.
        /// </summary>
        public static async Task LoadAlgosIntoCombosAsync(
            Control uiContext,
            ComboBox bin2hexEncoder,
            ComboBox bin2hexCompressor,
            ComboBox bin2hexEnvelope)
        {
            
            string helpOut;
            try
            {
                helpOut = await Bin2shellService.RunPythonAsync(
                    Constants.Bin2ShellScript,
                    new[] { "-y", Constants.Bin2ShellAlgos, "-h" }
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // back to UI thread for the dialog
                if (uiContext.InvokeRequired)
                    uiContext.Invoke(new Action(() =>
                        MessageBox.Show(uiContext, $"Failed running python help:\n{ex.Message}",
                            "Bin2Shell", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                else
                    MessageBox.Show(uiContext, $"Failed running python help:\n{ex.Message}",
                        "Bin2Shell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var sets = ParseHelpOutput(helpOut);

            void BindAll()
            {
                BindCombo(bin2hexEncoder, sets.Encoders);
                BindCombo(bin2hexCompressor, sets.Compressors);
                BindCombo(bin2hexEnvelope, sets.Envelopes);
            }

            if (uiContext.InvokeRequired) uiContext.Invoke(new Action(BindAll));
            else BindAll();
        }

        // --- Internals -------------------------------------------------------
        static AlgoSets ParseHelpOutput(string help)
        {
            var sets = new AlgoSets();
            if (string.IsNullOrWhiteSpace(help))
                return sets;

            // Matches lines like: [1 ] xor
            var lineRe = new Regex(@"\[\s*(\d+)\s*\]\s+(.+)$", RegexOptions.Compiled);

            string current = null; // "Encoders" | "Compressors" | "Envelopes"

            foreach (var raw in help.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.Equals("Encoders:", StringComparison.OrdinalIgnoreCase)) { current = "Encoders"; continue; }
                if (line.Equals("Compressors:", StringComparison.OrdinalIgnoreCase)) { current = "Compressors"; continue; }
                if (line.Equals("Envelopes:", StringComparison.OrdinalIgnoreCase)) { current = "Envelopes"; continue; }

                if (current == null) continue;

                var m = lineRe.Match(line);
                if (!m.Success) continue;

                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                    continue;

                var name = m.Groups[2].Value.Trim();
                var item = new AlgoItem { Index = idx, Name = name };

                switch (current)
                {
                    case "Encoders": sets.Encoders.Add(item); break;
                    case "Compressors": sets.Compressors.Add(item); break;
                    case "Envelopes": sets.Envelopes.Add(item); break;
                }
            }

            return sets;
        }

        private static void BindCombo(ComboBox combo, IEnumerable<AlgoItem> items)
        {
            combo.BeginUpdate();
            try
            {
                var data = items
                    .OrderBy(i => i.Index)
                    .Select(i => new { i.Index, i.Name, i.Text })
                    .ToList();

                combo.DataSource = data;
                combo.DisplayMember = "Text";
                combo.ValueMember = "Index";

                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        
    }
}
