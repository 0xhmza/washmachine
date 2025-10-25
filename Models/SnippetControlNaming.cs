using System;
using System.Globalization;
using System.Text;

namespace Washmachine.Models;

public static class SnippetControlNaming
{
    private const string ComboPrefix = "snippetCombo";
    private const string ListPrefix = "snippetList";

    public static string GetSectionKey(CodeSnippetSection section)
    {
        if (section == null) throw new ArgumentNullException(nameof(section));
        return NormalizeIdentifier(!string.IsNullOrWhiteSpace(section.Template)
            ? section.Template
            : section.Header);
    }

    public static string GetComboName(CodeSnippetSection section, int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return $"{ComboPrefix}_{GetSectionKey(section)}_{index}";
    }

    public static string GetComboPrefix(CodeSnippetSection section)
    {
        return $"{ComboPrefix}_{GetSectionKey(section)}_";
    }

    public static string GetComboPrefix(string templateOrHeader)
    {
        return $"{ComboPrefix}_{NormalizeIdentifier(templateOrHeader)}_";
    }

    public static bool TryMatchComboName(CodeSnippetSection section, string controlName, out int index)
    {
        return TryMatchComboNameInternal(GetComboPrefix(section), controlName, out index);
    }

    public static bool TryMatchComboName(string templateOrHeader, string controlName, out int index)
    {
        return TryMatchComboNameInternal(GetComboPrefix(templateOrHeader), controlName, out index);
    }

    public static bool IsComboNameMatch(CodeSnippetSection section, string controlName)
    {
        return TryMatchComboName(section, controlName, out _);
    }

    public static bool IsComboNameMatch(string templateOrHeader, string controlName)
    {
        return TryMatchComboName(templateOrHeader, controlName, out _);
    }

    public static string GetListName(CodeSnippetSection section, int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return $"{ListPrefix}_{GetSectionKey(section)}_{index}";
    }

    public static string GetListPrefix(CodeSnippetSection section)
    {
        return $"{ListPrefix}_{GetSectionKey(section)}_";
    }

    public static string GetListPrefix(string templateOrHeader)
    {
        return $"{ListPrefix}_{NormalizeIdentifier(templateOrHeader)}_";
    }

    public static bool TryMatchListName(CodeSnippetSection section, string controlName, out int index)
    {
        return TryMatchComboNameInternal(GetListPrefix(section), controlName, out index);
    }

    public static bool TryMatchListName(string templateOrHeader, string controlName, out int index)
    {
        return TryMatchComboNameInternal(GetListPrefix(templateOrHeader), controlName, out index);
    }

    public static bool IsListNameMatch(CodeSnippetSection section, string controlName)
    {
        return TryMatchListName(section, controlName, out _);
    }

    public static bool IsListNameMatch(string templateOrHeader, string controlName)
    {
        return TryMatchListName(templateOrHeader, controlName, out _);
    }

    private static bool TryMatchComboNameInternal(string prefix, string controlName, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(controlName))
            return false;

        if (!controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = controlName.Substring(prefix.Length);
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNSPECIFIED";

        var buffer = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(char.ToUpperInvariant(ch));
            }
            else if (ch == '_')
            {
                buffer.Append('_');
            }
        }

        return buffer.Length == 0 ? "UNSPECIFIED" : buffer.ToString();
    }
}
