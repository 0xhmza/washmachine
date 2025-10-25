using System.Threading;
using System.Threading.Tasks;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICompilerToolLocator
{
    Task<CompilerToolDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<CompilerToolDiscoveryResult> AddManualCandidateAsync(string path, CancellationToken cancellationToken = default);
}
