using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Washmachine.Services;

public sealed class CppSectionEditor : ICppSectionEditor
{
    /// <summary>
    /// In the given C++ file, find the section delimited by:
    ///   //Start#<sectionName>
    ///   //End#<sectionName>
    /// Then locate the first line inside that section that calls <methodName>(...),
    /// and remove a leading '//' on that line (if present). Returns true if it changed anything.
    /// </summary>
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
                start = i;
            else if (start >= 0 && lines[i].Trim().Equals(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException($"Section '{sectionName}' not found in {cppPath}.");

        // Regex to capture indentation + optional '//' at start of line
        var leadingComment = new Regex(@"^(\s*)//\s?(.*)$", RegexOptions.Compiled);

        bool changed = false;

        for (int i = start + 1; i < end; i++)
        {
            string line = lines[i];

            // Quick check: must reference the method and a '(' after it
            int idx = line.IndexOf(methodName, StringComparison.Ordinal);
            if (idx < 0) continue;

            // Ensure it's a call (methodName followed by '(' somewhere later)
            int openParen = line.IndexOf('(', idx);
            if (openParen < 0) continue;

            // If the line starts with //, remove just that leading comment
            var m = leadingComment.Match(line);
            if (m.Success)
            {
                // m.Groups[1] = indentation, m.Groups[2] = rest of line without leading //
                string uncommented = m.Groups[1].Value + m.Groups[2].Value;
                lines[i] = uncommented;
                changed = true;
            }
            // If it's already uncommented, nothing to do.
            break; // only the first matching line in the section
        }

        if (changed)
        {
            // Optional: create a .bak backup
            // File.WriteAllLines(cppPath + ".bak", lines);

            File.WriteAllLines(cppPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return changed;
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

        // Remove read-only attribute if present
        var attrs = File.GetAttributes(filePath);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);

        // Read with BOM detection and remember original encoding
        string content;
        Encoding enc;
        using (var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
        {
            content = sr.ReadToEnd();
            enc = sr.CurrentEncoding;
        }

        // Count/replace occurrences (Ordinal = literal, case-sensitive)
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

        using (var sw = new StreamWriter(filePath, append: false, encoding: enc))
            sw.Write(sb.ToString());
    }
}
