using Enigma;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.IO;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.Local.json", optional: true, reloadOnChange: true);

var renderPort = Environment.GetEnvironmentVariable("PORT");
var isManagedProxyHost =
    !string.IsNullOrWhiteSpace(renderPort) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL"));

if (int.TryParse(renderPort, out var parsedPort) && parsedPort > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

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
builder.Services.AddHttpClient("EnigmaBackend", client =>
{
    client.BaseAddress = new Uri(backendBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    MaxConnectionsPerServer = 1024,
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
});
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
});
builder.Services.AddScoped<EnigmaApiClient>();
builder.Services.AddScoped<UiNotificationService>();
builder.Services.AddScoped<DeviceCompatibilityService>();
builder.Services.AddMemoryCache();
builder.Services.Configure<EmailVerificationOptions>(builder.Configuration.GetSection(EmailVerificationOptions.SectionName));
builder.Services.AddSingleton<IEmailVerificationSender, GmailApiEmailVerificationSender>();
builder.Services.AddSingleton<PendingSignUpVerificationService>();
builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("Enigma");

var configuredDataProtectionKeysPath =
    builder.Configuration["DataProtection:KeysPath"]
    ?? Environment.GetEnvironmentVariable("ENIGMA_DATA_PROTECTION_KEYS_PATH");
var dataProtectionKeysPath = ResolveDataProtectionKeysPath(configuredDataProtectionKeysPath, isManagedProxyHost);
string? dataProtectionStartupWarning = null;

if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    try
    {
        Directory.CreateDirectory(dataProtectionKeysPath);
        dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

        if (isManagedProxyHost && string.IsNullOrWhiteSpace(configuredDataProtectionKeysPath))
        {
            dataProtectionStartupWarning =
                $"Using default data-protection key path '{dataProtectionKeysPath}'. Attach your Render persistent disk there, or set DataProtection:KeysPath / ENIGMA_DATA_PROTECTION_KEYS_PATH to a mounted directory, so auth cookies and antiforgery tokens survive restarts.";
        }
    }
    catch (Exception exception)
    {
        dataProtectionStartupWarning =
            $"Failed to persist data-protection keys to '{dataProtectionKeysPath}'. Falling back to ephemeral container storage, which will break auth cookies and antiforgery tokens after restarts. {exception.Message}";
    }
}
else if (isManagedProxyHost)
{
    dataProtectionStartupWarning =
        "No data-protection key path is configured. Set DataProtection:KeysPath or ENIGMA_DATA_PROTECTION_KEYS_PATH to a persistent mounted directory so auth cookies and antiforgery tokens survive restarts.";
}

builder.Services.AddAntiforgery(options =>
{
    // Version the antiforgery cookie so browsers stop sending tokens that were
    // encrypted with a pre-fix key ring from earlier deployments.
    options.Cookie.Name = "Enigma.Antiforgery.v2";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

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

if (!string.IsNullOrWhiteSpace(dataProtectionStartupWarning))
{
    app.Logger.LogWarning("{Message}", dataProtectionStartupWarning);
}

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
app.UseForwardedHeaders();
app.UseStatusCodePagesWithReExecute("/not-found");
if (!isManagedProxyHost)
{
    app.UseHttpsRedirection();
}
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

app.MapGet("/robots.txt", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
    var robots = $"""
User-agent: *
Allow: /
Disallow: /api/

Sitemap: {baseUrl}/sitemap.xml
""";
    return Results.Text(robots, "text/plain");
});

app.MapGet("/sitemap.xml", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
    var document = BuildSitemapDocument(baseUrl);
    return Results.Text(document.ToString(SaveOptions.DisableFormatting), "application/xml");
});

app.MapGet("/llms.txt", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
    var content = $"""
# Enigma

Enigma is a puzzle maze game with solo runs, co-op expeditions, collectible maps, and a player-driven marketplace.

Public pages:
- {baseUrl}/
- {baseUrl}/lore
- {baseUrl}/about
- {baseUrl}/how-enigma-works
- {baseUrl}/enigma-game-mechanics
- {baseUrl}/enigma-multiplayer
""";
    return Results.Text(content, "text/plain");
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

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

static string? ResolveDataProtectionKeysPath(string? configuredPath, bool isManagedProxyHost)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return configuredPath;
    }

    if (isManagedProxyHost)
    {
        // Render services use an ephemeral filesystem by default. Prefer a stable
        // mount point so a persistent disk at /var/data works without extra config.
        return "/var/data/enigma-dp-keys";
    }

    return null;
}

static XDocument BuildSitemapDocument(string baseUrl)
{
    var sitemap = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
    var publicPaths = new[]
    {
        "/",
        "/lore",
        "/about",
        "/how-enigma-works",
        "/enigma-game-mechanics",
        "/enigma-multiplayer",
    };

    return new XDocument(
        new XElement(sitemap + "urlset",
            publicPaths.Select(path =>
                new XElement(sitemap + "url",
                    new XElement(sitemap + "loc", $"{baseUrl}{path}"),
                    new XElement(sitemap + "changefreq", path == "/" ? "weekly" : "monthly"),
                    new XElement(sitemap + "priority", path == "/" ? "1.0" : "0.8")))));
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
