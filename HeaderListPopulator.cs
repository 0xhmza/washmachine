using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public static class HeaderListPopulator
{
    /// <summary>
    /// Populates a ComboBox with function names from a specific header section.
    /// </summary>
    public static void PopulateComboFromHeaderSection(ComboBox combo, string headerPath, string sectionName)
    {
        if (combo == null) throw new ArgumentNullException(nameof(combo));
        var items = LoadFunctions(headerPath, sectionName);

        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.AddRange(items.ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0; // optional
        combo.EndUpdate();
    }

    /// <summary>
    /// Populates a ListBox with function names from a specific header section.
    /// </summary>
    public static void PopulateListFromHeaderSection(ListBox list, string headerPath, string sectionName)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        var items = LoadFunctions(headerPath, sectionName);

        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(items.ToArray());
        list.EndUpdate();
    }

    // Shared logic: parse the header and extract function names
    private static List<string> LoadFunctions(string headerPath, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(headerPath)) throw new ArgumentException("Header path is required", nameof(headerPath));
        if (!File.Exists(headerPath)) throw new FileNotFoundException("Header not found", headerPath);
        if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Section name is required", nameof(sectionName));

        var lines = File.ReadAllLines(headerPath);
        return ExtractFunctionNamesFromSection(lines, sectionName);
    }

    private static List<string> ExtractFunctionNamesFromSection(string[] lines, string sectionName)
    {
        var names = new List<string>();

        // Find the section header line
        int iName = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), sectionName, StringComparison.OrdinalIgnoreCase))
            {
                bool looksLikeBanner =
                    i > 0 && lines[i - 1].TrimStart().StartsWith("/*") &&
                    i + 1 < lines.Length && lines[i + 1].Contains("*/");

                if (looksLikeBanner) { iName = i; break; }
            }
        }
        if (iName == -1) return names;

        int start = iName + 2;

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("/*")) break;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
            if (!line.Contains("(") || !line.Contains(")") || !line.EndsWith(";")) continue;

            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line.Substring(0, slashes).TrimEnd();

            string beforeParen = line[..line.IndexOf('(')];
            var tokens = Regex.Split(beforeParen, @"[\s\*\&]+")
                              .Where(t => !string.IsNullOrWhiteSpace(t))
                              .ToList();
            if (tokens.Count == 0) continue;

            string candidate = tokens[^1];
            if (Regex.IsMatch(candidate, @"^[A-Za-z_]\w*$"))
                names.Add(candidate);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<string>();
        foreach (var n in names)
            if (seen.Add(n)) unique.Add(n);

        return unique;
    }
}
