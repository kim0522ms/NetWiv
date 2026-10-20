using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NetWiv.BlazorHybrid.Shared;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddSampleUi();
await builder.Build().RunAsync();
