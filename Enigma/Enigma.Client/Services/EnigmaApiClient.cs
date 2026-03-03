using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Enigma.Client.Models;
using Microsoft.AspNetCore.Components;

namespace Enigma.Client.Services;

public sealed class EnigmaApiClient
{
    private readonly HttpClient _http;
    private readonly NavigationManager _navigationManager;

    public EnigmaApiClient(HttpClient http, NavigationManager navigationManager)
    {
        _http = http;
        _navigationManager = navigationManager;
    }

    public string BuildUrl(string relativePath)
    {
        return new Uri(new Uri(_navigationManager.BaseUri), relativePath).ToString();
    }

    public async Task<LoginUserSummary?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(BuildUrl("api/auth/session/me"), cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await ReadJsonAsync<LoginResponse>(response, cancellationToken);
        return payload?.User;
    }

    public async Task<ApiStatusResponse?> LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync(BuildUrl("api/auth/session/logout"), null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ApiStatusResponse { Status = "success" };
        }

        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<ApiStatusResponse>(response, cancellationToken);
    }

    public Task<HttpResponseMessage> PostJsonAsync<T>(string relativePath, T payload, CancellationToken cancellationToken = default)
    {
        return _http.PostAsJsonAsync(BuildUrl(relativePath), payload, cancellationToken);
    }

    public Task<HttpResponseMessage> PutJsonAsync<T>(string relativePath, T payload, CancellationToken cancellationToken = default)
    {
        return _http.PutAsJsonAsync(BuildUrl(relativePath), payload, cancellationToken);
    }

    public async Task<HttpResponseMessage> DeleteJsonAsync<T>(string relativePath, T payload, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(relativePath))
        {
            Content = JsonContent.Create(payload),
        };

        return await _http.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> GetAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return _http.GetAsync(BuildUrl(relativePath), cancellationToken);
    }

    public async Task<T?> GetFromJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(BuildUrl(relativePath), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    public async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.Content is null)
        {
            return default;
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
