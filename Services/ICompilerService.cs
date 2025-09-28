using System.Threading;
using System.Threading.Tasks;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICompilerService
{
    Task<CompilerResult> CompileAsync(UiData data, MsvcToolchain toolchain, CancellationToken cancellationToken = default);
}

