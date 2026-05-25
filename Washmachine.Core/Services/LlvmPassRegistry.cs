using System.Text.Json;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Discovers LLVM obfuscation passes registered in the Assets/llvm-passes directory.
/// Each pass directory contains a pass.json metadata file and optionally a compiled pass.dll.
/// </summary>
public sealed class LlvmPassRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;

    public LlvmPassRegistry(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads all registered LLVM passes from the passes directory.
    /// Missing or malformed pass.json files are skipped with a warning.
    /// </summary>
    public IReadOnlyList<LlvmPassDefinition> GetAllPasses()
    {
        var passesDir = _paths.LlvmPassesDirectory;
        if (!Directory.Exists(passesDir))
        {
            _logger.Debug($"LLVM passes directory not found: {passesDir}");
            return Array.Empty<LlvmPassDefinition>();
        }

        var results = new List<LlvmPassDefinition>();

        foreach (var dir in Directory.EnumerateDirectories(passesDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var jsonPath = Path.Combine(dir, "pass.json");
            if (!File.Exists(jsonPath))
                continue;

            try
            {
                var json = File.ReadAllText(jsonPath);
                var meta = JsonSerializer.Deserialize<PassMeta>(json, JsonOptions);
                if (meta is null || string.IsNullOrWhiteSpace(meta.Id))
                {
                    _logger.Warn($"LLVM pass metadata is empty or missing 'id' in: {jsonPath}");
                    continue;
                }

                var dllPath = Path.Combine(dir, "pass.dll");
                results.Add(new LlvmPassDefinition
                {
                    Id = meta.Id,
                    Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Id : meta.Name,
                    Description = meta.Description ?? string.Empty,
                    OptPassName = meta.OptName,
                    PluginPath = File.Exists(dllPath) ? dllPath : null,
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to load LLVM pass from '{dir}': {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Returns only the passes that have a compiled plugin DLL ready for loading.
    /// </summary>
    public IReadOnlyList<LlvmPassDefinition> GetBuiltPasses()
        => GetAllPasses().Where(p => p.IsBuilt).ToList();

    /// <summary>
    /// Tries to resolve passes by their IDs, returning the built ones among them.
    /// Logs a warning for any requested ID that is not registered or not yet built.
    /// </summary>
    public IReadOnlyList<LlvmPassDefinition> ResolveEnabledPasses(IEnumerable<string> requestedIds)
    {
        var all = GetAllPasses().ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<LlvmPassDefinition>();

        foreach (var id in requestedIds)
        {
            if (!all.TryGetValue(id, out var pass))
            {
                _logger.Warn($"LLVM pass '{id}' is not registered. Skipping.");
                continue;
            }

            if (!pass.IsBuilt)
            {
                _logger.Warn($"LLVM pass '{id}' ({pass.Name}) is missing its opt_name in pass.json — skipping.");
                continue;
            }

            resolved.Add(pass);
        }

        return resolved;
    }

    private sealed class PassMeta
    {
        public string Id { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Description { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("opt_name")]
        public string? OptName { get; init; }
    }
}
