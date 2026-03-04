using Enigma;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net.WebSockets;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var backendBaseUrl =
    builder.Configuration["Backend:BaseUrl"]
    ?? Environment.GetEnvironmentVariable("ENIGMA_BACKEND_URL")
    ?? "https://nonelastic-prorailroad-gillian.ngrok-free.dev/";
var enableWasmDebugging =
    builder.Environment.IsDevelopment() &&
    string.Equals(
        builder.Configuration["EnableWasmDebugging"] ?? Environment.GetEnvironmentVariable("ENABLE_WASM_DEBUGGING"),
        "true",
        StringComparison.OrdinalIgnoreCase);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
});
builder.Services.AddScoped<EnigmaApiClient>();
builder.Services.AddControllers();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Enigma.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (enableWasmDebugging)
{
    app.UseWebAssemblyDebugging();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseRouting();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Enigma.Client._Imports).Assembly);
app.MapControllers();

app.MapGet("/api/auth/multiplayer/session/ws/{sessionId}", async (HttpContext context, string sessionId) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var username = context.User.FindFirstValue(ClaimTypes.Name);
    if (string.IsNullOrWhiteSpace(username))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var browserSocket = await context.WebSockets.AcceptWebSocketAsync();
    using var backendSocket = new ClientWebSocket();
    backendSocket.Options.SetRequestHeader("ngrok-skip-browser-warning", "true");

    try
    {
        var backendSocketUri = BuildBackendSocketUri(
            backendBaseUrl,
            $"database/multiplayer/session/ws/{Uri.EscapeDataString(sessionId)}?username={Uri.EscapeDataString(username)}");
        await backendSocket.ConnectAsync(backendSocketUri, context.RequestAborted);
    }
    catch
    {
        if (browserSocket.State == WebSocketState.Open)
        {
            await browserSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Unable to connect the co-op relay.", CancellationToken.None);
        }

        return;
    }

    using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    var browserToBackend = RelayWebSocketAsync(browserSocket, backendSocket, relayCancellation.Token);
    var backendToBrowser = RelayWebSocketAsync(backendSocket, browserSocket, relayCancellation.Token);

    await Task.WhenAny(browserToBackend, backendToBrowser);
    relayCancellation.Cancel();

    try
    {
        await Task.WhenAll(browserToBackend, backendToBrowser);
    }
    catch
    {
    }
});

app.Run();

static Uri BuildBackendSocketUri(string baseUrl, string relativePath)
{
    var absolute = new Uri(new Uri(baseUrl), relativePath);
    var builder = new UriBuilder(absolute)
    {
        Scheme = absolute.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
    };
    return builder.Uri;
}

static async Task RelayWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
{
    var buffer = new byte[8192];

    while (!cancellationToken.IsCancellationRequested &&
           source.State is WebSocketState.Open or WebSocketState.CloseReceived &&
           destination.State is WebSocketState.Open or WebSocketState.CloseReceived)
    {
        WebSocketReceiveResult result;
        try
        {
            result = await source.ReceiveAsync(buffer, cancellationToken);
        }
        catch
        {
            break;
        }

        if (result.MessageType == WebSocketMessageType.Close)
        {
            if (destination.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await destination.CloseAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        CancellationToken.None);
                }
                catch
                {
                }
            }

            break;
        }

        if (destination.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            break;
        }

        await destination.SendAsync(
            buffer.AsMemory(0, result.Count),
            result.MessageType,
            result.EndOfMessage,
            cancellationToken);
    }
}
