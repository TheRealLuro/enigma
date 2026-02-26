using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class APIController : ControllerBase
{
    private static string Esc(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
        return client;
    }

    #region Map

    // New Map
    [HttpGet("newMap")]
    public async Task<IActionResult> GetNewSeed([FromQuery] string difficulty, [FromQuery] int size)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/maze/genseed?difficulty={Esc(difficulty)}&size={size}&token={Esc(token)}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Load Map
    [HttpGet("loadMap")]
    public async Task<IActionResult> GetSeedFromName([FromQuery] string name)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/load_map?map_name={Esc(name)}&token={Esc(token)}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Add Map
    [HttpPost("addMap")]
    public async Task<IActionResult> AddMap([FromQuery] string name, string seed, int size, string difficulty, string founder, TimeOnly time, int rating)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/add?map_name={Esc(name)}&seed={Esc(seed)}&size={size}&difficulty={Esc(difficulty)}&founder={Esc(founder)}&time_completed={Esc(time.ToString())}&first_rating={rating}&token={Esc(token)}";
        using var response = await client.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    // Update Map
    [HttpPut("updateMap")]
    public async Task<IActionResult> UpdateMap([FromQuery] string seed, string username, TimeOnly time, int? rating)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/update_map?seed={Esc(seed)}&username={Esc(username)}&completion_time={Esc(time.ToString())}&token={Esc(token)}&rating={rating}";
        using var response = await client.PutAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    #endregion

    #region User

    // Sign Up
    [HttpPost("signUp")]
    public async Task<IActionResult> SignUp([FromQuery] string username, string email, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/signup?username={Esc(username)}&email={Esc(email)}&passwd={Esc(password)}&token={Esc(token)}";
        using var response = await client.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    // User Login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromQuery] string username, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/login?username={Esc(username)}&passwd={Esc(password)}&token={Esc(token)}";
        using var response = await client.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    // Update Stats
    [HttpPut("updateStats")]
    public async Task<IActionResult> UpdateStats([FromQuery] string username, string seed, string? items, int reward, bool seedExisted, bool mapLost)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/update_progress?username={Esc(username)}&map_seed={Esc(seed)}&items_in_use={Esc(items ?? string.Empty)}&earned_mn={reward}&token={Esc(token)}&seed_existed={seedExisted}&map_lost={mapLost}";
        using var response = await client.PutAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    // Get User
    [HttpGet("user")]
    public async Task<IActionResult> GetUser([FromQuery] string username, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/getuser?username={Esc(username)}&passwd={Esc(password)}&token={Esc(token)}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    #endregion

    // Get Leaderboard
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string sortBy, string order)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = CreateClient(token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/leaderboard/leaderboard?token={Esc(token)}&sort_by={Esc(sortBy)}&order={Esc(order)}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }
}
