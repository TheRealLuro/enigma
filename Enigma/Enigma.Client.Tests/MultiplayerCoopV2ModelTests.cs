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
                  "actions": [],
                  "board": {
                    "template": "signal"
                  }
                },
                "stage": {
                  "elements": []
                },
                "board": {
                  "template": "signal",
                  "role_panel": {
                    "role": "owner"
                  }
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
        Assert.True(envelope.Session.CurrentRoomPuzzle.View.TryGetProperty("board", out var board));
        Assert.Equal(JsonValueKind.Object, board.ValueKind);
        Assert.Equal("signal", board.GetProperty("template").GetString());
        Assert.Equal("owner", board.GetProperty("role_panel").GetProperty("role").GetString());
    }

    [Fact]
    public void DeserializesSocketPositionEnvelope()
    {
        const string json = """
        {
          "type": "position",
          "status": "success",
          "username": "guest",
          "room": { "x": 3, "y": 2 },
          "position": {
            "x": 420.5,
            "y": 512.25,
            "width": 60,
            "height": 60,
            "x_percent": 38.9,
            "y_percent": 47.4
          },
          "facing": "Left",
          "is_on_black_hole": true,
          "team_gold": 147,
          "current_room_progress": {
            "puzzle_solved": false,
            "reward_pickup_collected": true
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<MultiplayerSocketEnvelope>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(envelope);
        Assert.Equal("position", envelope!.Type);
        Assert.Equal("guest", envelope.Username);
        Assert.NotNull(envelope.Room);
        Assert.Equal(3, envelope.Room!.X);
        Assert.NotNull(envelope.Position);
        Assert.Equal(420.5d, envelope.Position!.X);
        Assert.Equal("Left", envelope.Facing);
        Assert.True(envelope.IsOnBlackHole);
        Assert.Equal(147, envelope.TeamGold);
        Assert.NotNull(envelope.CurrentRoomProgress);
        Assert.True(envelope.CurrentRoomProgress!.RewardPickupCollected);
    }

    [Fact]
    public void DeserializesSocketPuzzleSnapshotEnvelope()
    {
        const string json = """
        {
          "type": "puzzle_state",
          "status": "success",
          "team_gold": 211,
          "current_room_progress": {
            "puzzle_solved": false,
            "reward_pickup_collected": false
          },
          "current_room_puzzle": {
            "key": "w",
            "difficulty": "medium",
            "name": "Flood Lattice",
            "instruction": "Keep both locks armed.",
            "status": "cooldown",
            "completed": false,
            "role": "guest",
            "view_type": "coop_v2",
            "view": {
              "schema_version": 2,
              "status_code": "cooldown",
              "board": {
                "template": "systems"
              }
            }
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<MultiplayerSocketEnvelope>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(envelope);
        Assert.Equal("puzzle_state", envelope!.Type);
        Assert.Equal(211, envelope.TeamGold);
        Assert.NotNull(envelope.CurrentRoomPuzzle);
        Assert.Equal("guest", envelope.CurrentRoomPuzzle!.Role);
        Assert.True(envelope.CurrentRoomPuzzle.View.TryGetProperty("board", out var board));
        Assert.Equal("systems", board.GetProperty("template").GetString());
    }
}
