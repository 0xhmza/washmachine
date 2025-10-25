using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Washmachine.Services;

public sealed class ShellcodeEncodingCatalogService : IShellcodeEncodingCatalog
{
    private static readonly Regex LineRegex = new(@"\[\s*(\d+)\s*\]\s+(.+)$", RegexOptions.Compiled);

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

        ParseHelpOutput(helpOutput, encoders, compressors, envelopes, antiEmulation);

        return new ShellcodeEncodingCatalog(encoders, compressors, envelopes, antiEmulation);
    }

    private static void ParseHelpOutput(
        string help,
        ICollection<ShellcodeEncodingItem> encoders,
        ICollection<ShellcodeEncodingItem> compressors,
        ICollection<ShellcodeEncodingItem> envelopes,
        ICollection<AntiEmulationOption> antiEmulation)
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
                string value when value.Equals("Encoders:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Encoders,
                string value when value.Equals("Compressors:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Compressors,
                string value when value.Equals("Envelopes:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Envelopes,
                string value when value.Equals("Anti-Emulation:", StringComparison.OrdinalIgnoreCase) => CatalogSection.AntiEmulation,
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
                        encoders.Add(new ShellcodeEncodingItem(index, payload));
                    break;
                case CatalogSection.Compressors:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        compressors.Add(new ShellcodeEncodingItem(index, payload));
                    break;
                case CatalogSection.Envelopes:
                    if (seen.Add($"{current}:{index}:{payload}"))
                        envelopes.Add(new ShellcodeEncodingItem(index, payload));
                    break;
                case CatalogSection.AntiEmulation:
                    var option = CreateAntiEmulationOption(index, payload);
                    if (option != null && seen.Add($"{current}:{index}:{option.Name}"))
                        antiEmulation.Add(option);
                    break;
            }
        }
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
        AntiEmulation
    }
}





