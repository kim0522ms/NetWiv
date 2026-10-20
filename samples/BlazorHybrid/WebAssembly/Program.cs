using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NetWiv.BlazorHybrid.Shared;
using Microsoft.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddSampleUi();
builder.Services.AddScoped<IUiInteropReferenceFactory, BrowserUiInteropReferenceFactory>();
await builder.Build().RunAsync();
