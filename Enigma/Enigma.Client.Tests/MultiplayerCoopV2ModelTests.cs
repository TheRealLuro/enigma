using System.Text.Json;
using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class MultiplayerCoopV2ModelTests
{
    [Fact]
    public void DeserializesV2CoopPuzzleEnvelope()
    {
        const string json = """
        {
          "status": "success",
          "session": {
            "session_id": "mp-123",
            "status": "active",
            "owner_username": "owner",
            "guest_username": "guest",
            "seed": "easy-seed",
            "source": "new",
            "difficulty": "easy",
            "size": 2,
            "team_gold": 0,
            "solved_room_count": 0,
            "puzzle_protocol": "v2",
            "run_nonce": "nonce-xyz",
            "current_room": { "x": 1, "y": 1 },
            "current_room_progress": { "puzzle_solved": false, "reward_pickup_collected": false },
            "current_room_puzzle": {
              "key": "p",
              "difficulty": "easy",
              "name": "Split Signal",
              "instruction": "test",
              "status": "active",
              "completed": false,
              "role": "owner",
              "view_type": "coop_v2",
              "view": {
                "schema_version": 2,
                "family_id": "split_signal",
                "phase": "configure",
                "status_code": "active",
                "progress": 0.5,
                "hud": [],
                "actions": [],
                "panel": {
                  "status_text": "active",
                  "prompt": "Press E to open the panel.",
                  "hud": [],
                  "actions": []
                },
                "stage": {
                  "elements": []
                }
              }
            },
            "start_room": { "x": 1, "y": 1 },
            "finish_room": { "x": 2, "y": 2 },
            "invited_friends": [],
            "all_ready": true,
            "required_players": 2
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<MultiplayerSessionEnvelope>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.Session);
        Assert.Equal("v2", envelope.Session!.PuzzleProtocol);
        Assert.Equal("nonce-xyz", envelope.Session.RunNonce);
        Assert.Equal("coop_v2", envelope.Session.CurrentRoomPuzzle!.ViewType);
        Assert.True(envelope.Session.CurrentRoomPuzzle.View.TryGetProperty("schema_version", out var version));
        Assert.Equal(2, version.GetInt32());
        Assert.True(envelope.Session.CurrentRoomPuzzle.View.TryGetProperty("panel", out var panel));
        Assert.Equal(JsonValueKind.Object, panel.ValueKind);
        Assert.True(envelope.Session.CurrentRoomPuzzle.View.TryGetProperty("stage", out var stage));
        Assert.Equal(JsonValueKind.Object, stage.ValueKind);
    }
}
