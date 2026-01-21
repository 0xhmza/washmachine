using System.Reflection;
using System.Text.Json;
using Washmachine.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Washmachine.Services;

public interface ICodeSnippetCatalogService
{
    IReadOnlyList<CodeSnippetSection> GetAllSections();
    CodeSnippetSection GetSectionByHeader(string header);
    bool TryGetSectionByHeader(string header, out CodeSnippetSection section);
    IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header);
    bool TryGetSectionByTemplate(string template, out CodeSnippetSection section);
    bool TryResolveSection(string key, out CodeSnippetSection section);
    IReadOnlyList<CodeTemplateDefinition> GetTemplates();
    CodeTemplateDefinition GetTemplate(string templateId);
    bool TryGetTemplate(string templateId, out CodeTemplateDefinition template);
}

public sealed class YamlCodeSnippetCatalogService : ICodeSnippetCatalogService
{
    private readonly IAppPaths _paths;
    private readonly Lazy<CatalogBundle> _catalog;
    private const string EmbeddedCatalogResourceName = "Washmachine.Assets.vx_api_snippets.yaml";

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

    public YamlCodeSnippetCatalogService(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _catalog = new Lazy<CatalogBundle>(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<CodeSnippetSection> GetAllSections()
        => _catalog.Value.Snippets.Sections;

    public CodeSnippetSection GetSectionByHeader(string header)
    {
        if (!TryGetSectionByHeader(header, out var section))
            throw new KeyNotFoundException($"No snippet section declared for '{header}'.");

        return section;
    }

    public bool TryGetSectionByHeader(string header, out CodeSnippetSection section)
    {
        return _catalog.Value.Snippets.TryGetByHeader(header, out section);
    }

    public IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header)
    {
        return GetSectionByHeader(header).Items;
    }

    public bool TryGetSectionByTemplate(string template, out CodeSnippetSection section)
    {
        return _catalog.Value.Snippets.TryGetByTemplate(template, out section);
    }

    public bool TryResolveSection(string key, out CodeSnippetSection section)
    {
        section = default!;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (TryGetSectionByTemplate(key, out section) || TryGetSectionByHeader(key, out section))
            return true;

        string keyNorm = NormalizeKey(key);
        var sections = _catalog.Value.Snippets.Sections;

        // Score potential matches so we can pick the closest template/header name.
        var candidate = sections
            .Select(s => (Section: s, Score: MatchScore(keyNorm, s)))
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Section.Display, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate.Section != null)
        {
            section = candidate.Section;
            return true;
        }

        return false;
    }

    public IReadOnlyList<CodeTemplateDefinition> GetTemplates()
        => _catalog.Value.Templates.Templates;

    public CodeTemplateDefinition GetTemplate(string templateId)
    {
        if (!TryGetTemplate(templateId, out var template))
            throw new KeyNotFoundException($"No code template declared for '{templateId}'.");

        return template;
    }

    public bool TryGetTemplate(string templateId, out CodeTemplateDefinition template)
    {
        return _catalog.Value.Templates.TryGetById(templateId, out template);
    }

    private CatalogBundle LoadCatalog()
    {
        string content = LoadCatalogContent();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Snippet catalog is empty or invalid.");

        var dto = TryParseYaml(content) ?? TryParseJson(content);
        if (dto?.Sections == null || dto.Sections.Count == 0)
            throw new InvalidOperationException("Snippet catalog is empty or invalid.");

        var sections = dto.Sections
            .Select(section =>
            {
                var items = section.Items?.Select(item => new CodeSnippetItem(
                                item.Id ?? string.Empty,
                                item.Display ?? item.Id ?? string.Empty,
                                item.Snippet ?? string.Empty))
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
                                input.InfoButtonLabel))
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
        if (File.Exists(_paths.SnippetCatalogFile))
            return File.ReadAllText(_paths.SnippetCatalogFile);

        var assembly = Assembly.GetExecutingAssembly();
        Stream? stream = assembly.GetManifestResourceStream(EmbeddedCatalogResourceName);
        if (stream == null)
        {
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("vx_api_snippets.yaml", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(resourceName))
                stream = assembly.GetManifestResourceStream(resourceName);
        }

        if (stream == null)
            throw new FileNotFoundException("Snippet catalog file not found.", _paths.SnippetCatalogFile);

        using (stream)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }


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
                placeholders);
        }
    }

    private static SnippetCatalogDto? TryParseYaml(string content)
    {
        try
        {
            return YamlDeserializer.Deserialize<SnippetCatalogDto>(content);
        }
        catch (YamlException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static SnippetCatalogDto? TryParseJson(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<SnippetCatalogDto>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
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
    }

    private sealed class TemplateDto
    {
        public string? Id { get; set; }
        public string? Display { get; set; }
        public string? Description { get; set; }
        public string? Content { get; set; }
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

    private static string NormalizeKey(string value)
        => new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string TrimPlural(string value)
    {
        if (value.EndsWith("es", StringComparison.OrdinalIgnoreCase))
            return value[..^2];
        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return value[..^1];
        return value;
    }

    private static int MatchScore(string keyNorm, CodeSnippetSection section)
    {
        string headerNorm = NormalizeKey(section.Header);
        string templateNorm = NormalizeKey(section.Template);

        static bool Equalish(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a, TrimPlural(b), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(TrimPlural(a), b, StringComparison.OrdinalIgnoreCase);

        if (Equalish(keyNorm, templateNorm) || Equalish(keyNorm, headerNorm))
            return 0;

        if (templateNorm.Contains(keyNorm, StringComparison.OrdinalIgnoreCase) ||
            headerNorm.Contains(keyNorm, StringComparison.OrdinalIgnoreCase) ||
            keyNorm.Contains(templateNorm, StringComparison.OrdinalIgnoreCase) ||
            keyNorm.Contains(headerNorm, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        int sharedPrefix = LongestCommonPrefix(keyNorm, templateNorm);
        sharedPrefix = Math.Max(sharedPrefix, LongestCommonPrefix(keyNorm, headerNorm));
        if (sharedPrefix >= Math.Min(keyNorm.Length, templateNorm.Length) - 1 ||
            sharedPrefix >= Math.Min(keyNorm.Length, headerNorm.Length) - 1)
        {
            return 2;
        }

        if (SharesKeyword(keyNorm, templateNorm, headerNorm))
            return 3;

        return -1;
    }

    private static int LongestCommonPrefix(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        for (; i < len; i++)
        {
            if (a[i] != b[i])
                break;
        }

        return i;
    }

    private static bool SharesKeyword(string keyNorm, string templateNorm, string headerNorm)
    {
        string[] keywords = { "injection", "shellcode", "guard", "uac", "debug", "generic", "payload" };
        foreach (var keyword in keywords)
        {
            if (keyNorm.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                (templateNorm.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 headerNorm.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
