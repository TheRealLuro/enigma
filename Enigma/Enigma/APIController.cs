using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class APIController : ControllerBase
{
    private readonly string _backendBaseUrl;

    public APIController(IConfiguration configuration)
    {
        _backendBaseUrl =
            configuration["Backend:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("ENIGMA_BACKEND_URL")
            ?? "https://nonelastic-prorailroad-gillian.ngrok-free.dev/";
    }

    private static string Esc(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_backendBaseUrl) };
        client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<IActionResult> RelayAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return new ContentResult
        {
            Content = content,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            StatusCode = (int)response.StatusCode,
        };
    }

    private static async Task<string> ReadContentAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync();
    }

    private string RequireAuthenticatedUsername()
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        return username;
    }

    private async Task SignInAsync(string username, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(12),
            });
    }

    [HttpPost("session/login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] SessionLoginRequest request)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("database/users/login", new
        {
            username = request.Username,
            passwd = request.Password,
        });

        if (!response.IsSuccessStatusCode)
        {
            return await RelayAsync(response);
        }

        var content = await ReadContentAsync(response);
        using var document = JsonDocument.Parse(content);
        var username = document.RootElement
            .GetProperty("user")
            .GetProperty("username")
            .GetString();

        if (string.IsNullOrWhiteSpace(username))
        {
            return StatusCode(502, new { detail = "Backend login payload was missing the username." });
        }

        await SignInAsync(username, request.RememberMe);

        return Content(content, response.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    [HttpPost("session/signup")]
    [HttpPost("signUp")]
    public async Task<IActionResult> SignUp([FromBody] SessionSignUpRequest request)
    {
        using var client = CreateClient();
        using var signUpResponse = await client.PostAsJsonAsync("database/users/signup", new
        {
            username = request.Username,
            email = request.Email,
            passwd = request.Password,
        });

        if (!signUpResponse.IsSuccessStatusCode)
        {
            return await RelayAsync(signUpResponse);
        }

        using var loginResponse = await client.PostAsJsonAsync("database/users/login", new
        {
            username = request.Username,
            passwd = request.Password,
        });

        if (!loginResponse.IsSuccessStatusCode)
        {
            return await RelayAsync(loginResponse);
        }

        var content = await ReadContentAsync(loginResponse);
        await SignInAsync(request.Username, request.RememberMe);
        return Content(content, loginResponse.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    [Authorize]
    [HttpPost("session/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { status = "success" });
    }

    [Authorize]
    [HttpGet("session/me")]
    public async Task<IActionResult> Me()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/users/account?username={Esc(username)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("newMap")]
    public async Task<IActionResult> GetNewSeed([FromQuery] string difficulty, [FromQuery] int size)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"maze/genseed?difficulty={Esc(difficulty)}&size={size}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("loadMap")]
    public async Task<IActionResult> GetSeedFromName([FromQuery] string name)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/maps/load_map?map_name={Esc(name)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("addMap")]
    public async Task<IActionResult> AddMap([FromBody] SubmitMapRequest request)
    {
        using var client = CreateClient();
        var founder = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/maps/add?map_name={Esc(request.Name)}&seed={Esc(request.Seed)}&size={request.Size}&difficulty={Esc(request.Difficulty)}&founder={Esc(founder)}&time_completed={Esc(request.Time.ToString())}&first_rating={request.Rating}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("updateMap")]
    public async Task<IActionResult> UpdateMap([FromBody] UpdateMapRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsync(
            $"database/maps/update_map?seed={Esc(request.Seed)}&username={Esc(username)}&completion_time={Esc(request.Time.ToString())}&rating={request.Rating}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("updateStats")]
    public async Task<IActionResult> UpdateStats([FromBody] UpdateStatsRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsync(
            $"database/users/update_progress?username={Esc(username)}&map_seed={Esc(request.Seed)}&items_in_use={Esc(request.Items ?? string.Empty)}&earned_mn={request.Reward}&seed_existed={request.SeedExisted}&map_lost={request.MapLost}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("game/abandon")]
    public async Task<IActionResult> AbandonRun([FromBody] AbandonRunRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/users/abandon_run", new
        {
            username,
            run_nonce = request.RunNonce,
            seed = request.Seed,
            used_items = request.UsedItems,
            map_name = request.MapName,
            source = request.Source,
            forfeited_run_payout = request.ForfeitedRunPayout,
            projected_completion_payout = request.ProjectedCompletionPayout,
            map_value = request.MapValue,
            reason = request.Reason,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> GetUser([FromQuery] string username)
    {
        using var client = CreateClient();
        var viewer = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/users/getuser?username={Esc(username)}&viewer={Esc(viewer)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("userSearch")]
    [HttpGet("players/search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string query, [FromQuery] int limit = 6)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/users/search?query={Esc(query)}&limit={limit}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("leaderboard")]
    [HttpGet("leaderboard/maps")]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string sortBy = "rating", [FromQuery] string order = "desc")
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/leaderboard/leaderboard?sort_by={Esc(sortBy)}&order={Esc(order)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("playerLeaderboard")]
    [HttpGet("leaderboard/players")]
    public async Task<IActionResult> GetPlayerLeaderboard([FromQuery] string sortBy = "maze_nuggets", [FromQuery] string order = "desc")
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/leaderboard/players?sort_by={Esc(sortBy)}&order={Esc(order)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("friendRequest")]
    public async Task<IActionResult> SendFriendRequest([FromBody] FriendRequestRequest request)
    {
        using var client = CreateClient();
        var senderUser = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/users/send_fr?sender_user={Esc(senderUser)}&receiver_user={Esc(request.ReceiverUser)}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("acceptFriend")]
    public async Task<IActionResult> AcceptFriendRequest([FromBody] AcceptFriendRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/users/accept_fr?username={Esc(username)}&adding={Esc(request.Adding)}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("friends/remove")]
    public async Task<IActionResult> RemoveFriend([FromBody] RemoveFriendRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/users/remove_friend", new
        {
            username,
            friend_username = request.FriendUsername,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("account/email")]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsJsonAsync("database/users/update_email", new
        {
            username,
            current_password = request.CurrentPassword,
            new_email = request.NewEmail,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("account/password")]
    public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsJsonAsync("database/users/update_password", new
        {
            username,
            current_password = request.CurrentPassword,
            new_password = request.NewPassword,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("account/avatar")]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsJsonAsync("database/users/update_avatar", new
        {
            username,
            map_name = request.MapName,
            crop = new
            {
                x = request.Crop.X,
                y = request.Crop.Y,
                size = request.Crop.Size,
            },
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "database/users/delete_account")
        {
            Content = JsonContent.Create(new
            {
                username,
                current_password = request.CurrentPassword,
                confirm_username = request.ConfirmUsername,
            }),
        };
        using var response = await client.SendAsync(deleteRequest);
        if (response.IsSuccessStatusCode)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("account/tutorial")]
    public async Task<IActionResult> UpdateTutorial([FromBody] TutorialActionRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/users/tutorial_state", new
        {
            username,
            action = request.Action,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("items/shop")]
    public async Task<IActionResult> GetItemShop()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("database/merchant/items");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("items/inventory")]
    public async Task<IActionResult> GetInventory()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/users/inventory?username={Esc(username)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("items/purchase")]
    public async Task<IActionResult> PurchaseItem([FromBody] PurchaseItemRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/merchant/buy_item?username={Esc(username)}&item_id={Esc(request.ItemId)}&quantity={request.Quantity}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("marketplace/add")]
    public async Task<IActionResult> AddMarketplaceListing([FromBody] MarketplaceAddRequest request)
    {
        using var client = CreateClient();
        var user = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/maps/add_to_marketplace?user={Esc(user)}&map_name={Esc(request.MapName)}&price={request.Price}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("marketplace/buy")]
    public async Task<IActionResult> BuyMarketplaceListing([FromBody] MarketplaceBuyRequest request)
    {
        using var client = CreateClient();
        var buyer = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/marketplace/buy?map_name={Esc(request.MapName)}&buyer={Esc(buyer)}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("marketplace")]
    public async Task<IActionResult> GetMarketplaceListings()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("database/marketplace/listings");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("maps/recycle")]
    public async Task<IActionResult> RecycleMap([FromBody] RecycleMapRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsync(
            $"database/maps/recycle?username={Esc(username)}&map_name={Esc(request.MapName)}",
            null);
        return await RelayAsync(response);
    }
}

public sealed class SessionLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public sealed class SessionSignUpRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public sealed class SubmitMapRequest
{
    public string Name { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public int Size { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public TimeOnly Time { get; set; }
    public int Rating { get; set; }
}

public sealed class UpdateMapRequest
{
    public string Seed { get; set; } = string.Empty;
    public TimeOnly Time { get; set; }
    public int? Rating { get; set; }
}

public sealed class UpdateStatsRequest
{
    public string Seed { get; set; } = string.Empty;
    public string? Items { get; set; }
    public int Reward { get; set; }
    public bool SeedExisted { get; set; }
    public bool MapLost { get; set; }
}

public sealed class FriendRequestRequest
{
    public string ReceiverUser { get; set; } = string.Empty;
}

public sealed class AcceptFriendRequest
{
    public string Adding { get; set; } = string.Empty;
}

public sealed class RemoveFriendRequest
{
    public string FriendUsername { get; set; } = string.Empty;
}

public sealed class UpdateEmailRequest
{
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class UpdatePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class AvatarCropRequest
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Size { get; set; } = 100;
}

public sealed class UpdateAvatarRequest
{
    public string MapName { get; set; } = string.Empty;
    public AvatarCropRequest Crop { get; set; } = new();
}

public sealed class DeleteAccountRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string ConfirmUsername { get; set; } = string.Empty;
}

public sealed class TutorialActionRequest
{
    public string Action { get; set; } = string.Empty;
}

public sealed class PurchaseItemRequest
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public sealed class MarketplaceAddRequest
{
    public string MapName { get; set; } = string.Empty;
    public int Price { get; set; }
}

public sealed class MarketplaceBuyRequest
{
    public string MapName { get; set; } = string.Empty;
}

public sealed class RecycleMapRequest
{
    public string MapName { get; set; } = string.Empty;
}

public sealed class AbandonRunRequest
{
    public string RunNonce { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string Source { get; set; } = "new";
    public List<string> UsedItems { get; set; } = [];
    public int ForfeitedRunPayout { get; set; }
    public int ProjectedCompletionPayout { get; set; }
    public int MapValue { get; set; }
    public string Reason { get; set; } = "abandoned";
}
