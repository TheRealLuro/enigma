using System.Net;
using System.Net.Http.Json;
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

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
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
        return await response.Content.ReadFromJsonAsync<ApiStatusResponse>(cancellationToken: cancellationToken);
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

    public Task<T?> GetFromJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        return _http.GetFromJsonAsync<T>(BuildUrl(relativePath), cancellationToken);
    }
}
