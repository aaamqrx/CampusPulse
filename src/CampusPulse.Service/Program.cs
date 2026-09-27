using CampusPulse.Core;
using CampusPulse.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var store = new SecureStore();
store.Initialize();
if (args is ["--initialize-store"])
    return;
if (args.Length != 0)
    throw new ArgumentException("Unsupported service argument.");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = ProductInfo.ServiceName);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton<StartupManager>();
builder.Services.AddSingleton<CampusNetworkClient>();
builder.Services.AddSingleton<ConnectionWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ConnectionWorker>());
builder.Services.AddHostedService<ControlPipeServer>();
await builder.Build().RunAsync();
