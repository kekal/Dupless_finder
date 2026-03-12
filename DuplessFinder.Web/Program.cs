using DuplessFinder.Web;
using DuplessFinder.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IFileAccessService, FileAccessService>();
builder.Services.AddSingleton<IOpenCvService, OpenCvService>();
builder.Services.AddSingleton<ICalcOperations, CalcOperations>();

await builder.Build().RunAsync();
