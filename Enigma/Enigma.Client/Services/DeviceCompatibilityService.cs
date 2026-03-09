using Enigma.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Enigma.Client.Services;

public sealed class DeviceCompatibilityService : IAsyncDisposable
{
    public const string PendingRouteStorageKey = "enigma.compatibility.pending-route.v1";

    private IJSRuntime? _js;
    private DotNetObjectReference<DeviceCompatibilityService>? _dotNetReference;
    private bool _registered;
    private readonly NavigationManager _navigationManager;

    public DeviceCompatibilityService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public event Action? Changed;

    public bool IsReady { get; private set; }

    public DeviceCapabilitySnapshot Snapshot { get; private set; } = new();

    public DeviceCompatibilityClass CurrentClass { get; private set; } = DeviceCompatibilityClass.UnknownFallback;

    public bool CanPlayGameplay => CurrentClass == DeviceCompatibilityClass.DesktopPlayable;

    public bool IsBrowseOnly => !CanPlayGameplay;

    public string? BlockedTargetRoute { get; private set; }

    public string? PendingDesktopContinuationRoute { get; private set; }

    public async Task EnsureInitializedAsync(IJSRuntime js)
    {
        _js = js;
        if (_registered)
        {
            return;
        }

        _dotNetReference ??= DotNetObjectReference.Create(this);
        Snapshot = await js.InvokeAsync<DeviceCapabilitySnapshot>("enigmaDeviceCompatibility.getSnapshot");
        CurrentClass = Classify(Snapshot);
        PendingDesktopContinuationRoute = await js.InvokeAsync<string?>("enigmaDeviceCompatibility.getPendingRoute");

        await js.InvokeVoidAsync("enigmaDeviceCompatibility.register", _dotNetReference);
        _registered = true;
        IsReady = true;
        NotifyChanged();
    }

    public async Task<GameplayCompatibilityDecision> EvaluateGameplayAccessAsync(IJSRuntime js, string targetRoute, bool persistTarget = true)
    {
        await EnsureInitializedAsync(js);

        if (CanPlayGameplay)
        {
            BlockedTargetRoute = null;
            return GameplayCompatibilityDecision.Allowed;
        }

        BlockedTargetRoute = targetRoute;
        if (persistTarget)
        {
            PendingDesktopContinuationRoute = targetRoute;
            await js.InvokeVoidAsync("enigmaDeviceCompatibility.setPendingRoute", targetRoute);
        }

        NotifyChanged();
        return CurrentClass == DeviceCompatibilityClass.UnknownFallback
            ? GameplayCompatibilityDecision.BlockedUnknown
            : GameplayCompatibilityDecision.BlockedBrowseOnly;
    }

    public async Task ClearPendingRouteAsync()
    {
        if (_js is null)
        {
            PendingDesktopContinuationRoute = null;
            BlockedTargetRoute = null;
            NotifyChanged();
            return;
        }

        PendingDesktopContinuationRoute = null;
        BlockedTargetRoute = null;
        await _js.InvokeVoidAsync("enigmaDeviceCompatibility.clearPendingRoute");
        NotifyChanged();
    }

    public async Task<bool> CopyContinuationRouteAsync(string? route = null)
    {
        if (_js is null)
        {
            return false;
        }

        var absoluteRoute = BuildAbsoluteRoute(route ?? BlockedTargetRoute ?? PendingDesktopContinuationRoute);
        if (string.IsNullOrWhiteSpace(absoluteRoute))
        {
            return false;
        }

        return await _js.InvokeAsync<bool>("enigmaDeviceCompatibility.copyText", absoluteRoute);
    }

    public void ClearBlockedTarget()
    {
        BlockedTargetRoute = null;
        NotifyChanged();
    }

    public string? GetContinuationRoute()
    {
        return BlockedTargetRoute ?? PendingDesktopContinuationRoute;
    }

    public static DeviceCompatibilityClass Classify(DeviceCapabilitySnapshot snapshot)
    {
        var hasDesktopSignals =
            snapshot.PrimaryPointerFine ||
            (snapshot.AnyFinePointer && snapshot.CanHover) ||
            (snapshot.AnyFinePointer && !snapshot.UserAgentMobile);

        if (hasDesktopSignals)
        {
            return DeviceCompatibilityClass.DesktopPlayable;
        }

        var hasTouchSignals =
            snapshot.HasTouch ||
            snapshot.MaxTouchPoints > 0 ||
            snapshot.PrimaryPointerCoarse ||
            snapshot.AnyCoarsePointer ||
            snapshot.UserAgentMobile;

        if (!hasTouchSignals)
        {
            return DeviceCompatibilityClass.UnknownFallback;
        }

        return snapshot.ViewportWidth >= 768
            ? DeviceCompatibilityClass.TabletBrowseOnly
            : DeviceCompatibilityClass.MobileBrowseOnly;
    }

    [JSInvokable]
    public Task HandleSnapshotChanged(DeviceCapabilitySnapshot snapshot)
    {
        Snapshot = snapshot ?? new DeviceCapabilitySnapshot();
        CurrentClass = Classify(Snapshot);
        IsReady = true;
        NotifyChanged();
        return Task.CompletedTask;
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }

    private string? BuildAbsoluteRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        if (Uri.TryCreate(route, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(new Uri(_navigationManager.BaseUri), route).ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_registered && _js is not null)
        {
            try
            {
                await _js.InvokeVoidAsync("enigmaDeviceCompatibility.unregister");
            }
            catch
            {
            }
        }

        _dotNetReference?.Dispose();
    }
}
