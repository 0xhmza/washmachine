using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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
        _sectionsByHeader = list
            .Where(s => !string.IsNullOrWhiteSpace(s.Header))
            .ToDictionary(s => s.Header, StringComparer.OrdinalIgnoreCase);
        _sectionsByTemplate = list
            .Where(s => !string.IsNullOrWhiteSpace(s.Template))
            .ToDictionary(s => s.Template, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CodeSnippetSection> Sections => _sections;

    public bool TryGetByHeader(string header, out CodeSnippetSection section)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            section = default!;
            return false;
        }

        return _sectionsByHeader.TryGetValue(header, out section);
    }

    public bool TryGetByTemplate(string template, out CodeSnippetSection section)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            section = default!;
            return false;
        }

        return _sectionsByTemplate.TryGetValue(template, out section);
    }
}

public sealed class CodeSnippetSection
{
    private readonly ReadOnlyCollection<CodeSnippetItem> _items;
    private readonly Dictionary<string, CodeSnippetItem> _itemsById;

    public CodeSnippetSection(
        string header,
        string template,
        string display,
        bool allowMultiple,
        IEnumerable<CodeSnippetItem> items)
    {
        Header = header ?? string.Empty;
        Template = template ?? string.Empty;
        Display = string.IsNullOrWhiteSpace(display) ? Header : display;
        AllowMultiple = allowMultiple;
        var list = (items ?? Enumerable.Empty<CodeSnippetItem>()).ToList();
        _items = new ReadOnlyCollection<CodeSnippetItem>(list);
        _itemsById = list
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Header { get; }
    public string Template { get; }
    public string Display { get; }
    public bool AllowMultiple { get; }
    public IReadOnlyList<CodeSnippetItem> Items => _items;

    public bool TryGetItem(string id, out CodeSnippetItem item)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            item = default!;
            return false;
        }

        return _itemsById.TryGetValue(id, out item);
    }
}

public sealed class CodeSnippetItem
{
    public CodeSnippetItem(string id, string display, string snippet)
    {
        Id = id ?? string.Empty;
        Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        Snippet = snippet ?? string.Empty;
    }

    public string Id { get; }
    public string Display { get; }
    public string Snippet { get; }
}
