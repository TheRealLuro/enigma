using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Globalization;
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
    private static string FormatBackendTime(TimeOnly value) => value.ToString("HH:mm:ss:fff", CultureInfo.InvariantCulture);

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_backendBaseUrl) };
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private IActionResult BuildBackendFailureResponse(Exception exception)
    {
        if (exception is UriFormatException)
        {
            return StatusCode(500, new
            {
                status = "error",
                detail = "Backend base URL is invalid. Set ENIGMA_BACKEND_URL or Backend:BaseUrl."
            });
        }

        if (exception is TaskCanceledException)
        {
            return StatusCode(504, new
            {
                status = "error",
                detail = "Backend request timed out."
            });
        }

        return StatusCode(503, new
        {
            status = "error",
            detail = "Backend service is unavailable."
        });
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
        try
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return BuildBackendFailureResponse(ex);
        }
    }

    [HttpPost("session/signup")]
    [HttpPost("signUp")]
    public async Task<IActionResult> SignUp([FromBody] SessionSignUpRequest request)
    {
        try
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return BuildBackendFailureResponse(ex);
        }
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
        try
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return BuildBackendFailureResponse(ex);
        }
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
            $"database/maps/add?map_name={Esc(request.Name)}&seed={Esc(request.Seed)}&size={request.Size}&difficulty={Esc(request.Difficulty)}&founder={Esc(founder)}&time_completed={Esc(FormatBackendTime(request.Time))}&first_rating={request.Rating}",
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
            $"database/maps/update_map?seed={Esc(request.Seed)}&username={Esc(username)}&completion_time={Esc(FormatBackendTime(request.Time))}&rating={request.Rating}",
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
    [HttpGet("maps/search")]
    public async Task<IActionResult> SearchMaps([FromQuery] string query, [FromQuery] int limit = 6)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/maps/search?query={Esc(query)}&limit={limit}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("leaderboard")]
    [HttpGet("leaderboard/maps")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string sortBy = "rating",
        [FromQuery] string order = "desc",
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(
            $"database/leaderboard/leaderboard?sort_by={Esc(sortBy)}&order={Esc(order)}&limit={limit}&offset={offset}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("playerLeaderboard")]
    [HttpGet("leaderboard/players")]
    public async Task<IActionResult> GetPlayerLeaderboard(
        [FromQuery] string sortBy = "maze_nuggets",
        [FromQuery] string order = "desc",
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(
            $"database/leaderboard/players?sort_by={Esc(sortBy)}&order={Esc(order)}&limit={limit}&offset={offset}");
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
    [HttpPut("account/username")]
    public async Task<IActionResult> UpdateUsername([FromBody] UpdateUsernameRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsJsonAsync("database/users/update_username", new
        {
            username,
            current_password = request.CurrentPassword,
            new_username = request.NewUsername,
        });

        var content = await ReadContentAsync(response);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        if (!response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                Content = content,
                ContentType = contentType,
                StatusCode = (int)response.StatusCode,
            };
        }

        try
        {
            using var payload = JsonDocument.Parse(content);
            var updatedUsername = payload.RootElement
                .GetProperty("user")
                .GetProperty("username")
                .GetString();

            if (!string.IsNullOrWhiteSpace(updatedUsername))
            {
                var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var rememberMe = authResult.Succeeded && authResult.Properties?.IsPersistent == true;
                await SignInAsync(updatedUsername, rememberMe);
            }
        }
        catch (JsonException)
        {
        }

        return new ContentResult
        {
            Content = content,
            ContentType = contentType,
            StatusCode = (int)response.StatusCode,
        };
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
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/merchant/items?username={Esc(username)}");
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
    [HttpGet("economy/overview")]
    public async Task<IActionResult> GetEconomyOverview()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/economy/overview?username={Esc(username)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("staking")]
    public async Task<IActionResult> GetStakingOverview()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/staking/overview?username={Esc(username)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("staking/stake")]
    public async Task<IActionResult> StakeMap([FromBody] StakingMapRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/staking/stake", new
        {
            username,
            map_id = request.MapId,
            map_name = request.MapName,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("staking/unstake")]
    public async Task<IActionResult> UnstakeMap([FromBody] StakingMapRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/staking/unstake", new
        {
            username,
            map_id = request.MapId,
            map_name = request.MapName,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("staking/claim")]
    public async Task<IActionResult> ClaimStakingReward()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/staking/claim", new
        {
            username,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("voting/session")]
    public async Task<IActionResult> GetVotingSession()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/governance/session?username={Esc(username)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("voting/session/start")]
    public async Task<IActionResult> StartVotingSession([FromBody] StartVotingSessionRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/governance/session/start", new
        {
            username,
            title = request.Title,
            description = request.Description,
            vote_type = request.VoteType,
            options = request.Options,
            duration_value = request.DurationValue,
            duration_unit = request.DurationUnit,
            vote_cost_mn = request.VoteCostMn,
            number_min = request.NumberMin,
            number_max = request.NumberMax,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("voting/session/close")]
    public async Task<IActionResult> CloseVotingSession()
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/governance/session/close", new
        {
            username,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("voting/cast")]
    public async Task<IActionResult> CastVote([FromBody] CastVoteRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/governance/vote", new
        {
            username,
            option_id = request.OptionId,
            option_ids = request.OptionIds,
            text_entry = request.TextEntry,
            number_entry = request.NumberEntry,
            vote_quantity = request.VoteQuantity,
        });
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
    [HttpPut("marketplace/listing/price")]
    public async Task<IActionResult> UpdateMarketplaceListingPrice([FromBody] MarketplaceUpdateListingPriceRequest request)
    {
        using var client = CreateClient();
        var seller = RequireAuthenticatedUsername();
        using var response = await client.PutAsync(
            $"database/marketplace/listing/price?map_name={Esc(request.MapName)}&seller={Esc(seller)}&price={request.Price}",
            null);
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpDelete("marketplace/listing")]
    public async Task<IActionResult> RemoveMarketplaceListing([FromBody] MarketplaceRemoveListingRequest request)
    {
        using var client = CreateClient();
        var seller = RequireAuthenticatedUsername();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"database/marketplace/listing?map_name={Esc(request.MapName)}&seller={Esc(seller)}");
        using var response = await client.SendAsync(deleteRequest);
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

    [Authorize]
    [HttpGet("multiplayer/puzzles")]
    public async Task<IActionResult> GetMultiplayerPuzzleCatalog()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("database/multiplayer/puzzle_catalog");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/create")]
    public async Task<IActionResult> CreateMultiplayerSession([FromBody] CreateMultiplayerSessionRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/create", new
        {
            username,
            seed = request.Seed,
            map_name = request.MapName,
            source = request.Source,
            invited_friends = request.InvitedFriends,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/invite")]
    public async Task<IActionResult> InviteToMultiplayerSession([FromBody] MultiplayerInviteRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/invite", new
        {
            username,
            session_id = request.SessionId,
            friend_username = request.FriendUsername,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/join")]
    public async Task<IActionResult> JoinMultiplayerSession([FromBody] MultiplayerSessionRequest request)
    {
        try
        {
            using var client = CreateClient();
            var username = RequireAuthenticatedUsername();
            using var response = await client.PostAsJsonAsync("database/multiplayer/session/join", new
            {
                username,
                session_id = request.SessionId,
            });
            return await RelayAsync(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return BuildBackendFailureResponse(ex);
        }
    }

    [Authorize]
    [HttpPost("multiplayer/session/ready")]
    public async Task<IActionResult> SetMultiplayerReady([FromBody] MultiplayerReadyRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/ready", new
        {
            username,
            session_id = request.SessionId,
            ready = request.Ready,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpGet("multiplayer/session")]
    public async Task<IActionResult> GetMultiplayerSession([FromQuery] string sessionId)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.GetAsync($"database/multiplayer/session?session_id={Esc(sessionId)}&username={Esc(username)}");
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPut("multiplayer/session/state")]
    public async Task<IActionResult> UpdateMultiplayerState([FromBody] MultiplayerStateRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PutAsJsonAsync("database/multiplayer/session/state", new
        {
            username,
            session_id = request.SessionId,
            room_x = request.RoomX,
            room_y = request.RoomY,
            position = new
            {
                x = request.Position.X,
                y = request.Position.Y,
                width = request.Position.Width,
                height = request.Position.Height,
                x_percent = request.Position.XPercent,
                y_percent = request.Position.YPercent,
            },
            facing = request.Facing,
            is_on_black_hole = request.IsOnBlackHole,
            gold_collected = request.GoldCollected,
            puzzle_solved = request.PuzzleSolved,
            reward_pickup_collected = request.RewardPickupCollected,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/room/move")]
    public async Task<IActionResult> MoveMultiplayerRoom([FromBody] MultiplayerMoveRoomRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/room/move", new
        {
            username,
            session_id = request.SessionId,
            target_room_x = request.TargetRoomX,
            target_room_y = request.TargetRoomY,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/puzzle/action")]
    public async Task<IActionResult> MultiplayerPuzzleAction([FromBody] MultiplayerPuzzleActionRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        object args = request.Args.ValueKind == JsonValueKind.Undefined
            ? new Dictionary<string, object?>()
            : request.Args;
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/puzzle_action", new
        {
            username,
            session_id = request.SessionId,
            action = request.Action,
            args,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/finish")]
    public async Task<IActionResult> FinishMultiplayerSession([FromBody] MultiplayerSessionRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/finish", new
        {
            username,
            session_id = request.SessionId,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/leave")]
    public async Task<IActionResult> LeaveMultiplayerSession([FromBody] MultiplayerLeaveRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/leave", new
        {
            username,
            session_id = request.SessionId,
            reason = request.Reason,
        });
        return await RelayAsync(response);
    }

    [Authorize]
    [HttpPost("multiplayer/session/sync-saved-map")]
    public async Task<IActionResult> SyncSavedMultiplayerMap([FromBody] MultiplayerSyncSavedMapRequest request)
    {
        using var client = CreateClient();
        var username = RequireAuthenticatedUsername();
        using var response = await client.PostAsJsonAsync("database/multiplayer/session/sync_saved_map", new
        {
            username,
            session_id = request.SessionId,
            map_name = request.MapName,
        });
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

public sealed class UpdateUsernameRequest
{
    public string NewUsername { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class UpdatePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class UpdateAvatarRequest
{
    public string MapName { get; set; } = string.Empty;
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

public sealed class StakingMapRequest
{
    public string? MapId { get; set; }
    public string? MapName { get; set; }
}

public sealed class StartVotingSessionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VoteType { get; set; } = "one_choice";
    public List<string> Options { get; set; } = [];
    public int DurationValue { get; set; } = 24;
    public string DurationUnit { get; set; } = "hours";
    public int VoteCostMn { get; set; } = 10;
    public int? NumberMin { get; set; }
    public int? NumberMax { get; set; }
}

public sealed class CastVoteRequest
{
    public string? OptionId { get; set; }
    public List<string> OptionIds { get; set; } = [];
    public string? TextEntry { get; set; }
    public int? NumberEntry { get; set; }
    public int VoteQuantity { get; set; } = 1;
}

public sealed class CreateMultiplayerSessionRequest
{
    public string Seed { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string Source { get; set; } = "new";
    public List<string> InvitedFriends { get; set; } = [];
}

public sealed class MultiplayerInviteRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string FriendUsername { get; set; } = string.Empty;
}

public sealed class MultiplayerSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
}

public sealed class MultiplayerReadyRequest
{
    public string SessionId { get; set; } = string.Empty;
    public bool Ready { get; set; } = true;
}

public sealed class MultiplayerPositionRequest
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 8;
    public double Height { get; set; } = 8;
    public double XPercent { get; set; } = 50;
    public double YPercent { get; set; } = 50;
}

public sealed class MultiplayerStateRequest
{
    public string SessionId { get; set; } = string.Empty;
    public int RoomX { get; set; }
    public int RoomY { get; set; }
    public MultiplayerPositionRequest Position { get; set; } = new();
    public string Facing { get; set; } = "Down";
    public bool IsOnBlackHole { get; set; }
    public int GoldCollected { get; set; }
    public bool PuzzleSolved { get; set; }
    public bool RewardPickupCollected { get; set; }
}

public sealed class MultiplayerMoveRoomRequest
{
    public string SessionId { get; set; } = string.Empty;
    public int TargetRoomX { get; set; }
    public int TargetRoomY { get; set; }
}

public sealed class MultiplayerPuzzleActionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonElement Args { get; set; }
}

public sealed class MultiplayerLeaveRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = "left_session";
}

public sealed class MultiplayerSyncSavedMapRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? MapName { get; set; }
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

public sealed class MarketplaceUpdateListingPriceRequest
{
    public string MapName { get; set; } = string.Empty;
    public int Price { get; set; }
}

public sealed class MarketplaceRemoveListingRequest
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
