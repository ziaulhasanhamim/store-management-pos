using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using POSApp.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<HttpReceivingClient>();
builder.Services.AddScoped<IReceivingClient>(provider => provider.GetRequiredService<HttpReceivingClient>());
builder.Services.AddScoped<ISalesClient, HttpSalesClient>();
builder.Services.AddScoped<ImportActivity>();
builder.Services.AddScoped<IImportClient, HttpImportClient>();
builder.Services.AddScoped<IDraftClient, HttpDraftClient>();
builder.Services.AddScoped<IMoneyEntryClient, HttpMoneyEntryClient>();
builder.Services.AddScoped<IStockTakeClient, HttpStockTakeClient>();
builder.Services.AddScoped<IStockAdjustmentClient, HttpStockAdjustmentClient>();
builder.Services.AddScoped<IAttributeDefinitionClient, HttpAttributeDefinitionClient>();

await builder.Build().RunAsync();
