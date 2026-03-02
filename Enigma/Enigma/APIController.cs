using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

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

    [HttpGet("newMap")]
    public async Task<IActionResult> GetNewSeed([FromQuery] string difficulty, [FromQuery] int size)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"maze/genseed?difficulty={Esc(difficulty)}&size={size}");
        return await RelayAsync(response);
    }

    [HttpGet("loadMap")]
    public async Task<IActionResult> GetSeedFromName([FromQuery] string name)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/maps/load_map?map_name={Esc(name)}");
        return await RelayAsync(response);
    }

    [HttpPost("addMap")]
    public async Task<IActionResult> AddMap([FromQuery] string name, string seed, int size, string difficulty, string founder, TimeOnly time, int rating)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/maps/add?map_name={Esc(name)}&seed={Esc(seed)}&size={size}&difficulty={Esc(difficulty)}&founder={Esc(founder)}&time_completed={Esc(time.ToString())}&first_rating={rating}",
            null);
        return await RelayAsync(response);
    }

    [HttpPut("updateMap")]
    public async Task<IActionResult> UpdateMap([FromQuery] string seed, string username, TimeOnly time, int? rating)
    {
        using var client = CreateClient();
        using var response = await client.PutAsync(
            $"database/maps/update_map?seed={Esc(seed)}&username={Esc(username)}&completion_time={Esc(time.ToString())}&rating={rating}",
            null);
        return await RelayAsync(response);
    }

    [HttpPost("signUp")]
    public async Task<IActionResult> SignUp([FromQuery] string username, string email, string password)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/users/signup?username={Esc(username)}&email={Esc(email)}&passwd={Esc(password)}",
            null);
        return await RelayAsync(response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromQuery] string username, [FromQuery] string password)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/users/login?username={Esc(username)}&passwd={Esc(password)}",
            null);
        return await RelayAsync(response);
    }

    [HttpPut("updateStats")]
    public async Task<IActionResult> UpdateStats([FromQuery] string username, string seed, string? items, int reward, bool seedExisted, bool mapLost)
    {
        using var client = CreateClient();
        using var response = await client.PutAsync(
            $"database/users/update_progress?username={Esc(username)}&map_seed={Esc(seed)}&items_in_use={Esc(items ?? string.Empty)}&earned_mn={reward}&seed_existed={seedExisted}&map_lost={mapLost}",
            null);
        return await RelayAsync(response);
    }

    [HttpGet("user")]
    public async Task<IActionResult> GetUser([FromQuery] string username)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/users/getuser?username={Esc(username)}");
        return await RelayAsync(response);
    }

    [HttpGet("userSearch")]
    public async Task<IActionResult> SearchUsers([FromQuery] string query, [FromQuery] int limit = 6)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/users/search?query={Esc(query)}&limit={limit}");
        return await RelayAsync(response);
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string sortBy = "rating", [FromQuery] string order = "desc")
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/leaderboard/leaderboard?sort_by={Esc(sortBy)}&order={Esc(order)}");
        return await RelayAsync(response);
    }

    [HttpGet("playerLeaderboard")]
    public async Task<IActionResult> GetPlayerLeaderboard([FromQuery] string sortBy = "maze_nuggets", [FromQuery] string order = "desc")
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"database/leaderboard/players?sort_by={Esc(sortBy)}&order={Esc(order)}");
        return await RelayAsync(response);
    }

    [HttpPost("friendRequest")]
    public async Task<IActionResult> SendFriendRequest([FromQuery] string senderUser, [FromQuery] string receiverUser)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/users/send_fr?sender_user={Esc(senderUser)}&receiver_user={Esc(receiverUser)}",
            null);
        return await RelayAsync(response);
    }

    [HttpPost("acceptFriend")]
    public async Task<IActionResult> AcceptFriendRequest([FromQuery] string username, [FromQuery] string adding)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/users/accept_fr?username={Esc(username)}&adding={Esc(adding)}",
            null);
        return await RelayAsync(response);
    }

    [HttpGet("marketplace")]
    public async Task<IActionResult> GetMarketplaceListings()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("database/marketplace/listings");
        return await RelayAsync(response);
    }

    [HttpPost("marketplace/add")]
    public async Task<IActionResult> AddMarketplaceListing([FromQuery] string user, [FromQuery] string mapName, [FromQuery] int price)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/maps/add_to_marketplace?user={Esc(user)}&map_name={Esc(mapName)}&price={price}",
            null);
        return await RelayAsync(response);
    }

    [HttpPost("marketplace/buy")]
    public async Task<IActionResult> BuyMarketplaceListing([FromQuery] string mapName, [FromQuery] string buyer)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"database/marketplace/buy?map_name={Esc(mapName)}&buyer={Esc(buyer)}",
            null);
        return await RelayAsync(response);
    }
}
