using Enigma.Client.Components.ArchiveTerminal;
using Enigma.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Enigma.Client.Pages;

public partial class Lore : IAsyncDisposable
{
    private CancellationTokenSource? _liveRefreshLoopCts;
    private bool _initialized;
    private bool _initializationStarted;
    private bool _refreshingLiveData;
    private bool _sectorImagesAttempted;

    [Parameter]
    public string? LegacyGroup { get; set; }

    [SupplyParameterFromQuery(Name = "file")]
    public string? FileId { get; set; }

    protected ArchiveTerminalController Controller { get; } = new();
    protected List<LoreSectorImageRecord> SectorImages { get; private set; } = [];
    protected bool IsSectorImagesLoading { get; private set; }
    protected bool IsAuthenticatedUser { get; private set; }

    protected string PageTitle =>
        Controller.SelectedFile is null
            ? "Enigma Research Archive | Public Observer Terminal"
            : $"{Controller.SelectedFile.Definition.Title} | Enigma Research Archive";

    protected string PageDescription =>
        Controller.SelectedFile?.Definition.Summary
        ?? "Access the Enigma public observer archive and browse anomaly research files, explorer logs, and sector intelligence.";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _initializationStarted)
        {
            return;
        }

        _initializationStarted = true;
        var routeResolution = await Controller.InitializeAsync(JS, FileId, LegacyGroup);
        _initialized = true;

        if (routeResolution.RequiresNavigation)
        {
            NavigationManager.NavigateTo(routeResolution.CanonicalUrl, replace: true);
        }

        await RefreshLiveDataAsync();
        await RefreshSessionStateAsync();
        await EnsureSectorImagesAsync();
        StartLiveRefreshLoop();
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized)
        {
            return;
        }

        var routeResolution = Controller.ApplyRoute(FileId, LegacyGroup);
        if (routeResolution.RequiresNavigation && !MatchesCurrentPathAndQuery(routeResolution.CanonicalUrl))
        {
            NavigationManager.NavigateTo(routeResolution.CanonicalUrl, replace: true);
        }

        await EnsureSectorImagesAsync();
    }

    protected async Task HandleBootCompletedAsync()
    {
        Controller.CompleteBoot();
        await Controller.PersistAsync(JS);
        await InvokeAsync(StateHasChanged);
    }

    protected async Task HandleSelectFileAsync(string fileId)
    {
        var result = Controller.SelectFile(fileId);
        Controller.CloseSidebar();

        if (result.WasDenied)
        {
            await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-denied", new { fileId, message = result.DeniedMessage });
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (result.SelectedChanged || !MatchesCurrentPathAndQuery(result.CanonicalUrl))
        {
            NavigationManager.NavigateTo(result.CanonicalUrl, replace: false);
        }

        await EnsureSectorImagesAsync();
        await Controller.PersistAsync(JS);
        await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-select", new { fileId });

        if (result.TriggerCorruptionPulse)
        {
            await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-corruption", new { fileId });
        }

        if (result.NewlyUnlockedIds.Count > 0)
        {
            await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-unlocked", new { fileIds = result.NewlyUnlockedIds });
        }

        await InvokeAsync(StateHasChanged);
    }

    protected async Task HandleHoverFileAsync(string? fileId)
    {
        Controller.SetPreviewFile(fileId);
        await InvokeAsync(StateHasChanged);
    }

    protected async Task HandleFilterChangedAsync(ArchiveDirectoryFilter filter)
    {
        Controller.SetFilter(filter);
        await InvokeAsync(StateHasChanged);
    }

    protected async Task HandleOpenFragmentAsync(string fileId)
    {
        var result = Controller.OpenFragment(fileId);
        if (!result.Opened)
        {
            return;
        }

        await Controller.PersistAsync(JS);
        await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-fragment", new { fileId });

        if (result.NewlyUnlockedIds.Count > 0)
        {
            await JS.InvokeVoidAsync("enigmaArchiveTerminal.emitArchiveEvent", "enigma:archive-unlocked", new { fileIds = result.NewlyUnlockedIds });
        }

        await InvokeAsync(StateHasChanged);
    }

    protected async Task HandleCloseFragmentAsync()
    {
        Controller.CloseFragment();
        await Controller.PersistAsync(JS);
        await InvokeAsync(StateHasChanged);
    }

    protected Task HandleToggleSidebarAsync()
    {
        Controller.ToggleSidebar();
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected Task HandleCloseSidebarAsync()
    {
        Controller.CloseSidebar();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task RefreshLiveDataAsync(CancellationToken cancellationToken = default)
    {
        if (_refreshingLiveData)
        {
            return;
        }

        _refreshingLiveData = true;

        try
        {
            var telemetryTask = LoadTelemetryAsync(cancellationToken);
            var atlasTask = LoadSectorAtlasAsync(cancellationToken);
            var governanceTask = LoadAuthenticatedGovernanceAsync(cancellationToken);

            await Task.WhenAll(telemetryTask, atlasTask, governanceTask);

            Controller.ApplyTelemetrySnapshot(await telemetryTask);
            Controller.ApplySectorAtlasSnapshot(await atlasTask);
            Controller.ApplyAuthenticatedGovernanceSnapshot(await governanceTask);
        }
        finally
        {
            _refreshingLiveData = false;
        }
    }

    private async Task RefreshSessionStateAsync()
    {
        try
        {
            var session = await Api.GetSessionAsync(lightweight: true);
            IsAuthenticatedUser = session is not null && !string.IsNullOrWhiteSpace(session.Username);
        }
        catch
        {
            IsAuthenticatedUser = false;
        }
    }

    private async Task<LoreTelemetryResponse?> LoadTelemetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Api.GetAsync("api/public/lore/telemetry", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await Api.ReadJsonAsync<LoreTelemetryResponse>(response, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LoreSectorAtlasResponse?> LoadSectorAtlasAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Api.GetAsync("api/public/lore/sector-atlas?limit=24", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await Api.ReadJsonAsync<LoreSectorAtlasResponse>(response, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LoreGovernanceSnapshot?> LoadAuthenticatedGovernanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Api.GetAsync("api/auth/voting/session", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await Api.ReadJsonAsync<GovernanceSessionResponse>(response, cancellationToken);
            return LoreLiveDataComposer.ToGovernanceSnapshot(payload);
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureSectorImagesAsync(CancellationToken cancellationToken = default)
    {
        if (IsSectorImagesLoading ||
            _sectorImagesAttempted ||
            !string.Equals(Controller.SelectedFileId, "sector-images", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsSectorImagesLoading = true;
        _sectorImagesAttempted = true;
        var requestFailed = false;

        try
        {
            using var response = await Api.GetAsync("api/public/lore/sector-images?limit=6", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                requestFailed = true;
                SectorImages = [];
                return;
            }

            var payload = await Api.ReadJsonAsync<LoreSectorImageResponse>(response, cancellationToken);
            SectorImages = payload?.Images?
                .Where(image => !string.IsNullOrWhiteSpace(image.MapImage))
                .Take(6)
                .ToList() ?? [];
        }
        catch
        {
            requestFailed = true;
            SectorImages = [];
        }
        finally
        {
            Controller.RegisterSectorImagesState(SectorImages.Count, requestFailed);
            IsSectorImagesLoading = false;
        }
    }

    private bool MatchesCurrentPathAndQuery(string relativeUrl)
    {
        var current = new Uri(NavigationManager.Uri).PathAndQuery;
        return string.Equals(current, relativeUrl, StringComparison.OrdinalIgnoreCase);
    }

    private void StartLiveRefreshLoop()
    {
        if (_liveRefreshLoopCts is not null)
        {
            return;
        }

        _liveRefreshLoopCts = new CancellationTokenSource();
        _ = RunLiveRefreshLoopAsync(_liveRefreshLoopCts.Token);
    }

    private async Task RunLiveRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(45));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshLiveDataAsync(cancellationToken);
                await RefreshSessionStateAsync();
                await EnsureSectorImagesAsync(cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_liveRefreshLoopCts is not null)
        {
            try
            {
                _liveRefreshLoopCts.Cancel();
            }
            catch
            {
            }

            _liveRefreshLoopCts.Dispose();
        }

        try
        {
            await Controller.PersistAsync(JS);
        }
        catch
        {
        }
    }
}
