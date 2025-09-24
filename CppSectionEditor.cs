using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Washmachine.Services;

public sealed class CppSectionEditor : ICppSectionEditor
{
    public bool UncommentMethodInSection(string cppPath, string sectionName, string methodName)
    {
        if (string.IsNullOrWhiteSpace(cppPath)) throw new ArgumentException("cppPath is required");
        if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("sectionName is required");
        if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("methodName is required");
        if (!File.Exists(cppPath)) throw new FileNotFoundException("File not found", cppPath);

        var lines = File.ReadAllLines(cppPath);
        var startMarker = $"//Start#{sectionName}";
        var endMarker = $"//End#{sectionName}";

        int start = -1, end = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (start < 0 && lines[i].Trim().Equals(startMarker, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
            }
            else if (start >= 0 && lines[i].Trim().Equals(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException($"Section '{sectionName}' not found in {cppPath}.");

        var leadingComment = new Regex(@"^(\s*)//\s?(.*)$", RegexOptions.Compiled);
        bool changed = false;

        for (int i = start + 1; i < end; i++)
        {
            string line = lines[i];
            int idx = line.IndexOf(methodName, StringComparison.Ordinal);
            if (idx < 0) continue;

            int openParen = line.IndexOf('(', idx);
            if (openParen < 0) continue;

            var match = leadingComment.Match(line);
            if (match.Success)
            {
                string uncommented = match.Groups[1].Value + match.Groups[2].Value;
                lines[i] = uncommented;
                changed = true;
            }
            break;
        }

        if (changed)
        {
            File.WriteAllLines(cppPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return changed;
    }

    public void ReplaceSectionContent(string cppPath, string sectionName, string newContent)
    {
        if (string.IsNullOrWhiteSpace(cppPath)) throw new ArgumentException("cppPath is required", nameof(cppPath));
        if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("sectionName is required", nameof(sectionName));
        if (!File.Exists(cppPath)) throw new FileNotFoundException("File not found", cppPath);

        var lines = File.ReadAllLines(cppPath);
        var startMarker = $"//Start#{sectionName}";
        var endMarker = $"//End#{sectionName}";

        int start = -1, end = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (start < 0 && trimmed.Equals(startMarker, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                continue;
            }

            if (start >= 0 && trimmed.Equals(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException($"Section '{sectionName}' not found in {cppPath}.");

        string indent = new string(lines[start].TakeWhile(char.IsWhiteSpace).ToArray());
        var replacementLines = string.IsNullOrEmpty(newContent)
            ? new List<string>()
            : SplitLinesPreserveEmpty(newContent).Select(line => indent + line).ToList();

        var updated = new List<string>(lines.Length + replacementLines.Count);
        updated.AddRange(lines.Take(start + 1));
        updated.AddRange(replacementLines);
        updated.AddRange(lines.Skip(end));

        File.WriteAllLines(cppPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void ReplaceInCppFile(string filePath, string oldValue, string newValue, bool backup = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);
        if (oldValue == null)
            throw new ArgumentNullException(nameof(oldValue));
        if (newValue == null)
            newValue = string.Empty;

        var attrs = File.GetAttributes(filePath);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);

        string content;
        Encoding enc;
        using (var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
        {
            content = sr.ReadToEnd();
            enc = sr.CurrentEncoding;
        }

        int occurrences = 0;
        int start = 0;
        int idx;
        var sb = new StringBuilder(content.Length + Math.Max(0, newValue.Length - oldValue.Length));
        while ((idx = content.IndexOf(oldValue, start, StringComparison.Ordinal)) >= 0)
        {
            sb.Append(content, start, idx - start);
            sb.Append(newValue);
            start = idx + oldValue.Length;
            occurrences++;
        }
        if (occurrences == 0)
            throw new InvalidOperationException($"Marker not found: '{oldValue}' in {filePath}");

        if (start < content.Length)
            sb.Append(content, start, content.Length - start);

        if (backup)
            File.Copy(filePath, filePath + ".bak", overwrite: true);

        using var sw = new StreamWriter(filePath, append: false, encoding: enc);
        sw.Write(sb.ToString());
    }

    private static IEnumerable<string> SplitLinesPreserveEmpty(string value)
    {
        return value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }
}
