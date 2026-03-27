using System.Globalization;
using System.Text.RegularExpressions;

namespace Washmachine.Services;

public interface IShellcodeEncodingCatalog
{
    Task<ShellcodeEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed record ShellcodeEncodingCatalog(
    IReadOnlyList<ShellcodeEncodingItem> Encoders,
    IReadOnlyList<ShellcodeEncodingItem> Compressors,
    IReadOnlyList<ShellcodeEncodingItem> Envelopes,
    IReadOnlyList<AntiEmulationOption> AntiEmulation,
    IReadOnlyList<ShellcodeEncodingItem> WebHelpers);

public sealed record ShellcodeEncodingItem(int Index, string Name, string Description = "")
{
    public string ShortDisplay => Name;
    public string DisplayText => string.IsNullOrWhiteSpace(Description)
        ? $"{Index} - {Name}"
        : $"{Index} - {Name}  {Description}";
}

public sealed record AntiEmulationOption(int Index, string Name, string Description, string? ArgsHint)
{
    public string DisplayText => $"{Index} - {Name}";
    public bool RequiresArguments => !string.IsNullOrWhiteSpace(ArgsHint);
}

/// <summary>
/// Parses Bin2Shell help output to build encoder/envelope catalogs for the UI.
/// </summary>
public sealed class ShellcodeEncodingCatalogService : IShellcodeEncodingCatalog
{
    private static readonly Regex LineRegex = new(@"\[\s*(\d+)\s*\]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex NameDescRegex = new(@"^(\S+)\s{2,}(.+)$", RegexOptions.Compiled);

    private readonly IBin2ShellRunner _runner;
    private readonly IAppPaths _paths;

    public ShellcodeEncodingCatalogService(IBin2ShellRunner runner, IAppPaths paths)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<ShellcodeEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var args = new[] { "-y", _paths.Bin2ShellAlgos, "-h" };
        string helpOutput = await _runner.RunAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);

        var encoders = new List<ShellcodeEncodingItem>();
        var compressors = new List<ShellcodeEncodingItem>();
        var envelopes = new List<ShellcodeEncodingItem>();
        var antiEmulation = new List<AntiEmulationOption>();
        var webHelpers = new List<ShellcodeEncodingItem>();

        ParseHelpOutput(helpOutput, encoders, compressors, envelopes, antiEmulation, webHelpers);

        return new ShellcodeEncodingCatalog(encoders, compressors, envelopes, antiEmulation, webHelpers);
    }

    private static void ParseHelpOutput(
        string help,
        ICollection<ShellcodeEncodingItem> encoders,
        ICollection<ShellcodeEncodingItem> compressors,
        ICollection<ShellcodeEncodingItem> envelopes,
        ICollection<AntiEmulationOption> antiEmulation,
        ICollection<ShellcodeEncodingItem> webHelpers)
    {
        if (string.IsNullOrWhiteSpace(help))
            return;

        var current = CatalogSection.None;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in help.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            current = line switch
            {
                string v when v.StartsWith("available encoders", StringComparison.OrdinalIgnoreCase) => CatalogSection.Encoders,
                string v when v.Equals("Encoders:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Encoders,
                string v when v.StartsWith("available compressors", StringComparison.OrdinalIgnoreCase) => CatalogSection.Compressors,
                string v when v.Equals("Compressors:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Compressors,
                string v when v.StartsWith("available envelopes", StringComparison.OrdinalIgnoreCase) => CatalogSection.Envelopes,
                string v when v.Equals("Envelopes:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Envelopes,
                string v when v.StartsWith("available anti", StringComparison.OrdinalIgnoreCase) => CatalogSection.AntiEmulation,
                string v when v.Equals("Anti-Emulation:", StringComparison.OrdinalIgnoreCase) => CatalogSection.AntiEmulation,
                string v when v.StartsWith("available web", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                string v when v.Equals("Web Helpers:", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                string v when v.StartsWith("Web", StringComparison.OrdinalIgnoreCase) && v.EndsWith(":", StringComparison.Ordinal) && v.Contains("helper", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                _ => current
            };

            if (current == CatalogSection.None)
                continue;

            var match = LineRegex.Match(line);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                continue;

            var payload = match.Groups[2].Value.Trim();
            if (payload.Length == 0)
                continue;

            switch (current)
            {
                case CatalogSection.Encoders:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        encoders.Add(CreateEncodingItem(index, payload));
                    break;
                case CatalogSection.Compressors:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        compressors.Add(CreateEncodingItem(index, payload));
                    break;
                case CatalogSection.Envelopes:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        envelopes.Add(CreateEncodingItem(index, payload));
                    break;
                case CatalogSection.AntiEmulation:
                    var option = CreateAntiEmulationOption(index, payload);
                    if (option != null && seen.Add($"{current}:{index}:{option.Name}"))
                        antiEmulation.Add(option);
                    break;
                case CatalogSection.WebHelpers:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        webHelpers.Add(CreateEncodingItem(index, payload));
                    break;
            }
        }
    }

    /// <summary>
    /// Splits a help-output payload like "winhttp  Windows WinHTTP API (...)" into name + description.
    /// If no two-space separator is found, the entire payload becomes the name.
    /// </summary>
    private static ShellcodeEncodingItem CreateEncodingItem(int index, string payload)
    {
        var m = NameDescRegex.Match(payload);
        if (m.Success)
            return new ShellcodeEncodingItem(index, m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());

        return new ShellcodeEncodingItem(index, payload);
    }

    private static AntiEmulationOption? CreateAntiEmulationOption(int index, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        string name = payload;
        string description = string.Empty;
        string? argsHint = null;

        string working = payload;

        int dashIndex = working.IndexOf('-');
        if (dashIndex >= 0)
        {
            name = working[..dashIndex].Trim();
            working = working[(dashIndex + 1)..].Trim();
        }
        else
        {
            var tokens = working.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                name = tokens[0];
                int namePosition = working.IndexOf(name, StringComparison.Ordinal);
                working = namePosition >= 0 && namePosition + name.Length < working.Length
                    ? working[(namePosition + name.Length)..].Trim()
                    : string.Empty;
            }
            else
            {
                name = working.Trim();
                working = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            return null;

        string remainder = working;
        if (remainder.Length > 0)
        {
            int pipeIndex = remainder.IndexOf('|');
            if (pipeIndex >= 0)
            {
                description = remainder[..pipeIndex].Trim();
                argsHint = remainder[(pipeIndex + 1)..].Trim();
            }
            else
            {
                description = remainder.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(argsHint) && argsHint.StartsWith("Args", StringComparison.OrdinalIgnoreCase))
        {
            argsHint = argsHint.Substring(4).TrimStart(':').Trim();
        }

        return new AntiEmulationOption(
            index,
            name,
            description,
            string.IsNullOrWhiteSpace(argsHint) ? null : argsHint);
    }

    private enum CatalogSection
    {
        None,
        Encoders,
        Compressors,
        Envelopes,
        AntiEmulation,
        WebHelpers
    }
}





