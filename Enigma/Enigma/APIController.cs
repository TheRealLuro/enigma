using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class APIController : ControllerBase
{
    [HttpGet("new")]
    public async Task<IActionResult> GetNewSeed([FromQuery] string difficulty, [FromQuery] int size)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/maze/genseed?difficulty={difficulty}&size={size}";
        var result = await client.GetStringAsync(url);

        return Ok(result);
    }

    [HttpGet("load")]
    public async Task<IActionResult> GetSeedFromName([FromQuery] string name)
    {
        var token = System.IO.File.ReadAllText("Important/something.txt").Trim();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var url = $"https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/load_map?map_name={name}&token={token}";
        var result = await client.GetStringAsync(url);
        return Ok(result);
    }
}
