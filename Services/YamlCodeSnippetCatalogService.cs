using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Washmachine.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Washmachine.Services;

public sealed class YamlCodeSnippetCatalogService : ICodeSnippetCatalogService
{
    private readonly IAppPaths _paths;
    private readonly Lazy<CatalogBundle> _catalog;

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
        if (!File.Exists(_paths.SnippetCatalogFile))
            throw new FileNotFoundException("Snippet catalog file not found.", _paths.SnippetCatalogFile);

        string content = File.ReadAllText(_paths.SnippetCatalogFile);
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
}
