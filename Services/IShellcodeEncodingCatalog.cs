using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
