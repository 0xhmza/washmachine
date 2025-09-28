using System.Threading;
using System.Threading.Tasks;
using Washmachine.Models;
using Washmachine.Views;

namespace Washmachine.Services;

public interface IMsvcToolchainLocator
{
    Task<MsvcToolchain?> EnsureToolchainAsync(IMainFormView view, CancellationToken cancellationToken = default);
}
