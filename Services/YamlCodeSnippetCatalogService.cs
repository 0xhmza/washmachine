using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Washmachine.Models;

namespace Washmachine.Services;

public sealed class YamlCodeSnippetCatalogService : ICodeSnippetCatalogService
{
    private readonly IAppPaths _paths;
    private readonly Lazy<CodeSnippetCatalog> _catalog;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public YamlCodeSnippetCatalogService(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _catalog = new Lazy<CodeSnippetCatalog>(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CodeSnippetSection GetSectionByHeader(string header)
    {
        if (!TryGetSectionByHeader(header, out var section))
            throw new KeyNotFoundException($"No snippet section declared for '{header}'.");

        return section;
    }

    public bool TryGetSectionByHeader(string header, out CodeSnippetSection section)
    {
        return _catalog.Value.TryGetByHeader(header, out section);
    }

    public IReadOnlyList<CodeSnippetItem> GetItemsForHeader(string header)
    {
        return GetSectionByHeader(header).Items;
    }

    private CodeSnippetCatalog LoadCatalog()
    {
        if (!File.Exists(_paths.SnippetCatalogFile))
            throw new FileNotFoundException("Snippet catalog file not found.", _paths.SnippetCatalogFile);

        using var stream = File.OpenRead(_paths.SnippetCatalogFile);
        var dto = JsonSerializer.Deserialize<SnippetCatalogDto>(stream, JsonOptions);
        if (dto?.Sections == null)
            throw new InvalidOperationException("Snippet catalog is empty or invalid.");

        var sections = dto.Sections
            .Select(section => new CodeSnippetSection(
                section.Header ?? string.Empty,
                section.Template ?? string.Empty,
                section.Display ?? section.Header ?? string.Empty,
                section.AllowMultiple,
                section.Items?.Select(item => new CodeSnippetItem(
                    item.Id ?? string.Empty,
                    item.Display ?? item.Id ?? string.Empty,
                    item.Snippet ?? string.Empty)) ?? Enumerable.Empty<CodeSnippetItem>()))
            .ToList();

        return new CodeSnippetCatalog(sections);
    }

    private sealed class SnippetCatalogDto
    {
        public List<SnippetSectionDto>? Sections { get; set; }
    }

    private sealed class SnippetSectionDto
    {
        public string? Header { get; set; }
        public string? Template { get; set; }
        public string? Display { get; set; }
        public bool AllowMultiple { get; set; }
        public List<SnippetItemDto>? Items { get; set; }
    }

    private sealed class SnippetItemDto
    {
        public string? Id { get; set; }
        public string? Display { get; set; }
        public string? Snippet { get; set; }
    }
}
