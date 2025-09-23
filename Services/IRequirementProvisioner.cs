using System.Threading;
using System.Threading.Tasks;
using Washmachine.Views;

namespace Washmachine.Services;

public interface IRequirementProvisioner
{
    Task EnsureRequirementsAsync(IMainFormView view, CancellationToken cancellationToken = default);
}
