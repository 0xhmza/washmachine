using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Washmachine.Models;

public sealed class CodeTemplateCatalog
{
    private readonly ReadOnlyCollection<CodeTemplateDefinition> _templates;
    private readonly Dictionary<string, CodeTemplateDefinition> _templatesById;

    public CodeTemplateCatalog(IEnumerable<CodeTemplateDefinition> templates)
    {
        if (templates == null) throw new ArgumentNullException(nameof(templates));
        var list = templates.ToList();
        _templates = new ReadOnlyCollection<CodeTemplateDefinition>(list);

        var map = new Dictionary<string, CodeTemplateDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in list)
        {
            if (template == null)
                continue;
            if (string.IsNullOrWhiteSpace(template.Id))
                continue;

            map[template.Id!] = template;
        }

        _templatesById = map;
    }

    public IReadOnlyList<CodeTemplateDefinition> Templates => _templates;

    public bool TryGetById(string id, [NotNullWhen(true)] out CodeTemplateDefinition? template)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            template = null;
            return false;
        }

        return _templatesById.TryGetValue(id, out template);
    }
}

public sealed class CodeTemplateDefinition
{
    private readonly ReadOnlyCollection<CodeTemplatePlaceholder> _placeholders;

    public CodeTemplateDefinition(
        string id,
        string display,
        string description,
        string content,
        IEnumerable<CodeTemplatePlaceholder> placeholders)
    {
        Id = id ?? string.Empty;
        Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        Description = description ?? string.Empty;
        Content = content ?? string.Empty;
        _placeholders = new ReadOnlyCollection<CodeTemplatePlaceholder>(
            (placeholders ?? Enumerable.Empty<CodeTemplatePlaceholder>()).ToList());
    }

    public string Id { get; }
    public string Display { get; }
    public string Description { get; }
    public string Content { get; }
    public IReadOnlyList<CodeTemplatePlaceholder> Placeholders => _placeholders;
}

public sealed class CodeTemplatePlaceholder
{
    public CodeTemplatePlaceholder(
        string name,
        TemplatePlaceholderKind kind,
        string? snippetTemplateKey)
    {
        Name = name ?? string.Empty;
        Kind = kind;
        SnippetTemplateKey = snippetTemplateKey ?? string.Empty;
    }

    public string Name { get; }
    public TemplatePlaceholderKind Kind { get; }
    public string SnippetTemplateKey { get; }
}

public enum TemplatePlaceholderKind
{
    System,
    Snippet
}
