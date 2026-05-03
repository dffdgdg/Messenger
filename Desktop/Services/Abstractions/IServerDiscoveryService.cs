using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Services.Abstractions;

public interface IServerDiscoveryService
{
    Task<string?> DiscoverAsync(int timeoutMs = 3000, CancellationToken ct = default);
}