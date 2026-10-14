using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Washmachine.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Washmachine.Services;

public interface IPlaybookService
{
    IReadOnlyList<CodeSnippetSection> GetAllSections();
    CodeSnippetSection GetSectionByHeader(string header);
    bool TryGetSectionByHeader(string header, [NotNullWhen(true)] out CodeSnippetSection? section);
    IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header);
    bool TryGetSectionByTemplate(string template, [NotNullWhen(true)] out CodeSnippetSection? section);
    bool TryResolveSection(string key, [NotNullWhen(true)] out CodeSnippetSection? section);
    IReadOnlyList<CodeTemplateDefinition> GetTemplates();
    CodeTemplateDefinition GetTemplate(string templateId);
    bool TryGetTemplate(string templateId, [NotNullWhen(true)] out CodeTemplateDefinition? template);
    bool TryReload(out string? error);
}

/// <summary>
/// Loads snippet/template catalogs from YAML or JSON (file or embedded resource).
/// </summary>
public sealed class PlaybookService : IPlaybookService
{
    private readonly IAppPaths _paths;
    private Lazy<CatalogBundle> _catalog;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public PlaybookService(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _catalog = CreateCatalog();
    }

    public IReadOnlyList<CodeSnippetSection> GetAllSections()
        => _catalog.Value.Snippets.Sections;

    public CodeSnippetSection GetSectionByHeader(string header)
    {
        if (!TryGetSectionByHeader(header, out var section))
            throw new KeyNotFoundException($"No snippet section declared for '{header}'.");

        return section;
    }

    public bool TryGetSectionByHeader(string header, [NotNullWhen(true)] out CodeSnippetSection? section)
    {
        return _catalog.Value.Snippets.TryGetByHeader(header, out section);
    }

    public IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header)
    {
        return GetSectionByHeader(header).Items;
    }

    public bool TryGetSectionByTemplate(string template, [NotNullWhen(true)] out CodeSnippetSection? section)
    {
        return _catalog.Value.Snippets.TryGetByTemplate(template, out section);
    }

    public bool TryResolveSection(string key, [NotNullWhen(true)] out CodeSnippetSection? section)
    {
        section = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        // Exact match only (case-insensitive). Fuzzy MatchScore / SharesKeyword resolution
        // was deleted in P0-4 — it silently rescued typos in YAML template/header fields
        // and could pick the wrong section. If a caller's key doesn't match, fix the catalog.
        return TryGetSectionByTemplate(key, out section) || TryGetSectionByHeader(key, out section);
    }

    public IReadOnlyList<CodeTemplateDefinition> GetTemplates()
        => _catalog.Value.Templates.Templates;

    public CodeTemplateDefinition GetTemplate(string templateId)
    {
        if (!TryGetTemplate(templateId, out var template))
            throw new KeyNotFoundException($"No code template declared for '{templateId}'.");

        return template;
    }

    public bool TryGetTemplate(string templateId, [NotNullWhen(true)] out CodeTemplateDefinition? template)
    {
        return _catalog.Value.Templates.TryGetById(templateId, out template);
    }

    public bool TryReload(out string? error)
    {
        try
        {
            _catalog = CreateCatalog();
            _ = _catalog.Value;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private CatalogBundle LoadCatalog()
    {
        string path = _paths.ActivePlaybookFullPath;
        string content = LoadCatalogContent();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"Playbook '{path}' is empty.");

        SnippetCatalogDto? dto = ParseCatalog(content, path);
        if (dto == null)
            throw new InvalidOperationException(
                $"Playbook '{path}' could not be parsed as YAML or JSON.");

        if (dto.Sections == null || dto.Sections.Count == 0)
            throw new InvalidOperationException(
                $"Playbook '{path}' contains no sections.");

        ValidateCatalogShape(dto, path);

        var sections = dto.Sections
            .Select(section =>
            {
                var items = section.Items?.Select(item => new CodeSnippetItem(
                                item.Id ?? string.Empty,
                                item.Display ?? item.Id ?? string.Empty,
                                item.Snippet ?? string.Empty,
                                item.Default,
                                item.Includes,
                                item.Implementation,
                                item.Inputs?.Select(inp => new CodeSnippetInput(
                                    inp.Id ?? string.Empty,
                                    inp.Label ?? string.Empty,
                                    ParseInputType(inp.Type),
                                    ParseInputPlacement(inp.Placement),
                                    inp.Required,
                                    inp.Width,
                                    inp.Placeholder,
                                    inp.InfoAction,
                                    inp.InfoButtonLabel,
                                    inp.DefaultValue)),
                                item.Requires))
                            ?? Enumerable.Empty<CodeSnippetItem>();

                var inputs = section.Inputs?.Select(input => new CodeSnippetInput(
                                input.Id ?? string.Empty,
                                input.Label ?? string.Empty,
                                ParseInputType(input.Type),
                                ParseInputPlacement(input.Placement),
                                input.Required,
                                input.Width,
                                input.Placeholder,
                                input.InfoAction,
                                input.InfoButtonLabel,
                                input.DefaultValue))
                            ?? Enumerable.Empty<CodeSnippetInput>();

                return new CodeSnippetSection(
                    section.Header ?? string.Empty,
                    section.Template ?? string.Empty,
                    section.Display ?? section.Header ?? string.Empty,
                    section.AllowMultiple,
                    items,
                    inputs);
            })
            .ToList();

        var templates = BuildTemplates(dto);

        return new CatalogBundle(
            new CodeSnippetCatalog(sections),
            new CodeTemplateCatalog(templates));
    }

    private string LoadCatalogContent()
    {
        if (File.Exists(_paths.ActivePlaybookFullPath))
            return File.ReadAllText(_paths.ActivePlaybookFullPath);
        throw new FileNotFoundException("Playbook file not found.", _paths.ActivePlaybookFullPath);
    }

    private Lazy<CatalogBundle> CreateCatalog()
        => new(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);


    private static IEnumerable<CodeTemplateDefinition> BuildTemplates(SnippetCatalogDto dto)
    {
        var templates = dto.Templates ?? new List<TemplateDto>();
        foreach (var templateDto in templates)
        {
            var placeholders = templateDto.Placeholders?
                .Select(p => new CodeTemplatePlaceholder(
                    p.Name ?? string.Empty,
                    ParsePlaceholderKind(p.Kind),
                    p.SnippetTemplate))
                ?? Enumerable.Empty<CodeTemplatePlaceholder>();

            yield return new CodeTemplateDefinition(
                templateDto.Id ?? string.Empty,
                templateDto.Display ?? templateDto.Id ?? string.Empty,
                templateDto.Description ?? string.Empty,
                templateDto.Content ?? string.Empty,
                placeholders,
                templateDto.Preamble);
        }
    }

    private static SnippetCatalogDto? ParseCatalog(string content, string path)
    {
        // Try YAML first (the canonical format). On a YAML parse error, capture the
        // line/column so the user can find the bad line — but fall through to JSON
        // before raising, since some catalogs (e.g. exported from tools) may be JSON.
        Exception? yamlError = null;
        try
        {
            return YamlDeserializer.Deserialize<SnippetCatalogDto>(content);
        }
        catch (YamlException ex)
        {
            yamlError = ex;
        }
        catch (InvalidOperationException ex)
        {
            yamlError = ex;
        }

        try
        {
            return JsonSerializer.Deserialize<SnippetCatalogDto>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // Not JSON either — surface the YAML error since YAML is the canonical format.
        }
        catch (NotSupportedException)
        {
        }

        if (yamlError is YamlException yamlEx)
        {
            var start = yamlEx.Start;
            throw new InvalidOperationException(
                $"YAML parse error in '{path}' at line {start.Line}, column {start.Column}: {yamlEx.Message}",
                yamlEx);
        }

        if (yamlError != null)
            throw new InvalidOperationException(
                $"YAML parse error in '{path}': {yamlError.Message}", yamlError);

        return null;
    }

    /// <summary>
    /// Walks the parsed DTO and raises an <see cref="InvalidOperationException"/> with
    /// section/item context for any structural issue: missing ids, duplicate templates,
    /// unknown <c>requires:</c> tokens, snippet placeholders pointing at sections that
    /// don't exist, etc. The exception message is operator-actionable, not a stack trace.
    /// </summary>
    private static void ValidateCatalogShape(SnippetCatalogDto dto, string path)
    {
        var errors = new List<string>();

        // ── Sections: every section needs at least a header or a template, ids must be
        //    unique within a section, and the template name (if present) must be unique
        //    across sections so TryGetByTemplate is unambiguous.
        var seenTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, sIdx) in dto.Sections!.Select((s, i) => (s, i)))
        {
            string label = !string.IsNullOrWhiteSpace(section.Template)
                ? $"section[{sIdx}] template='{section.Template}'"
                : !string.IsNullOrWhiteSpace(section.Header)
                    ? $"section[{sIdx}] header='{section.Header}'"
                    : $"section[{sIdx}]";

            if (string.IsNullOrWhiteSpace(section.Header) && string.IsNullOrWhiteSpace(section.Template))
                errors.Add($"{label}: must declare either 'header' or 'template'.");

            if (!string.IsNullOrWhiteSpace(section.Template))
            {
                if (!seenTemplates.Add(section.Template!.Trim()))
                    errors.Add($"{label}: template '{section.Template}' is already used by another section.");
            }

            var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (item, iIdx) in (section.Items ?? new()).Select((it, i) => (it, i)))
            {
                string itemLabel = $"{label} item[{iIdx}]";

                if (string.IsNullOrWhiteSpace(item.Id))
                    errors.Add($"{itemLabel}: 'id' is required.");
                else if (!seenItemIds.Add(item.Id!.Trim()))
                    errors.Add($"{itemLabel}: id '{item.Id}' duplicates an earlier item in the same section.");

                if (item.Requires != null)
                {
                    foreach (var token in item.Requires)
                    {
                        if (string.IsNullOrWhiteSpace(token))
                            continue;
                        if (!KnownRequiresTokens.Map.ContainsKey(token.Trim()))
                            errors.Add(
                                $"{itemLabel}: requires '{token}' is not a known capability token. " +
                                $"Add it to KnownRequiresTokens.Map or remove it from the catalog.");
                    }
                }
            }
        }

        // ── Templates: ids unique, snippet-kind placeholders must reference an existing section template.
        var seenTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (templ, tIdx) in (dto.Templates ?? new()).Select((t, i) => (t, i)))
        {
            string label = !string.IsNullOrWhiteSpace(templ.Id)
                ? $"template[{tIdx}] id='{templ.Id}'"
                : $"template[{tIdx}]";

            if (string.IsNullOrWhiteSpace(templ.Id))
                errors.Add($"{label}: 'id' is required.");
            else if (!seenTemplateIds.Add(templ.Id!.Trim()))
                errors.Add($"{label}: id duplicates an earlier template.");

            foreach (var (ph, pIdx) in (templ.Placeholders ?? new()).Select((p, i) => (p, i)))
            {
                string phLabel = $"{label} placeholder[{pIdx}]";
                if (string.IsNullOrWhiteSpace(ph.Name))
                    errors.Add($"{phLabel}: 'name' is required.");

                bool isSnippetKind = string.Equals(ph.Kind, "snippet", StringComparison.OrdinalIgnoreCase);
                if (isSnippetKind)
                {
                    if (string.IsNullOrWhiteSpace(ph.SnippetTemplate))
                        errors.Add($"{phLabel}: snippet-kind placeholder must declare 'snippetTemplate'.");
                    else if (!seenTemplates.Contains(ph.SnippetTemplate!.Trim()))
                        errors.Add(
                            $"{phLabel}: snippetTemplate '{ph.SnippetTemplate}' does not match any " +
                            $"section's 'template' field. Fix the spelling in the catalog.");
                }
            }
        }

        if (errors.Count > 0)
        {
            var msg = $"Playbook '{path}' has {errors.Count} validation error(s):" +
                      Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", errors);
            throw new InvalidOperationException(msg);
        }
    }

    private static TemplatePlaceholderKind ParsePlaceholderKind(string? value)
        => value switch
        {
            null or "" => TemplatePlaceholderKind.System,
            var v when string.Equals(v, "system", StringComparison.OrdinalIgnoreCase) => TemplatePlaceholderKind.System,
            var v when string.Equals(v, "snippet", StringComparison.OrdinalIgnoreCase) => TemplatePlaceholderKind.Snippet,
            _ => TemplatePlaceholderKind.System
        };

    private sealed class CatalogBundle
    {
        public CatalogBundle(CodeSnippetCatalog snippets, CodeTemplateCatalog templates)
        {
            Snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));
            Templates = templates ?? throw new ArgumentNullException(nameof(templates));
        }

        public CodeSnippetCatalog Snippets { get; }
        public CodeTemplateCatalog Templates { get; }
    }

    private sealed class SnippetCatalogDto
    {
        public List<SnippetSectionDto>? Sections { get; set; }
        public List<TemplateDto>? Templates { get; set; }
    }

    private sealed class SnippetSectionDto
    {
        public string? Header { get; set; }
        public string? Template { get; set; }
        public string? Display { get; set; }
        public bool AllowMultiple { get; set; }
        public List<SnippetItemDto>? Items { get; set; }
        public List<SnippetInputDto>? Inputs { get; set; }
    }

    private sealed class SnippetItemDto
    {
        public string? Id { get; set; }
        public string? Display { get; set; }
        public string? Snippet { get; set; }
        public bool Default { get; set; }
        public string? Includes { get; set; }
        public string? Implementation { get; set; }
        public List<SnippetInputDto>? Inputs { get; set; }
        public List<string>? Requires { get; set; }
    }

    private sealed class SnippetInputDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Type { get; set; }
        public string? Placement { get; set; }
        public bool Required { get; set; }
        public int? Width { get; set; }
        public string? Placeholder { get; set; }
        public string? InfoAction { get; set; }
        public string? InfoButtonLabel { get; set; }
        public string? DefaultValue { get; set; }
    }

    private sealed class TemplateDto
    {
        public string? Id { get; set; }
        public string? Display { get; set; }
        public string? Description { get; set; }
        public string? Content { get; set; }
        public string? Preamble { get; set; }
        public List<TemplatePlaceholderDto>? Placeholders { get; set; }
    }

    private sealed class TemplatePlaceholderDto
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? SnippetTemplate { get; set; }
    }

    private static SnippetInputType ParseInputType(string? value)
        => value switch
        {
            null or "" => SnippetInputType.TextBox,
            var v when string.Equals(v, "textbox", StringComparison.OrdinalIgnoreCase) => SnippetInputType.TextBox,
            _ => SnippetInputType.TextBox
        };

    private static SnippetInputPlacement ParseInputPlacement(string? value)
        => value switch
        {
            null or "" => SnippetInputPlacement.AfterSelector,
            var v when string.Equals(v, "before", StringComparison.OrdinalIgnoreCase) => SnippetInputPlacement.BeforeSelector,
            var v when string.Equals(v, "after", StringComparison.OrdinalIgnoreCase) => SnippetInputPlacement.AfterSelector,
            _ => SnippetInputPlacement.AfterSelector
        };

}
