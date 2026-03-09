using Enigma.Client.Components.ArchiveTerminal;
using Enigma.Client.Models;
using Microsoft.JSInterop;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class ArchiveTerminalControllerTests
{
    [Fact]
    public async Task InitializePrefersCanonicalQuerySelection()
    {
        var js = new FakeArchiveJsRuntime();
        js.SetLocalValue(ArchiveTerminalStorageKeys.LastFile, "exploration-problem");
        js.SetLocalValue(ArchiveTerminalStorageKeys.BootSeen, "true");

        var controller = new ArchiveTerminalController();
        var resolution = await controller.InitializeAsync(js, "e-units", null);

        Assert.Equal("e-units", resolution.SelectedFileId);
        Assert.Equal("/lore?file=e-units", resolution.CanonicalUrl);
        Assert.False(resolution.RequiresNavigation);
    }

    [Fact]
    public async Task InitializeFallsBackToLastOpenedWhenQueryIsInvalid()
    {
        var js = new FakeArchiveJsRuntime();
        js.SetLocalValue(ArchiveTerminalStorageKeys.LastFile, "exploration-problem");

        var controller = new ArchiveTerminalController();
        var resolution = await controller.InitializeAsync(js, "missing-file", null);

        Assert.Equal("exploration-problem", resolution.SelectedFileId);
        Assert.Equal("/lore?file=exploration-problem", resolution.CanonicalUrl);
        Assert.True(resolution.RequiresNavigation);
    }

    [Fact]
    public async Task LegacyDirectiveAliasNormalizesToGovernanceFile()
    {
        var controller = new ArchiveTerminalController();
        var resolution = await controller.InitializeAsync(new FakeArchiveJsRuntime(), null, "directive");

        Assert.Equal("governance", resolution.SelectedFileId);
        Assert.Equal("/lore?file=governance", resolution.CanonicalUrl);
        Assert.True(resolution.RequiresNavigation);
    }

    [Fact]
    public async Task UnlockGraphProgressesToFinalFile()
    {
        var controller = new ArchiveTerminalController();
        await controller.InitializeAsync(new FakeArchiveJsRuntime(), null, null);

        controller.SelectFile("e-units");
        Assert.True(controller.TryGetResolvedFile("explorers")?.IsFullyReadable);

        controller.SelectFile("explorers");
        Assert.True(controller.TryGetResolvedFile("anomaly-collapse")?.CanSelect);

        controller.SelectFile("anomaly-collapse");
        Assert.True(controller.TryGetResolvedFile("expedition-failure")?.IsFullyReadable);
        Assert.True(controller.TryGetResolvedFile("sectors")?.CanSelect);

        controller.SelectFile("expedition-failure");
        controller.OpenFragment("expedition-failure");
        Assert.True(controller.TryGetResolvedFile("anomaly-awareness")?.IsFullyReadable);

        controller.SelectFile("anomaly-awareness");
        controller.SelectFile("sectors");
        Assert.True(controller.TryGetResolvedFile("sector-reactivation")?.CanSelect);
        Assert.True(controller.TryGetResolvedFile("maze-nuggets")?.IsFullyReadable);
        Assert.True(controller.TryGetResolvedFile("governance")?.IsFullyReadable);

        controller.SelectFile("sector-reactivation");
        Assert.True(controller.TryGetResolvedFile("sector-images")?.IsFullyReadable);

        controller.SelectFile("sector-images");
        controller.SelectFile("governance");

        var finalFile = controller.TryGetResolvedFile("unanswered-questions");
        Assert.NotNull(finalFile);
        Assert.True(finalFile!.IsFullyReadable);
        Assert.Equal(ArchiveRightPanelType.FinalRedaction, finalFile.Definition.RightPanelType);
    }

    [Fact]
    public async Task LiveTelemetryDrivesCountsAndLogs()
    {
        var controller = new ArchiveTerminalController();
        await controller.InitializeAsync(new FakeArchiveJsRuntime(), null, null);

        controller.ApplyTelemetrySnapshot(new LoreTelemetryResponse
        {
            Status = "success",
            Overview = new LoreTelemetryOverview
            {
                SectorCount = 9,
                ExplorerCount = 4,
                LatestSectorFoundedDisplay = "Mar 09, 2026",
                SectorDataAvailable = true,
                ExplorerDataAvailable = true,
            },
            RecentSectors =
            [
                new LoreSectorRecord
                {
                    Id = "sector-1",
                    MapName = "First Sector",
                    TimeFounded = "2026-03-09T12:00:00Z",
                    TimeFoundedDisplay = "Mar 09, 2026",
                },
            ],
        });

        Assert.Equal(9, controller.Overview.SectorCount);
        Assert.Equal(4, controller.Overview.ExplorerCount);
        Assert.Equal("First Sector", controller.ActiveSectorRecord?.MapName);
        Assert.Contains(controller.SystemLogEntries, entry => entry.Message.Contains("Telemetry synced // 9 sectors // 4 explorers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GovernanceDirectoryFactUsesLiveAvailabilityState()
    {
        var controller = new ArchiveTerminalController();
        await controller.InitializeAsync(new FakeArchiveJsRuntime(), null, null);
        controller.ApplyAuthenticatedGovernanceSnapshot(new LoreGovernanceSnapshot
        {
            Available = true,
            VotingOpen = true,
            Title = "Containment Vote",
            AvailabilityLabel = "Live governance session available",
        });

        var governance = controller.TryGetResolvedFile("governance");

        Assert.NotNull(governance);
        Assert.Equal("Live governance session", controller.GetDirectoryLiveFact(governance!));
    }

    private sealed class FakeArchiveJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _local = new(StringComparer.OrdinalIgnoreCase);

        public void SetLocalValue(string key, string value)
        {
            _local[key] = value;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (string.Equals(identifier, "enigmaArchiveTerminal.getStorageItem", StringComparison.Ordinal))
            {
                var storageName = args?.ElementAtOrDefault(0)?.ToString();
                var key = args?.ElementAtOrDefault(1)?.ToString() ?? string.Empty;
                object? value = storageName == "local" && _local.TryGetValue(key, out var raw) ? raw : null;
                return ValueTask.FromResult((TValue)value!);
            }

            if (string.Equals(identifier, "enigmaArchiveTerminal.setStorageItem", StringComparison.Ordinal))
            {
                var storageName = args?.ElementAtOrDefault(0)?.ToString();
                var key = args?.ElementAtOrDefault(1)?.ToString() ?? string.Empty;
                var value = args?.ElementAtOrDefault(2)?.ToString() ?? string.Empty;
                if (storageName == "local")
                {
                    _local[key] = value;
                }

                return ValueTask.FromResult(default(TValue)!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }
}
