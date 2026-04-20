using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Washmachine.Models;

public sealed class CodeSnippetCatalog
{
    private readonly ReadOnlyCollection<CodeSnippetSection> _sections;
    private readonly Dictionary<string, CodeSnippetSection> _sectionsByHeader;
    private readonly Dictionary<string, CodeSnippetSection> _sectionsByTemplate;

    public CodeSnippetCatalog(IEnumerable<CodeSnippetSection> sections)
    {
        if (sections == null) throw new ArgumentNullException(nameof(sections));
        var list = sections.ToList();
        _sections = new ReadOnlyCollection<CodeSnippetSection>(list);

        var headerMap = new Dictionary<string, CodeSnippetSection>(StringComparer.OrdinalIgnoreCase);
        var templateMap = new Dictionary<string, CodeSnippetSection>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in list)
        {
            if (section == null)
                continue;

            if (!string.IsNullOrWhiteSpace(section.Header))
                headerMap[section.Header!] = section;

            if (!string.IsNullOrWhiteSpace(section.Template))
                templateMap[section.Template!] = section;
        }

        _sectionsByHeader = headerMap;
        _sectionsByTemplate = templateMap;
    }

    public IReadOnlyList<CodeSnippetSection> Sections => _sections;

    public bool TryGetByHeader(string header, [NotNullWhen(true)] out CodeSnippetSection? section)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            section = null;
            return false;
        }

        return _sectionsByHeader.TryGetValue(header, out section);
    }

    public bool TryGetByTemplate(string template, [NotNullWhen(true)] out CodeSnippetSection? section)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            section = null;
            return false;
        }

        return _sectionsByTemplate.TryGetValue(template, out section);
    }
}

public sealed class CodeSnippetSection
{
    private readonly ReadOnlyCollection<CodeSnippetItem> _items;
    private readonly ReadOnlyCollection<CodeSnippetInput> _inputs;
    private readonly Dictionary<string, CodeSnippetItem> _itemsById;

    public CodeSnippetSection(
        string header,
        string template,
        string display,
        bool allowMultiple,
        IEnumerable<CodeSnippetItem> items,
        IEnumerable<CodeSnippetInput> inputs)
    {
        Header = header ?? string.Empty;
        Template = template ?? string.Empty;
        Display = string.IsNullOrWhiteSpace(display) ? Header : display;
        AllowMultiple = allowMultiple;
        var list = (items ?? Enumerable.Empty<CodeSnippetItem>()).ToList();
        _items = new ReadOnlyCollection<CodeSnippetItem>(list);

        var itemMap = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            if (item == null)
                continue;

            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            itemMap[item.Id!] = item;
        }

        _itemsById = itemMap;
        _inputs = new ReadOnlyCollection<CodeSnippetInput>(
            (inputs ?? Enumerable.Empty<CodeSnippetInput>()).ToList());
    }

    public string Header { get; }
    public string Template { get; }
    public string Display { get; }
    public bool AllowMultiple { get; }
    public IReadOnlyList<CodeSnippetItem> Items => _items;
    public IReadOnlyList<CodeSnippetInput> Inputs => _inputs;

    public bool TryGetItem(string id, [NotNullWhen(true)] out CodeSnippetItem? item)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            item = null;
            return false;
        }

        return _itemsById.TryGetValue(id, out item);
    }
}

public sealed class CodeSnippetItem
{
    public CodeSnippetItem(
        string id,
        string display,
        string snippet,
        bool isDefault = false,
        string? includes = null,
        string? implementation = null,
        IEnumerable<CodeSnippetInput>? inputs = null)
    {
        Id = id ?? string.Empty;
        Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        Snippet = snippet ?? string.Empty;
        IsDefault = isDefault;
        Includes = includes ?? string.Empty;
        Implementation = implementation ?? string.Empty;
        Inputs = new System.Collections.ObjectModel.ReadOnlyCollection<CodeSnippetInput>(
            (inputs ?? Enumerable.Empty<CodeSnippetInput>()).ToList());
    }

    public string Id { get; }
    public string Display { get; }
    public string Snippet { get; }
    public bool IsDefault { get; }

    /// <summary>
    /// Additional <c>#include</c> directives this snippet requires beyond the template base.
    /// </summary>
    public string Includes { get; }

    /// <summary>
    /// Full C++ function implementation for this snippet. Placed before <c>main()</c>
    /// so the call in <see cref="Snippet"/> can reference it.
    /// </summary>
    public string Implementation { get; }

    /// <summary>
    /// Per-snippet configurable parameters. Input IDs are local to this snippet;
    /// the runtime prefixes them with the section+item for global uniqueness.
    /// </summary>
    public IReadOnlyList<CodeSnippetInput> Inputs { get; }
}

public sealed class CodeSnippetInput
{
    public CodeSnippetInput(
        string id,
        string label,
        SnippetInputType type,
        SnippetInputPlacement placement,
        bool required,
        int? width,
        string? placeholder,
        string? infoAction,
        string? infoButtonLabel,
        string? defaultValue = null)
    {
        Id = id ?? string.Empty;
        Label = label ?? string.Empty;
        Type = type;
        Placement = placement;
        Required = required;
        Width = width;
        Placeholder = placeholder ?? string.Empty;
        InfoAction = infoAction ?? string.Empty;
        InfoButtonLabel = infoButtonLabel ?? string.Empty;
        DefaultValue = defaultValue ?? string.Empty;
    }

    public string Id { get; }
    public string Label { get; }
    public SnippetInputType Type { get; }
    public SnippetInputPlacement Placement { get; }
    public bool Required { get; }
    public int? Width { get; }
    public string Placeholder { get; }
    public string InfoAction { get; }
    public string InfoButtonLabel { get; }
    public string DefaultValue { get; }
}

public enum SnippetInputType
{
    TextBox
}

public enum SnippetInputPlacement
{
    BeforeSelector,
    AfterSelector
}
