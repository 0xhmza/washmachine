using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Washmachine.Services;

public sealed class HeaderListProvider : IHeaderListProvider
{
    private readonly IAppPaths _paths;

    public HeaderListProvider(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void PopulateComboFromHeaderSection(ComboBox combo, string sectionName)
    {
        if (combo == null) throw new ArgumentNullException(nameof(combo));
        var items = LoadFunctions(sectionName);

        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.AddRange(items.ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        combo.EndUpdate();
    }

    public void PopulateListFromHeaderSection(ListBox list, string sectionName)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        var items = LoadFunctions(sectionName);

        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(items.ToArray());
        list.EndUpdate();
    }

    private List<string> LoadFunctions(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name is required", nameof(sectionName));

        if (!File.Exists(_paths.ApiHeaderFile))
            throw new FileNotFoundException("Header not found", _paths.ApiHeaderFile);

        var lines = File.ReadAllLines(_paths.ApiHeaderFile);
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
