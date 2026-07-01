using Core.Services.Call.Abstractions;
using Core.Services.Realtime.Abstractions;
using System.Diagnostics;

namespace Core.Features.Shell.MainMenu.ViewModels.Hubs;

public class HubConnectionManager : IAsyncDisposable
{
    private readonly IGlobalHubConnection _globalHub;
    private readonly ICallHubConnection _callHub;

    public HubConnectionManager(IGlobalHubConnection globalHub, ICallHubConnection callHub)
    {
        _globalHub = globalHub;
        _callHub = callHub;
    }

    public async Task ReconnectAllAsync()
    {
        await ReconnectAsync("GlobalHub", () => _globalHub.DisconnectAsync(), () => _globalHub.ConnectAsync());
        await ReconnectAsync("CallHub", () => _callHub.DisconnectAsync(), () => _callHub.ConnectAsync());
    }

    private static async Task ReconnectAsync(string name, Func<Task> disconnect, Func<Task> connect)
    {
        try
        {
            await disconnect();
            await connect();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HubConnectionManager] {name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await SafeDisposeAsync("GlobalHub", _globalHub);
        await SafeDisposeAsync("CallHub", _callHub);
    }

    private static async Task SafeDisposeAsync(string name, IAsyncDisposable d)
    {
        try { await d.DisposeAsync(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HubConnectionManager] {name} dispose: {ex.Message}");
        }
    }
}