using System.Net.Http.Json;
using Enigma.Client.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/public/lore")]
[AllowAnonymous]
public sealed class PublicLoreController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PublicLoreController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("sector-images")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetSectorImages([FromQuery] int limit = 6)
    {
        limit = Math.Clamp(limit, 1, 12);
        SetNoStoreHeaders();

        try
        {
            using var client = _httpClientFactory.CreateClient("EnigmaBackend");
            var poolLimit = Math.Clamp(limit * 8, 24, 72);
            using var response = await client.GetAsync(
                $"database/leaderboard/leaderboard?sort_by=time_founded&order=desc&limit={poolLimit}&offset=0");

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new LoreSectorImageResponse
                {
                    Status = "error",
                });
            }

            var payload = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
            var images = payload?.Maps?
                .Where(map => !string.IsNullOrWhiteSpace(map.MapImage))
                .Select(map => new LoreSectorImageRecord
                {
                    MapName = map.MapName,
                    MapImage = map.MapImage ?? string.Empty,
                    Theme = map.ThemeLabel,
                    Difficulty = map.Difficulty,
                    TimeFoundedDisplay = map.TimeFoundedDisplay,
                })
                .GroupBy(map => map.MapImage, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList() ?? [];

            if (images.Count == 0)
            {
                return Ok(new LoreSectorImageResponse
                {
                    Status = "empty",
                });
            }

            Shuffle(images);

            return Ok(new LoreSectorImageResponse
            {
                Status = "success",
                Images = images.Take(limit).ToList(),
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new LoreSectorImageResponse
            {
                Status = "error",
            });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new LoreSectorImageResponse
            {
                Status = "error",
            });
        }
    }

    [HttpGet("telemetry")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetTelemetry()
    {
        SetNoStoreHeaders();

        try
        {
            using var client = _httpClientFactory.CreateClient("EnigmaBackend");
            var sectorFeedTask = TryGetAsync<LeaderboardResponse>(
                client,
                "database/leaderboard/leaderboard?sort_by=time_founded&order=desc&limit=24&offset=0");
            var explorerFeedTask = TryGetAsync<PlayerLeaderboardResponse>(
                client,
                "database/leaderboard/players?sort_by=discovered_maps&order=desc&limit=8&offset=0");

            await Task.WhenAll(sectorFeedTask, explorerFeedTask);

            var payload = LoreLiveDataComposer.ComposeTelemetry(
                await sectorFeedTask,
                await explorerFeedTask,
                LoreGovernanceSnapshot.Unavailable(),
                recentSectorLimit: 6,
                topExplorerLimit: 6);

            return payload.Status == "error"
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, payload)
                : Ok(payload);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new LoreTelemetryResponse
            {
                Status = "error",
                Governance = LoreGovernanceSnapshot.Unavailable(),
            });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new LoreTelemetryResponse
            {
                Status = "error",
                Governance = LoreGovernanceSnapshot.Unavailable(),
            });
        }
    }

    [HttpGet("sector-atlas")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetSectorAtlas([FromQuery] int limit = 24)
    {
        limit = Math.Clamp(limit, 6, 60);
        SetNoStoreHeaders();

        try
        {
            using var client = _httpClientFactory.CreateClient("EnigmaBackend");
            var sectorFeed = await TryGetAsync<LeaderboardResponse>(
                client,
                $"database/leaderboard/leaderboard?sort_by=time_founded&order=desc&limit={limit}&offset=0");
            var payload = LoreLiveDataComposer.ComposeSectorAtlas(sectorFeed, limit);

            return payload.Status == "error"
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, payload)
                : Ok(payload);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new LoreSectorAtlasResponse
            {
                Status = "error",
            });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new LoreSectorAtlasResponse
            {
                Status = "error",
            });
        }
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    private static async Task<T?> TryGetAsync<T>(HttpClient client, string requestPath)
        where T : class
    {
        using var response = await client.GetAsync(requestPath);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
