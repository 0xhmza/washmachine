using System.Globalization;
using System.Text.RegularExpressions;

namespace ShieldGuard.Services;

public interface ILicenseStubEncodingCatalog
{
    Task<LicenseStubEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed record LicenseStubEncodingCatalog(
    IReadOnlyList<LicenseStubEncodingItem> Encoders,
    IReadOnlyList<LicenseStubEncodingItem> Compressors,
    IReadOnlyList<LicenseStubEncodingItem> Envelopes,
    IReadOnlyList<AntiEmulationOption> AntiEmulation,
    IReadOnlyList<LicenseStubEncodingItem> WebHelpers);

public sealed record LicenseStubEncodingItem(int Index, string Name, string Description = "")
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
public sealed class LicenseStubEncodingCatalogService : ILicenseStubEncodingCatalog
{
    private static readonly Regex LineRegex = new(@"\[\s*(\d+)\s*\]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex NameDescRegex = new(@"^(\S+)\s{2,}(.+)$", RegexOptions.Compiled);

    private readonly IBin2LicenseRunner _runner;
    private readonly IAppPaths _paths;

    public LicenseStubEncodingCatalogService(IBin2LicenseRunner runner, IAppPaths paths)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<LicenseStubEncodingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var args = new[] { "-y", _paths.Bin2LicenseAlgos, "-h" };
        string helpOutput = await _runner.RunAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);

        var encoders = new List<LicenseStubEncodingItem>();
        var compressors = new List<LicenseStubEncodingItem>();
        var envelopes = new List<LicenseStubEncodingItem>();
        var antiEmulation = new List<AntiEmulationOption>();
        var webHelpers = new List<LicenseStubEncodingItem>();

        ParseHelpOutput(helpOutput, encoders, compressors, envelopes, antiEmulation, webHelpers);

        return new LicenseStubEncodingCatalog(encoders, compressors, envelopes, antiEmulation, webHelpers);
    }

    private static void ParseHelpOutput(
        string help,
        ICollection<LicenseStubEncodingItem> encoders,
        ICollection<LicenseStubEncodingItem> compressors,
        ICollection<LicenseStubEncodingItem> envelopes,
        ICollection<AntiEmulationOption> antiEmulation,
        ICollection<LicenseStubEncodingItem> webHelpers)
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
                string v when v.Equals("Emulation Protection:", StringComparison.OrdinalIgnoreCase) => CatalogSection.AntiEmulation,
                string v when v.StartsWith("available web", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                string v when v.Equals("Web Helpers:", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                string v when v.StartsWith("Web", StringComparison.OrdinalIgnoreCase) && v.EndsWith(":", StringComparison.Ordinal) && v.Contains("helper", StringComparison.OrdinalIgnoreCase) => CatalogSection.WebHelpers,
                // bin2license emits "available carriers (--carrier):" between
                // Web Helpers and the defaults footer. Without this case the
                // parser stays in WebHelpers and `ini`/`png`/`bmp`/`ico` leak
                // into the Web Helper dropdown.
                string v when v.StartsWith("available carriers", StringComparison.OrdinalIgnoreCase) => CatalogSection.Carriers,
                string v when v.Equals("Carriers:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Carriers,
                _ => current
            };

            if (current == CatalogSection.None)
                continue;

            var match = LineRegex.Match(line);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                continue;

            var licensepayload = match.Groups[2].Value.Trim();
            if (licensepayload.Length == 0)
                continue;

            switch (current)
            {
                case CatalogSection.Encoders:
                    if (seen.Add($"{current}:{index}:{licensepayload}"))
                        encoders.Add(CreateEncodingItem(index, licensepayload));
                    break;
                case CatalogSection.Compressors:
                    if (seen.Add($"{current}:{index}:{licensepayload}"))
                        compressors.Add(CreateEncodingItem(index, licensepayload));
                    break;
                case CatalogSection.Envelopes:
                    if (seen.Add($"{current}:{index}:{licensepayload}"))
                        envelopes.Add(CreateEncodingItem(index, licensepayload));
                    break;
                case CatalogSection.AntiEmulation:
                    var option = CreateAntiEmulationOption(index, licensepayload);
                    if (option != null && seen.Add($"{current}:{index}:{option.Name}"))
                        antiEmulation.Add(option);
                    break;
                case CatalogSection.WebHelpers:
                    if (seen.Add($"{current}:{index}:{licensepayload}"))
                        webHelpers.Add(CreateEncodingItem(index, licensepayload));
                    break;
            }
        }
    }

    /// <summary>
    /// Splits a help-output licensepayload like "winhttp  Windows WinHTTP API (...)" into name + description.
    /// If no two-space separator is found, the entire licensepayload becomes the name.
    /// </summary>
    private static LicenseStubEncodingItem CreateEncodingItem(int index, string licensepayload)
    {
        var m = NameDescRegex.Match(licensepayload);
        if (m.Success)
            return new LicenseStubEncodingItem(index, m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());

        return new LicenseStubEncodingItem(index, licensepayload);
    }

    private static AntiEmulationOption? CreateAntiEmulationOption(int index, string licensepayload)
    {
        if (string.IsNullOrWhiteSpace(licensepayload))
            return null;

        string name = licensepayload;
        string description = string.Empty;
        string? argsHint = null;

        string working = licensepayload;

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
        WebHelpers,
        // Carriers are intentionally parsed but discarded: they belong to a
        // separate bin2license feature (--carrier) that the GUI does not expose
        // here. We still need to recognise the section header so subsequent
        // carrier lines don't leak into the previous bucket (e.g. WebHelpers
        // ending up showing png/ico/bmp/ini).
        Carriers,
    }
}





