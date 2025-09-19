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

        ParseHelpOutput(helpOutput, encoders, compressors, envelopes);

        return new ShellcodeEncodingCatalog(encoders, compressors, envelopes);
    }

    private static void ParseHelpOutput(
        string help,
        ICollection<ShellcodeEncodingItem> encoders,
        ICollection<ShellcodeEncodingItem> compressors,
        ICollection<ShellcodeEncodingItem> envelopes)
    {
        if (string.IsNullOrWhiteSpace(help))
            return;

        var current = CatalogSection.None;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in help.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            current = line switch
            {
                string value when value.Equals("Encoders:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Encoders,
                string value when value.Equals("Compressors:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Compressors,
                string value when value.Equals("Envelopes:", StringComparison.OrdinalIgnoreCase) => CatalogSection.Envelopes,
                _ => current
            };

            if (current == CatalogSection.None)
                continue;

            var match = LineRegex.Match(line);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                continue;

            var name = match.Groups[2].Value.Trim();
            if (name.Length == 0)
                continue;

            string uniqueKey = $"{current}:{index}:{name}";
            if (!seen.Add(uniqueKey))
                continue;

            var item = new ShellcodeEncodingItem(index, name);

            switch (current)
            {
                case CatalogSection.Encoders:
                    encoders.Add(item);
                    break;
                case CatalogSection.Compressors:
                    compressors.Add(item);
                    break;
                case CatalogSection.Envelopes:
                    envelopes.Add(item);
                    break;
            }
        }
    }

    private enum CatalogSection
    {
        None,
        Encoders,
        Compressors,
        Envelopes
    }
}
