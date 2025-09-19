using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Washmachine.Services;

public interface IBin2ShellRunner
{
    Task<string> RunAsync(IEnumerable<string> arguments, string? pythonExecutable = null, CancellationToken cancellationToken = default);
}
