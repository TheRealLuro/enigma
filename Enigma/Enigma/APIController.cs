using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class APIController : ControllerBase
{
    #region Map

    // New Map
    [HttpGet("newMap")]
    public async Task<IActionResult> GetNewSeed([FromQuery] string difficulty, [FromQuery] int size)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/maze/genseed?difficulty={difficulty}&size={size}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Load Map
    [HttpGet("loadMap")]
    public async Task<IActionResult> GetSeedFromName([FromQuery] string name)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/load_map?map_name={name}&token={token}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Add Map
    [HttpGet("addMap")]
    public async Task<IActionResult> AddMap([FromQuery] string name, string seed, int size, string difficulty, string founder, TimeOnly time, int rating)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/add?map_name={name}&seed={seed}&size={size}&difficulty={difficulty}&founder={founder}&time_completed={time}&first_rating={rating}&token={token}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Update Map
    [HttpGet("updateMap")]
    public async Task<IActionResult> UpdateMap([FromQuery] string seed, string username, TimeOnly time, int? rating)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/update_map?seed={seed}&username={username}&completion_time={time}&token={token}&rating={rating}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    #endregion

    #region User

    // Sign Up
    [HttpGet("signUp")]
    public async Task<IActionResult> SignUp([FromQuery] string username, string email, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/signup?username={username}&email={email}&passwd={password}&token={token}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // User Login
    [HttpGet("login")]
    public async Task<IActionResult> Login([FromQuery] string username, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/login?username={username}&passwd={password}&token={token}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Update Stats
    [HttpGet("updateStats")]
    public async Task<IActionResult> UpdateStats([FromQuery] string username, string seed, string? items, int reward, bool seedExisted, bool mapLost)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/update_progress?username={username}&map_seed={seed}&items_in_use={items}&earned_mn={reward}&token={token}&seed_existed={seedExisted}&map_lost={mapLost}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    // Get User
    [HttpGet("user")]
    public async Task<IActionResult> GetUser([FromQuery] string username, string password)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/getuser?username={username}&passwd={password}&token={token}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }

    #endregion

    // Get Leaderboard
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string sortBy, string order)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/leaderboard/leaderboard?token={token}&sort_by={sortBy}&order={order}";
        var result = await client.GetStringAsync(url);
        return Content(result, "application/json");
    }
}
