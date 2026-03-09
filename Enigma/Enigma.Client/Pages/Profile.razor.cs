using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enigma.Client.Models;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Enigma.Client.Pages;

public partial class Profile : IAsyncDisposable
{
    private const int DefaultUsernameCooldownDays = 14;
    private const int MapCardsPerSection = 5;
    private const string OverviewTab = "overview";
    private const string MapsTab = "maps";
    private const string InventoryTab = "inventory";
    private const string SettingsTab = "settings";
    private const string MapSortByNameAsc = "name_asc";
    private const string MapSortByNameDesc = "name_desc";
    private const string MapSortByValueDesc = "value_desc";
    private const string MapSortByValueAsc = "value_asc";
    private const string MapSortByDifficulty = "difficulty";
    private const string MapSortBySizeDesc = "size_desc";
    private const string MapSortByNewest = "newest";
    private const string MapSortByOldest = "oldest";
    private static readonly TimeSpan OwnProfileRefreshInterval = TimeSpan.FromSeconds(4);

    private static readonly IReadOnlyList<(string Key, string Label)> OwnTabs =
    [
        (OverviewTab, "Console"),
        (MapsTab, "Sectors"),
        (InventoryTab, "Assets"),
        (SettingsTab, "Control"),
    ];

    private static readonly IReadOnlyList<(string Key, string Label)> PublicTabs =
    [
        (OverviewTab, "Console"),
        (MapsTab, "Sectors"),
    ];

    private static readonly IReadOnlyList<(string Key, string Label)> MapSortOptions =
    [
        (MapSortByNameAsc, "Name A-Z"),
        (MapSortByNameDesc, "Name Z-A"),
        (MapSortByValueDesc, "Value High-Low"),
        (MapSortByValueAsc, "Value Low-High"),
        (MapSortByDifficulty, "Difficulty"),
        (MapSortBySizeDesc, "Size Largest"),
        (MapSortByNewest, "Newest Discovery"),
        (MapSortByOldest, "Oldest Discovery"),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    [Inject] private EnigmaApiClient Api { get; set; } = default!;
    [Inject] private UiNotificationService Notifications { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private DeviceCompatibilityService DeviceCompatibility { get; set; } = default!;

    [Parameter] public string? ViewedUsername { get; set; }
    [SupplyParameterFromQuery(Name = "tab")] public string? RequestedTab { get; set; }

    private bool IsLoading { get; set; } = true;
    private bool IsActionBusy { get; set; }
    private bool HasError { get; set; }
    private string? LoadError { get; set; }
    private string? StatusMessage { get; set; }
    private string? LoadedUsername { get; set; }
    private string ActiveTab { get; set; } = OverviewTab;
    private string OwnedMapSearch { get; set; } = string.Empty;
    private string DiscoveredMapSearch { get; set; } = string.Empty;
    private string OwnedMapSortKey { get; set; } = MapSortByNameAsc;
    private string DiscoveredMapSortKey { get; set; } = MapSortByNameAsc;
    private int OwnedMapSectionIndex { get; set; }
    private int DiscoveredMapSectionIndex { get; set; }
    private LoginUserSummary? Session { get; set; }
    private ProfileUserData? ProfileData { get; set; }
    private List<ItemCatalogEntry> InventoryItems { get; set; } = [];
    private List<ItemCatalogEntry> OwnedGearItems =>
        InventoryItems
            .Where(item => !IsCosmeticItem(item))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    private List<ItemCatalogEntry> OwnedCosmeticItems =>
        InventoryItems
            .Where(IsCosmeticItem)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private EmailFormModel EmailForm { get; set; } = new();
    private UsernameFormModel UsernameForm { get; set; } = new();
    private PasswordFormModel PasswordForm { get; set; } = new();
    private DeleteFormModel DeleteForm { get; set; } = new();

    private bool ShowUsernamePassword { get; set; }
    private bool ShowEmailPassword { get; set; }
    private bool ShowPasswordFields { get; set; }
    private bool ShowDeletePassword { get; set; }

    private string AvatarMapName { get; set; } = string.Empty;
    private HashSet<string> StakedMapIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTimeOffset> StakedMapLockById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _ownProfileRefreshCancellation;
    private Task? _ownProfileRefreshTask;
    private bool ShowCompatibilityGate { get; set; }
    private string? PendingGameRoute { get; set; }

    private bool IsOwnProfile =>
        Session is not null
        && ProfileData is not null
        && string.Equals(Session.Username, ProfileData.Username, StringComparison.OrdinalIgnoreCase);
    private DateTimeOffset? NextUsernameChangeAt =>
        ParseUtcTimestamp(Session?.NextUsernameChangeAt)
        ?? (IsOwnProfile ? ParseUtcTimestamp(ProfileData?.NextUsernameChangeAt) : null);
    private int UsernameChangeCooldownDays
    {
        get
        {
            if (Session?.UsernameChangeCooldownDays > 0)
            {
                return Session.UsernameChangeCooldownDays;
            }

            if (IsOwnProfile && ProfileData?.UsernameChangeCooldownDays > 0)
            {
                return ProfileData.UsernameChangeCooldownDays;
            }

            return DefaultUsernameCooldownDays;
        }
    }
    private bool IsUsernameChangeCoolingDown =>
        IsOwnProfile &&
        NextUsernameChangeAt is DateTimeOffset nextChangeAt &&
        nextChangeAt > DateTimeOffset.UtcNow;
    private string UsernameCooldownMessage
    {
        get
        {
            if (!IsUsernameChangeCoolingDown || NextUsernameChangeAt is not DateTimeOffset nextChangeAt)
            {
                return $"You can update your callsign every {UsernameChangeCooldownDays} days.";
            }

            return $"Callsign updates are limited to every {UsernameChangeCooldownDays} days. "
                + $"Next update available on {nextChangeAt.ToLocalTime():MMM d, yyyy h:mm tt}.";
        }
    }

    private IReadOnlyList<(string Key, string Label)> VisibleTabs => IsOwnProfile ? OwnTabs : PublicTabs;

    private List<MapSummary> AvatarSourceOptions =>
        (ProfileData?.MapsOwned ?? [])
            .Where(map => !string.IsNullOrWhiteSpace(map.MapImage))
            .OrderBy(map => map.MapName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<SelectOption<string>> AvatarSourceDropdownOptions =>
        AvatarSourceOptions
            .Select(static map => new SelectOption<string>(map.MapName, map.MapName))
            .ToArray();

    private MapSummary? SelectedAvatarSource =>
        AvatarSourceOptions.FirstOrDefault(map => EqualsIgnoreCase(map.MapName, AvatarMapName))
        ?? AvatarSourceOptions.FirstOrDefault();

    private bool CanSaveAvatar => SelectedAvatarSource is not null && !string.IsNullOrWhiteSpace(SelectedAvatarSource.MapImage);

    private ProfileImageState? AvatarPreview =>
        string.IsNullOrWhiteSpace(AvatarMapName)
            ? ProfileData?.ProfileImage
            : new ProfileImageState
            {
                MapName = AvatarMapName,
                ImageUrl = SelectedAvatarSource?.MapImage,
                Crop = new ImageCropState(),
            };

    protected override async Task OnParametersSetAsync()
    {
        await LoadProfileAsync(forceReload: false);
    }

    private async Task LoadProfileAsync(bool forceReload)
    {
        IsLoading = true;
        HasError = false;
        StatusMessage = null;
        LoadError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            Session = await Api.GetSessionAsync();
            if (Session is null || string.IsNullOrWhiteSpace(Session.Username))
            {
                NavigationManager.NavigateTo("/", replace: true);
                return;
            }

            var targetUsername = string.IsNullOrWhiteSpace(ViewedUsername) ? Session.Username : ViewedUsername.Trim();
            var needsReload =
                forceReload
                || ProfileData is null
                || !EqualsIgnoreCase(ProfileData.Username, targetUsername);

            if (!needsReload)
            {
                ApplyRequestedTab();
                return;
            }

            var response = await Api.GetAsync($"api/auth/user?username={Uri.EscapeDataString(targetUsername)}");
            var payload = await ReadJsonAsync<ProfileResponse>(response);
            if (!response.IsSuccessStatusCode || payload?.User is null)
            {
                LoadError = await ReadErrorAsync(response);
                ProfileData = null;
                InventoryItems = [];
                StakedMapIds.Clear();
                StakedMapLockById.Clear();
                return;
            }

            ProfileData = payload.User;
            LoadedUsername = ProfileData.Username;

            if (IsOwnProfile)
            {
                await LoadInventoryAsync();
                await LoadStakingStateAsync();
                SeedSettingsForms();
            }
            else
            {
                InventoryItems = [];
                StakedMapIds.Clear();
                StakedMapLockById.Clear();
            }

            ApplyRequestedTab();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            ProfileData = null;
            InventoryItems = [];
            StakedMapIds.Clear();
            StakedMapLockById.Clear();
        }
        finally
        {
            IsLoading = false;
            ConfigureOwnProfileRefreshLoop();
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await DeviceCompatibility.EnsureInitializedAsync(JS);
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadInventoryAsync()
    {
        var inventory = await Api.GetFromJsonAsync<InventoryResponse>("api/auth/items/inventory");
        InventoryItems = inventory?.Items ?? [];
    }

    private async Task LoadStakingStateAsync()
    {
        var stakingPayload = await Api.GetFromJsonAsync<StakingOverviewResponse>("api/auth/staking");
        var stakedMaps = stakingPayload?.Overview?.StakedMaps ?? [];
        var nextStakedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextLockLookup = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (var stakedMap in stakedMaps)
        {
            var mapId = (stakedMap.MapId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                continue;
            }

            nextStakedIds.Add(mapId);
            var lockUntil = ParseUtcTimestamp(stakedMap.LockedUntil);
            if (lockUntil is DateTimeOffset expiresAt)
            {
                nextLockLookup[mapId] = expiresAt;
            }
        }

        StakedMapIds = nextStakedIds;
        StakedMapLockById = nextLockLookup;
        if (stakingPayload?.User is not null)
        {
            Session = stakingPayload.User;
        }
    }

    private void SeedSettingsForms()
    {
        if (Session is null || ProfileData is null)
        {
            return;
        }

        UsernameForm.NewUsername = Session.Username;
        UsernameForm.CurrentPassword = string.Empty;
        EmailForm.NewEmail = Session.Email;
        EmailForm.CurrentPassword = string.Empty;
        PasswordForm = new PasswordFormModel();
        DeleteForm = new DeleteFormModel();

        var profileImage = ProfileData.ProfileImage;
        AvatarMapName = profileImage?.MapName is { Length: > 0 } currentMapName
            && AvatarSourceOptions.Any(map => EqualsIgnoreCase(map.MapName, currentMapName))
                ? currentMapName
                : AvatarSourceOptions.FirstOrDefault()?.MapName ?? string.Empty;
    }

    private void ApplyRequestedTab()
    {
        var requested = string.IsNullOrWhiteSpace(RequestedTab) ? OverviewTab : RequestedTab.Trim().ToLowerInvariant();
        ActiveTab = VisibleTabs.Any(tab => tab.Key == requested) ? requested : OverviewTab;
    }

    private void SetActiveTab(string tab)
    {
        if (!VisibleTabs.Any(item => item.Key == tab))
        {
            return;
        }

        if (tab == SettingsTab && IsOwnProfile && string.IsNullOrWhiteSpace(AvatarMapName) && AvatarSourceOptions.Count > 0)
        {
            AvatarMapName = AvatarSourceOptions[0].MapName;
        }

        ActiveTab = tab;
        var basePath = IsOwnProfile ? "/profile" : $"/profile/{Uri.EscapeDataString(ProfileData?.Username ?? string.Empty)}";
        var target = tab == OverviewTab ? basePath : $"{basePath}?tab={Uri.EscapeDataString(tab)}";
        NavigationManager.NavigateTo(target, replace: true);
    }

    private static bool IsValidMapSortKey(string sortKey)
    {
        return MapSortOptions.Any(option => string.Equals(option.Key, sortKey, StringComparison.Ordinal));
    }

    private void OnOwnedMapSearchInput(ChangeEventArgs args)
    {
        OwnedMapSearch = args.Value?.ToString() ?? string.Empty;
        OwnedMapSectionIndex = 0;
    }

    private void OnDiscoveredMapSearchInput(ChangeEventArgs args)
    {
        DiscoveredMapSearch = args.Value?.ToString() ?? string.Empty;
        DiscoveredMapSectionIndex = 0;
    }

    private void OnOwnedMapSortChanged(ChangeEventArgs args)
    {
        var requestedSort = args.Value?.ToString() ?? string.Empty;
        OwnedMapSortKey = IsValidMapSortKey(requestedSort) ? requestedSort : MapSortByNameAsc;
        OwnedMapSectionIndex = 0;
    }

    private void OnDiscoveredMapSortChanged(ChangeEventArgs args)
    {
        var requestedSort = args.Value?.ToString() ?? string.Empty;
        DiscoveredMapSortKey = IsValidMapSortKey(requestedSort) ? requestedSort : MapSortByNameAsc;
        DiscoveredMapSectionIndex = 0;
    }

    private void ClearOwnedMapFilters()
    {
        OwnedMapSearch = string.Empty;
        OwnedMapSortKey = MapSortByNameAsc;
        OwnedMapSectionIndex = 0;
    }

    private void ClearDiscoveredMapFilters()
    {
        DiscoveredMapSearch = string.Empty;
        DiscoveredMapSortKey = MapSortByNameAsc;
        DiscoveredMapSectionIndex = 0;
    }

    private List<MapSummary> GetFilteredOwnedMaps()
    {
        return FilterAndSortMaps(ProfileData?.MapsOwned ?? [], OwnedMapSearch, OwnedMapSortKey);
    }

    private List<MapSummary> GetFilteredDiscoveredMaps()
    {
        return FilterAndSortMaps(ProfileData?.MapsDiscovered ?? [], DiscoveredMapSearch, DiscoveredMapSortKey);
    }

    private static List<MapSummary> FilterAndSortMaps(IEnumerable<MapSummary> source, string search, string sortKey)
    {
        var query = source ?? [];
        var normalizedSearch = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(map => MapMatchesSearch(map, normalizedSearch));
        }

        return ApplyMapSort(query, sortKey).ToList();
    }

    private static bool MapMatchesSearch(MapSummary map, string search)
    {
        return ContainsText(map.MapName, search)
            || ContainsText(map.ThemeLabel, search)
            || ContainsText(map.Theme, search)
            || ContainsText(map.Difficulty, search)
            || ContainsText(map.Founder, search)
            || ContainsText(map.Owner, search);
    }

    private static bool ContainsText(string? source, string search)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<MapSummary> ApplyMapSort(IEnumerable<MapSummary> source, string sortKey)
    {
        return sortKey switch
        {
            MapSortByNameDesc => source.OrderByDescending(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortByValueDesc => source.OrderByDescending(map => map.Value).ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortByValueAsc => source.OrderBy(map => map.Value).ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortByDifficulty => source
                .OrderBy(map => GetDifficultyRank(map.Difficulty))
                .ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortBySizeDesc => source
                .OrderByDescending(map => map.Size)
                .ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortByNewest => source
                .OrderByDescending(GetFoundedTimestampOrMin)
                .ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            MapSortByOldest => source
                .OrderBy(GetFoundedTimestampOrMin)
                .ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(map => map.MapName, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static int GetDifficultyRank(string? difficulty)
    {
        return difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => 1,
            "medium" => 2,
            "hard" => 3,
            "elite" => 4,
            _ => 5,
        };
    }

    private static DateTimeOffset GetFoundedTimestampOrMin(MapSummary map)
    {
        if (DateTimeOffset.TryParse(map.TimeFounded, out var founded))
        {
            return founded.ToUniversalTime();
        }

        return DateTimeOffset.MinValue;
    }

    private static int GetTotalMapSections(int totalMaps)
    {
        if (totalMaps <= 0)
        {
            return 1;
        }

        return (int)Math.Ceiling(totalMaps / (double)MapCardsPerSection);
    }

    private IReadOnlyList<MapSummary> GetVisibleOwnedMaps(IReadOnlyList<MapSummary> filteredMaps)
    {
        NormalizeOwnedMapSectionIndex(filteredMaps.Count);
        return filteredMaps
            .Skip(OwnedMapSectionIndex * MapCardsPerSection)
            .Take(MapCardsPerSection)
            .ToList();
    }

    private IReadOnlyList<MapSummary> GetVisibleDiscoveredMaps(IReadOnlyList<MapSummary> filteredMaps)
    {
        NormalizeDiscoveredMapSectionIndex(filteredMaps.Count);
        return filteredMaps
            .Skip(DiscoveredMapSectionIndex * MapCardsPerSection)
            .Take(MapCardsPerSection)
            .ToList();
    }

    private static IEnumerable<int> GetVisibleMapSectionIndexes(int currentSectionIndex, int totalMaps)
    {
        var totalSections = GetTotalMapSections(totalMaps);
        if (totalSections <= 7)
        {
            return Enumerable.Range(0, totalSections);
        }

        var start = Math.Max(0, currentSectionIndex - 2);
        var end = Math.Min(totalSections - 1, start + 4);
        if (end - start < 4)
        {
            start = Math.Max(0, end - 4);
        }

        return Enumerable.Range(start, end - start + 1);
    }

    private static string BuildMapSectionSummary(int currentSectionIndex, int totalMaps)
    {
        if (totalMaps <= 0)
        {
            return "No maps found.";
        }

        var totalSections = GetTotalMapSections(totalMaps);
        var start = (currentSectionIndex * MapCardsPerSection) + 1;
        var end = Math.Min(totalMaps, start + MapCardsPerSection - 1);
        return $"Section {currentSectionIndex + 1} of {totalSections} • Showing {start}-{end} of {totalMaps}";
    }

    private void NormalizeOwnedMapSectionIndex(int totalMaps)
    {
        var maxSectionIndex = Math.Max(0, GetTotalMapSections(totalMaps) - 1);
        OwnedMapSectionIndex = Math.Clamp(OwnedMapSectionIndex, 0, maxSectionIndex);
    }

    private void NormalizeDiscoveredMapSectionIndex(int totalMaps)
    {
        var maxSectionIndex = Math.Max(0, GetTotalMapSections(totalMaps) - 1);
        DiscoveredMapSectionIndex = Math.Clamp(DiscoveredMapSectionIndex, 0, maxSectionIndex);
    }

    private string GetOwnedMapSectionSummary(int totalMaps)
    {
        NormalizeOwnedMapSectionIndex(totalMaps);
        return BuildMapSectionSummary(OwnedMapSectionIndex, totalMaps);
    }

    private string GetDiscoveredMapSectionSummary(int totalMaps)
    {
        NormalizeDiscoveredMapSectionIndex(totalMaps);
        return BuildMapSectionSummary(DiscoveredMapSectionIndex, totalMaps);
    }

    private bool CanGoToPreviousOwnedMapSection(int totalMaps)
    {
        return totalMaps > MapCardsPerSection && OwnedMapSectionIndex > 0;
    }

    private bool CanGoToNextOwnedMapSection(int totalMaps)
    {
        return totalMaps > MapCardsPerSection && OwnedMapSectionIndex < GetTotalMapSections(totalMaps) - 1;
    }

    private bool CanGoToPreviousDiscoveredMapSection(int totalMaps)
    {
        return totalMaps > MapCardsPerSection && DiscoveredMapSectionIndex > 0;
    }

    private bool CanGoToNextDiscoveredMapSection(int totalMaps)
    {
        return totalMaps > MapCardsPerSection && DiscoveredMapSectionIndex < GetTotalMapSections(totalMaps) - 1;
    }

    private void GoToPreviousOwnedMapSection(int totalMaps)
    {
        if (!CanGoToPreviousOwnedMapSection(totalMaps))
        {
            return;
        }

        OwnedMapSectionIndex -= 1;
    }

    private void GoToNextOwnedMapSection(int totalMaps)
    {
        if (!CanGoToNextOwnedMapSection(totalMaps))
        {
            return;
        }

        OwnedMapSectionIndex += 1;
    }

    private void GoToPreviousDiscoveredMapSection(int totalMaps)
    {
        if (!CanGoToPreviousDiscoveredMapSection(totalMaps))
        {
            return;
        }

        DiscoveredMapSectionIndex -= 1;
    }

    private void GoToNextDiscoveredMapSection(int totalMaps)
    {
        if (!CanGoToNextDiscoveredMapSection(totalMaps))
        {
            return;
        }

        DiscoveredMapSectionIndex += 1;
    }

    private void GoToOwnedMapSection(int sectionIndex, int totalMaps)
    {
        var maxSectionIndex = Math.Max(0, GetTotalMapSections(totalMaps) - 1);
        OwnedMapSectionIndex = Math.Clamp(sectionIndex, 0, maxSectionIndex);
    }

    private void GoToDiscoveredMapSection(int sectionIndex, int totalMaps)
    {
        var maxSectionIndex = Math.Max(0, GetTotalMapSections(totalMaps) - 1);
        DiscoveredMapSectionIndex = Math.Clamp(sectionIndex, 0, maxSectionIndex);
    }

    private IEnumerable<int> GetVisibleOwnedMapSectionIndexes(int totalMaps)
    {
        NormalizeOwnedMapSectionIndex(totalMaps);
        return GetVisibleMapSectionIndexes(OwnedMapSectionIndex, totalMaps);
    }

    private IEnumerable<int> GetVisibleDiscoveredMapSectionIndexes(int totalMaps)
    {
        NormalizeDiscoveredMapSectionIndex(totalMaps);
        return GetVisibleMapSectionIndexes(DiscoveredMapSectionIndex, totalMaps);
    }

    private string GetOwnedMapSectionButtonClass(int sectionIndex)
    {
        return sectionIndex == OwnedMapSectionIndex
            ? "btn btn-primary leaderboard-page-button"
            : "btn btn-secondary leaderboard-page-button";
    }

    private string GetDiscoveredMapSectionButtonClass(int sectionIndex)
    {
        return sectionIndex == DiscoveredMapSectionIndex
            ? "btn btn-primary leaderboard-page-button"
            : "btn btn-secondary leaderboard-page-button";
    }

    private async Task SendFriendRequestAsync()
    {
        if (ProfileData is null || IsOwnProfile)
        {
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/friendRequest", new { receiverUser = ProfileData.Username }),
            async response =>
            {
                await RefreshSessionAsync();
                await LoadProfileAsync(forceReload: true);
                return await ReadStatusAsync(response, "Contact request sent.");
            });
    }

    private async Task AcceptFriendRequestAsync(string requesterUsername)
    {
        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/acceptFriend", new { adding = requesterUsername }),
            async response =>
            {
                await RefreshSessionAsync();
                await LoadProfileAsync(forceReload: true);
                return await ReadStatusAsync(response, "Contact request accepted.");
            });
    }

    private Task AcceptViewedFriendRequestAsync()
    {
        return ProfileData is null ? Task.CompletedTask : AcceptFriendRequestAsync(ProfileData.Username);
    }

    private async Task RemoveFriendAsync(string friendUsername)
    {
        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/friends/remove", new { friendUsername }),
            async response =>
            {
                await RefreshSessionAsync();
                await LoadProfileAsync(forceReload: true);
                return await ReadStatusAsync(response, "Contact removed.");
            });
    }

    private Task RemoveViewedFriendAsync()
    {
        return ProfileData is null ? Task.CompletedTask : RemoveFriendAsync(ProfileData.Username);
    }
    private async Task UpdateEmailAsync()
    {
        await ExecuteActionAsync(
            () => Api.PutJsonAsync("api/auth/account/email", new
            {
                currentPassword = EmailForm.CurrentPassword,
                newEmail = EmailForm.NewEmail,
            }),
            async response =>
            {
                var message = await RefreshSessionFromUserResponseAsync(response, "Contact email updated.");
                EmailForm.CurrentPassword = string.Empty;
                return message;
            });
    }

    private async Task UpdateUsernameAsync()
    {
        var requestedUsername = UsernameForm.NewUsername?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestedUsername))
        {
            HasError = true;
            StatusMessage = "Enter a new callsign first.";
            return;
        }

        if (Session is not null && EqualsIgnoreCase(Session.Username, requestedUsername))
        {
            HasError = true;
            StatusMessage = "Choose a different callsign.";
            return;
        }

        if (IsUsernameChangeCoolingDown)
        {
            HasError = true;
            StatusMessage = UsernameCooldownMessage;
            return;
        }

        var confirmChange = await JS.InvokeAsync<bool>(
            "confirm",
            $"Update callsign from '{Session?.Username}' to '{requestedUsername}'?");
        if (!confirmChange)
        {
            HasError = false;
            StatusMessage = "Username change canceled.";
            return;
        }

        UsernameForm.NewUsername = requestedUsername;

        await ExecuteActionAsync(
            () => Api.PutJsonAsync("api/auth/account/username", new
            {
                newUsername = requestedUsername,
                currentPassword = UsernameForm.CurrentPassword,
            }),
            async response =>
            {
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(ReadError(raw));
                }

                var payload = JsonSerializer.Deserialize<LoginResponse>(raw, JsonOptions);
                if (payload?.User is not null)
                {
                    Session = payload.User;
                    await JS.InvokeVoidAsync("enigmaGame.refreshUserSession", payload.User);
                    await JS.InvokeVoidAsync("enigmaGame.setPlayerIdentity", new Enigma.Client.Models.Gameplay.PlayerIdentity { Username = payload.User.Username }, false);
                }

                UsernameForm.CurrentPassword = string.Empty;
                var message = JsonSerializer.Deserialize<ApiStatusResponse>(raw, JsonOptions)?.ToDisplayMessage() ?? "Callsign updated.";
                NavigationManager.NavigateTo($"/profile?tab={Uri.EscapeDataString(SettingsTab)}", replace: true);
                return message;
            });
    }

    private async Task UpdatePasswordAsync()
    {
        if (!string.Equals(PasswordForm.NewPassword, PasswordForm.ConfirmPassword, StringComparison.Ordinal))
        {
            HasError = true;
            StatusMessage = "New password and confirmation must match.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PutJsonAsync("api/auth/account/password", new
            {
                currentPassword = PasswordForm.CurrentPassword,
                newPassword = PasswordForm.NewPassword,
            }),
            async response =>
            {
                var message = await RefreshSessionFromUserResponseAsync(response, "Access key updated.");
                PasswordForm = new PasswordFormModel();
                return message;
            });
    }

    private async Task UpdateAvatarAsync()
    {
        if (string.IsNullOrWhiteSpace(AvatarMapName) && AvatarSourceOptions.Count > 0)
        {
            AvatarMapName = AvatarSourceOptions[0].MapName;
        }

        if (string.IsNullOrWhiteSpace(AvatarMapName) || SelectedAvatarSource is null)
        {
            HasError = true;
            StatusMessage = "Choose an owned sector with artwork first.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PutJsonAsync("api/auth/account/avatar", new
            {
                mapName = AvatarMapName,
            }),
            async response => await RefreshSessionFromUserResponseAsync(response, "Explorer portrait updated."));
    }

    private async Task ReplayTutorialAsync()
    {
        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/tutorial", new { action = "seen" }),
            async response =>
            {
                var message = await RefreshSessionFromUserResponseAsync(response, "Orientation queued.");
                await JS.InvokeVoidAsync("enigmaGame.startTutorial");
                return message;
            });
    }

    private async Task LogoutAsync()
    {
        await Api.LogoutAsync();
        await ClearLocalSessionAsync();
        NavigationManager.NavigateTo("/", replace: true);
    }

    private async Task DeleteAccountAsync()
    {
        await ExecuteActionAsync(
            () => Api.DeleteJsonAsync("api/auth/account", new
            {
                currentPassword = DeleteForm.CurrentPassword,
                confirmUsername = DeleteForm.ConfirmUsername,
            }),
            async response =>
            {
                var message = await ReadStatusAsync(response, "Explorer account deleted.");
                await ClearLocalSessionAsync();
                NavigationManager.NavigateTo("/", replace: true);
                return message;
            });
    }

    private async Task RecycleMapAsync(MapSummary map)
    {
        if (!IsOwnProfile)
        {
            return;
        }
        if (!CanRecycleMap(map))
        {
            HasError = true;
            StatusMessage = GetRecycleDisabledReason(map);
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/maps/recycle", new { mapName = map.MapName }),
            async response =>
            {
                var message = await ReadStatusAsync(response, $"{map.MapName} recycled.");
                await RefreshSessionAsync();
                await LoadProfileAsync(forceReload: true);
                return message;
            });
    }

    private async Task OpenMapAsync(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        try
        {
            var payload = await Api.GetFromJsonAsync<SeedApiResponse>($"api/auth/loadMap?name={Uri.EscapeDataString(mapName)}");
            if (string.IsNullOrWhiteSpace(payload?.Seed))
            {
                throw new InvalidOperationException("The selected sector did not return a seed.");
            }

            PendingGameRoute = $"/game?seed={Uri.EscapeDataString(payload.Seed)}&source=load&mapName={Uri.EscapeDataString(mapName)}";
            var decision = await DeviceCompatibility.EvaluateGameplayAccessAsync(JS, PendingGameRoute);
            if (decision != GameplayCompatibilityDecision.Allowed)
            {
                ShowCompatibilityGate = true;
                await InvokeAsync(StateHasChanged);
                return;
            }

            ShowCompatibilityGate = false;
            NavigationManager.NavigateTo(PendingGameRoute);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
    }

    private async Task UseMapForPictureAsync(MapSummary map)
    {
        if (string.IsNullOrWhiteSpace(map.MapImage))
        {
            HasError = true;
            StatusMessage = $"{map.MapName} does not have artwork yet, so it cannot be used as an explorer portrait.";
            return;
        }

        AvatarMapName = map.MapName;
        await UpdateAvatarAsync();
    }

    private bool CanRecycleMap(MapSummary map)
    {
        return Session is not null
            && EqualsIgnoreCase(map.Owner, Session.Username)
            && !IsMapStaked(map);
    }

    private bool IsMapStaked(MapSummary map)
    {
        var mapId = (map.Id ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(mapId) && StakedMapIds.Contains(mapId);
    }

    private string GetRecycleDisabledReason(MapSummary map)
    {
        if (!IsMapStaked(map))
        {
            return "Recycle this sector.";
        }

        if (TryGetMapStakeLock(map, out var lockUntil) && lockUntil > DateTimeOffset.UtcNow)
        {
            return $"Analysis lock active until {lockUntil.ToLocalTime():MMM d, yyyy h:mm tt}.";
        }

        return "Sectors under analysis must be withdrawn before recycling.";
    }

    private string GetMapStakeStatusLabel(MapSummary map)
    {
        if (!IsMapStaked(map))
        {
            return string.Empty;
        }

        if (TryGetMapStakeLock(map, out var lockUntil) && lockUntil > DateTimeOffset.UtcNow)
        {
            var remaining = lockUntil - DateTimeOffset.UtcNow;
            if (remaining.TotalDays >= 1)
            {
                return $"Staked and locked for {(int)remaining.TotalDays}d {remaining.Hours}h.";
            }

            if (remaining.TotalHours >= 1)
            {
                return $"Staked and locked for {(int)remaining.TotalHours}h {remaining.Minutes}m.";
            }

            return $"Staked and locked for {Math.Max(0, remaining.Minutes)}m {Math.Max(0, remaining.Seconds)}s.";
        }

        return "Sector is under analysis. Withdraw it before recycling or listing.";
    }

    private bool TryGetMapStakeLock(MapSummary map, out DateTimeOffset lockUntil)
    {
        lockUntil = default;
        var mapId = (map.Id ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(mapId)
            && StakedMapLockById.TryGetValue(mapId, out lockUntil);
    }

    private async Task RefreshSessionAsync()
    {
        var session = await Api.GetSessionAsync();
        if (session is null)
        {
            return;
        }

        Session = session;
        await JS.InvokeVoidAsync("enigmaGame.refreshUserSession", session);
        await JS.InvokeVoidAsync("enigmaGame.setPlayerIdentity", new Enigma.Client.Models.Gameplay.PlayerIdentity { Username = session.Username }, false);
    }

    private void ConfigureOwnProfileRefreshLoop()
    {
        if (!IsOwnProfile || ProfileData is null)
        {
            StopOwnProfileRefreshLoop();
            return;
        }

        if (_ownProfileRefreshTask is { IsCompleted: false })
        {
            return;
        }

        StopOwnProfileRefreshLoop();
        _ownProfileRefreshCancellation = new CancellationTokenSource();
        _ownProfileRefreshTask = RunOwnProfileRefreshLoopAsync(_ownProfileRefreshCancellation.Token);
    }

    private void StopOwnProfileRefreshLoop()
    {
        if (_ownProfileRefreshCancellation is null)
        {
            return;
        }

        try
        {
            _ownProfileRefreshCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _ownProfileRefreshCancellation.Dispose();
        _ownProfileRefreshCancellation = null;
        _ownProfileRefreshTask = null;
    }

    private async Task RunOwnProfileRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(OwnProfileRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshOwnProfileLiveStateAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshOwnProfileLiveStateAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || IsLoading || IsActionBusy || !IsOwnProfile || ProfileData is null)
        {
            return;
        }

        var latestSession = await Api.GetSessionAsync(cancellationToken);
        if (latestSession is null || string.IsNullOrWhiteSpace(latestSession.Username))
        {
            return;
        }

        var friendsChanged = !SetEqualsIgnoreCase(ProfileData.Friends, latestSession.Friends);
        var requestsChanged = !SetEqualsIgnoreCase(ProfileData.FriendRequests, latestSession.FriendRequests);
        var nuggetsChanged = ProfileData.MazeNuggets != latestSession.MazeNuggets;
        var profileImageChanged = !Equals(ProfileData.ProfileImage?.ImageUrl, latestSession.ProfileImage?.ImageUrl);
        var onlineChanged = ProfileData.IsOnline != latestSession.IsOnline;
        var stakedCountChanged = ProfileData.StakedMapsCount != latestSession.StakedMapsCount;
        var hasChanges = friendsChanged || requestsChanged || nuggetsChanged || profileImageChanged || onlineChanged || stakedCountChanged;
        Session = latestSession;

        if (!hasChanges)
        {
            return;
        }

        ProfileData.Friends = NormalizeUsernameList(latestSession.Friends);
        ProfileData.FriendRequests = NormalizeUsernameList(latestSession.FriendRequests);
        ProfileData.MazeNuggets = latestSession.MazeNuggets;
        ProfileData.ProfileImage = latestSession.ProfileImage;
        ProfileData.OwnedMapsCount = latestSession.OwnedMapsCount;
        ProfileData.DiscoveredMapsCount = latestSession.DiscoveredMapsCount;
        ProfileData.StakedMapsCount = latestSession.StakedMapsCount;
        ProfileData.IsOnline = latestSession.IsOnline;
        if (stakedCountChanged)
        {
            await LoadStakingStateAsync();
        }

        await JS.InvokeVoidAsync("enigmaGame.refreshUserSession", latestSession);
        await JS.InvokeVoidAsync("enigmaGame.setPlayerIdentity", new Enigma.Client.Models.Gameplay.PlayerIdentity { Username = latestSession.Username }, false);
        await InvokeAsync(StateHasChanged);
    }

    private async Task<string> RefreshSessionFromUserResponseAsync(HttpResponseMessage response, string successFallback)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadError(raw));
        }

        var payload = JsonSerializer.Deserialize<LoginResponse>(raw, JsonOptions);
        if (payload?.User is not null)
        {
            Session = payload.User;
            await JS.InvokeVoidAsync("enigmaGame.refreshUserSession", payload.User);
            await JS.InvokeVoidAsync("enigmaGame.setPlayerIdentity", new Enigma.Client.Models.Gameplay.PlayerIdentity { Username = payload.User.Username }, false);
        }

        await LoadProfileAsync(forceReload: true);
        return successFallback;
    }

    private async Task ExecuteActionAsync(Func<Task<HttpResponseMessage>> requestFactory, Func<HttpResponseMessage, Task<string>> onSuccess)
    {
        if (IsActionBusy)
        {
            return;
        }

        IsActionBusy = true;
        HasError = false;
        StatusMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            using var response = await requestFactory();
            StatusMessage = await onSuccess(response);
            if (!string.IsNullOrWhiteSpace(StatusMessage))
            {
                Notifications.Success(StatusMessage);
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            if (!string.IsNullOrWhiteSpace(StatusMessage))
            {
                Notifications.Error(StatusMessage);
            }
        }
        finally
        {
            IsActionBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(raw) ? default : JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private static async Task<string> ReadStatusAsync(HttpResponseMessage response, string fallback)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadError(raw));
        }

        var payload = JsonSerializer.Deserialize<ApiStatusResponse>(raw, JsonOptions);
        return payload?.ToDisplayMessage() ?? fallback;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        return ReadError(await response.Content.ReadAsStringAsync());
    }

    private static string ReadError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Request failed.";
        }

        try
        {
            var status = JsonSerializer.Deserialize<ApiStatusResponse>(raw, JsonOptions);
            if (!string.IsNullOrWhiteSpace(status?.ToDisplayMessage()))
            {
                return status.ToDisplayMessage();
            }

            var detail = JsonSerializer.Deserialize<FastApiError>(raw, JsonOptions);
            if (!string.IsNullOrWhiteSpace(detail?.Detail))
            {
                return detail.Detail;
            }
        }
        catch
        {
        }

        return raw;
    }
    private string GetTabClass(string tab)
    {
        return ActiveTab == tab ? "profile-tab active" : "profile-tab";
    }



    private static string FormatPercent(double value)
    {
        return $"{value:P0}";
    }

    private static string FormatBestTime(MapSummary map)
    {
        var source = !string.IsNullOrWhiteSpace(map.BestTimeDisplay) ? map.BestTimeDisplay : map.BestTime;
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return "N/A";
        }

        var parts = source.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 4
            && int.TryParse(parts[0], out var hours)
            && int.TryParse(parts[1], out var minutes)
            && int.TryParse(parts[2], out var seconds)
            && int.TryParse(parts[3], out var milliseconds))
        {
            if (hours > 0)
            {
                return $"{hours}h {minutes:D2}m {seconds:D2}.{milliseconds:D3}s";
            }

            if (minutes > 0)
            {
                return $"{minutes}m {seconds:D2}.{milliseconds:D3}s";
            }

            return $"{seconds}.{milliseconds:D3}s";
        }

        return source;
    }

    private static string FormatFounded(MapSummary map)
    {
        if (!string.IsNullOrWhiteSpace(map.TimeFoundedDisplay))
        {
            return map.TimeFoundedDisplay;
        }

        if (DateTimeOffset.TryParse(map.TimeFounded, out var founded))
        {
            return founded.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        }

        return "Unknown";
    }

    private static string FormatFounder(MapSummary map)
    {
        if (!string.IsNullOrWhiteSpace(map.Founder))
        {
            return map.Founder;
        }

        if (!string.IsNullOrWhiteSpace(map.Owner))
        {
            return map.Owner;
        }

        return "Unknown";
    }

    private static string FormatThemeLabel(string? themeLabel, string? theme)
    {
        var value = !string.IsNullOrWhiteSpace(themeLabel) ? themeLabel : theme;
        return string.IsNullOrWhiteSpace(value) ? "Cartoon" : value.Replace('_', ' ').Trim();
    }

    private bool IsCosmeticItem(ItemCatalogEntry item)
    {
        if (string.Equals(item.Category, "cosmetic", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ProfileData?.OwnedCosmetics.Any(itemId => EqualsIgnoreCase(itemId, item.ItemId)) == true;
    }

    private static string FormatItemUses(ItemCatalogEntry item)
    {
        return item.MaxPerRun <= 0 ? "Permanent" : item.MaxPerRun.ToString();
    }

    private static string? FormatPurchaseLimit(ItemCatalogEntry item)
    {
        if (item.PurchaseLimit <= 0)
        {
            return null;
        }

        return item.PurchaseLimit == 1
            ? "1 per person"
            : $"{item.PurchaseLimit} per person";
    }

    private static string GetDifficultyClass(string difficulty)
    {
        return difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => "easy",
            "medium" => "medium",
            "hard" => "hard",
            _ => "default",
        };
    }

    private static string GetInitials(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "??";
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private void OpenProfile(string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            NavigationManager.NavigateTo($"/profile/{Uri.EscapeDataString(username)}");
        }
    }

    private void GoHome() => NavigationManager.NavigateTo("/home");

    private void GoPlayers() => NavigationManager.NavigateTo("/players");

    private Task CloseCompatibilityGateAsync()
    {
        ShowCompatibilityGate = false;
        DeviceCompatibility.ClearBlockedTarget();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private bool ShowBrowseOnlyNotice => DeviceCompatibility.IsReady && DeviceCompatibility.IsBrowseOnly;

    private static bool EqualsIgnoreCase(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeUsernameList(IEnumerable<string>? usernames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();
        foreach (var username in usernames ?? [])
        {
            var value = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                continue;
            }

            normalized.Add(value);
        }

        return normalized;
    }

    private static bool SetEqualsIgnoreCase(IEnumerable<string>? left, IEnumerable<string>? right)
    {
        var leftSet = new HashSet<string>(NormalizeUsernameList(left), StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(NormalizeUsernameList(right), StringComparer.OrdinalIgnoreCase);
        return leftSet.SetEquals(rightSet);
    }

    private static DateTimeOffset? ParseUtcTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private async Task ClearLocalSessionAsync()
    {
        await JS.InvokeVoidAsync("enigmaGame.clearPlayerIdentity");
        await JS.InvokeVoidAsync("enigmaGame.clearUserSession");
        await JS.InvokeVoidAsync("enigmaGame.clearActiveGameSession");
        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.clearPendingLossSummary");
        await JS.InvokeVoidAsync("enigmaGame.clearRunLoadout");
    }

    public ValueTask DisposeAsync()
    {
        StopOwnProfileRefreshLoop();
        return ValueTask.CompletedTask;
    }

    private sealed class SeedApiResponse
    {
        [JsonPropertyName("seed")]
        public string Seed { get; set; } = string.Empty;
    }

    private sealed class FastApiError
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    private sealed class EmailFormModel
    {
        [Required]
        [EmailAddress]
        public string NewEmail { get; set; } = string.Empty;

        [Required]
        public string CurrentPassword { get; set; } = string.Empty;
    }

    private sealed class UsernameFormModel
    {
        [Required]
        [MinLength(3)]
        [MaxLength(32)]
        public string NewUsername { get; set; } = string.Empty;

        [Required]
        public string CurrentPassword { get; set; } = string.Empty;
    }

    private sealed class PasswordFormModel
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    private sealed class DeleteFormModel
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmUsername { get; set; } = string.Empty;
    }
}
