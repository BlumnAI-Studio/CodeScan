using HelloOrleans.Grains;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;

var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans(silo => silo.UseLocalhostClustering());

using var host = builder.Build();
await host.StartAsync();

var grains = host.Services.GetRequiredService<IGrainFactory>();
var world = grains.GetGrain<IWorldGrain>("world");
await world.HelloAll();

await Task.Delay(500);
await host.StopAsync();
