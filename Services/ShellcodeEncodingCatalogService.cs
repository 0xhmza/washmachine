using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Washmachine.Services;

public interface IShellcodeEncodingCatalog
{
    Task<ShellcodeEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed record ShellcodeEncodingCatalog(
    IReadOnlyList<ShellcodeEncodingItem> Encoders,
    IReadOnlyList<ShellcodeEncodingItem> Compressors,
    IReadOnlyList<ShellcodeEncodingItem> Envelopes);

public sealed record ShellcodeEncodingItem(int Index, string Name)
{
    public string DisplayText => $"{Index} - {Name}";
}

public sealed class ShellcodeEncodingCatalogService : IShellcodeEncodingCatalog
{
    private readonly IAppPaths _paths;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public ShellcodeEncodingCatalogService(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<ShellcodeEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        string catalogPath = _paths.Bin2ShellAlgos;
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new InvalidOperationException("Bin2Shell algorithm catalog path is not configured.");
        if (!File.Exists(catalogPath))
            throw new FileNotFoundException("Bin2Shell algorithm catalog not found.", catalogPath);

        string content = await File.ReadAllTextAsync(catalogPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Bin2Shell algorithm catalog is empty.");

        Bin2ShellCatalogDto? dto;
        try
        {
            dto = YamlDeserializer.Deserialize<Bin2ShellCatalogDto>(content);
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException("Failed to parse Bin2Shell algorithm catalog.", ex);
        }

        var encoders = BuildItems(dto?.Encoders);
        var compressors = BuildItems(dto?.Compressors);
        var envelopes = BuildItems(dto?.Envelopes);

        return new ShellcodeEncodingCatalog(encoders, compressors, envelopes);
    }

    private static IReadOnlyList<ShellcodeEncodingItem> BuildItems(IEnumerable<Bin2ShellAlgorithmDto>? entries)
    {
        if (entries == null)
            return Array.Empty<ShellcodeEncodingItem>();

        return entries
            .Select(CreateItem)
            .Where(item => item != null)
            .Cast<ShellcodeEncodingItem>()
            .OrderBy(item => item.Index)
            .ToArray();
    }

    private static ShellcodeEncodingItem? CreateItem(Bin2ShellAlgorithmDto? dto)
    {
        if (dto == null)
            return null;

        string name = string.IsNullOrWhiteSpace(dto.Name)
            ? dto.Index.ToString(CultureInfo.InvariantCulture)
            : dto.Name.Trim();

        return new ShellcodeEncodingItem(dto.Index, name);
    }

    private sealed class Bin2ShellCatalogDto
    {
        public List<Bin2ShellAlgorithmDto>? Encoders { get; set; }
        public List<Bin2ShellAlgorithmDto>? Compressors { get; set; }
        public List<Bin2ShellAlgorithmDto>? Envelopes { get; set; }
    }

    private sealed class Bin2ShellAlgorithmDto
    {
        public int Index { get; set; }
        public string? Name { get; set; }
    }
}
