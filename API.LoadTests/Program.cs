using global::API.Services.Abstractions;
using global::API.Services.Call;
using global::API.Services.Features.Call;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Messenger.LoadTests.VoiceCalls;

var options = VoiceCallLoadTestOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(VoiceCallLoadTestOptions.HelpText);
    return 0;
}

options = options.ResolveRelayPort(FindAvailableUdpPort);

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(builder =>
    {
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CallSettings:RelayHost"] = options.RelayHost,
            ["CallSettings:RelayPort"] = options.RelayPort.ToString()
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
        logging.SetMinimumLevel(options.Verbose ? LogLevel.Information : LogLevel.Warning);
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<CallMixerService>();
        services.AddSingleton<CallRelayService>();
        services.AddHostedService(sp => sp.GetRequiredService<CallRelayService>());
        services.AddSingleton<ICallSessionService, CallSessionService>();
        services.AddSingleton(options);
        services.AddSingleton<VoiceCallLoadTestRunner>();
    })
    .Build();

await host.StartAsync();
try
{
    var runner = host.Services.GetRequiredService<VoiceCallLoadTestRunner>();
    var result = await runner.RunAsync(CancellationToken.None);
    result.PrintTo(Console.Out);

    return result.Passed ? 0 : 2;
}
finally
{
    await host.StopAsync();
}

static int FindAvailableUdpPort()
{
    using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
    return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
}
