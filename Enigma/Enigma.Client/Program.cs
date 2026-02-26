using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? "https://nonelastic-prorailroad-gillian.ngrok-free.dev/";
var apiToken = builder.Configuration["Api:Token"] ?? string.Empty;

builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
    client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");

    // Backend validates token via query param; this keeps the token available client-side for URL building.
    // Also set bearer for compatibility if backend middleware later uses Authorization.
    if (!string.IsNullOrWhiteSpace(apiToken))
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
    }

    return client;
});

await builder.Build().RunAsync();
