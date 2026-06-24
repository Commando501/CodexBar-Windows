using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace CodexBarTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private ServeProcess? _serve;
    private UsageClient? _client;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _updateTimer;
    private MenuItem? _statusItem;
    private MenuItem? _updateItem;

    private readonly UpdateChecker _updateChecker = new();
    private UpdateInfo? _pendingUpdate;

    private readonly UsageViewModel _usageVm = new();
    private readonly ConfigService _config = new();
    private readonly UiSettings _ui = UiSettings.Load();
    private readonly QuotaNotificationCoordinator _notifications = new();
    private readonly WidgetManager _widgets = new(WidgetStore.Load());
    private readonly UsageHistoryStore _history = UsageHistoryStore.Load();
    private SettingsWindow? _settingsWindow;
    private UsageWindow? _usageWindow;
    private List<ProviderViewModel> _latestTiles = new();
    private bool _refreshing;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var popup = new UsagePopup { DataContext = _usageVm };
        _usageWindow = new UsageWindow(popup, _ui);

        _trayIcon = new TaskbarIcon
        {
            IconSource = TrayIconFactory.CreateDefault(),
            ToolTipText = "CodexBar — starting…",
            ContextMenu = BuildContextMenu(),
        };
        // Left-click toggles our own draggable window; refresh as it opens.
        _trayIcon.TrayLeftMouseUp += (_, _) =>
        {
            if (_usageWindow is null) return;
            var willShow = !_usageWindow.IsVisible;
            _usageWindow.ToggleFromTray();
            if (willShow && _usageWindow.IsVisible) _ = RefreshUsageAsync();
        };

        // Restore a pinned panel so it's there as soon as the app launches.
        if (_ui.AlwaysOnScreen) _usageWindow.ShowPanel();

        // Restore any pinned desktop widgets; they show "waiting for data" until
        // the first refresh, then update alongside the panel.
        _widgets.RefreshRequested += () => _ = RefreshUsageAsync();
        _widgets.RestoreSaved();

        _ = StartEngineAsync();
        StartUpdateChecks();
    }

    private void StartUpdateChecks()
    {
        // Daily automatic checks plus one shortly after launch.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(manual: false);
        _updateTimer.Start();
        _ = CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && !_ui.AutomaticUpdateChecks) return;

        UpdateInfo? info;
        try
        {
            info = await _updateChecker.CheckAsync(AppVersion.Current);
        }
        catch (Exception ex)
        {
            if (manual) Dispatcher.Invoke(() => ShowBalloon("Update check failed", ex.Message, BalloonIcon.Warning));
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (info is not null)
            {
                _pendingUpdate = info;
                if (_updateItem is not null)
                {
                    _updateItem.Header = $"Download update (v{info.Version.ToString(3)})…";
                    _updateItem.Visibility = Visibility.Visible;
                }
                // Toast once per release for automatic checks; always for manual.
                if (manual || _ui.LastNotifiedUpdateTag != info.TagName)
                {
                    ShowBalloon(
                        "Update available",
                        $"CodexBar {info.Version.ToString(3)} is available — open the tray menu to download.",
                        BalloonIcon.Info);
                    _ui.LastNotifiedUpdateTag = info.TagName;
                    _ui.Save();
                }
            }
            else if (manual)
            {
                ShowBalloon("CodexBar is up to date", $"You're on the latest version (v{AppVersion.DisplayString}).", BalloonIcon.Info);
            }
        });
    }

    private void ShowBalloon(string title, string message, BalloonIcon icon) =>
        _trayIcon?.ShowBalloonTip(title, message, icon);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* nothing actionable if the shell can't open the browser */ }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        _statusItem = new MenuItem { Header = "Starting…", IsEnabled = false };
        menu.Items.Add(_statusItem);

        // Hidden until an update is found; opens the release page when shown.
        _updateItem = new MenuItem { Header = "Download update…", Visibility = Visibility.Collapsed };
        _updateItem.Click += (_, _) => { if (_pendingUpdate is { } u) OpenUrl(u.ReleaseUrl); };
        menu.Items.Add(_updateItem);

        menu.Items.Add(new Separator());

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += async (_, _) => await RefreshUsageAsync();
        menu.Items.Add(refresh);

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var checkUpdates = new MenuItem { Header = "Check for updates…" };
        checkUpdates.Click += (_, _) => _ = CheckForUpdatesAsync(manual: true);
        menu.Items.Add(checkUpdates);

        var widgets = new MenuItem { Header = "Widgets" };
        // Rebuild the provider list each time so it reflects the latest refresh.
        // SubmenuOpened bubbles, so it also fires when a child submenu opens
        // (e.g. hovering "Usage"). Guard on OriginalSource: rebuilding then would
        // clear the item under the cursor and collapse the whole menu.
        widgets.SubmenuOpened += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, widgets)) PopulateWidgetsMenu(widgets);
        };
        widgets.Items.Add(new MenuItem { Header = "Loading…", IsEnabled = false });
        menu.Items.Add(widgets);

        var alwaysOnScreen = new MenuItem
        {
            Header = "Always on screen",
            IsCheckable = true,
            IsChecked = _ui.AlwaysOnScreen,
        };
        alwaysOnScreen.Click += (_, _) =>
        {
            if (_usageWindow is null) return;
            _usageWindow.AlwaysOnScreen = alwaysOnScreen.IsChecked;
            // Pinning brings the panel up (and keeps it up); unpinning leaves it
            // visible but it will now dismiss on click-away like a normal popover.
            if (alwaysOnScreen.IsChecked)
            {
                _usageWindow.ShowPanel();
                _ = RefreshUsageAsync();
            }
        };
        menu.Items.Add(alwaysOnScreen);

        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true };
        try { startup.IsChecked = StartupRegistration.IsEnabled(); } catch { /* registry unavailable */ }
        startup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(startup.IsChecked); }
            catch { startup.IsChecked = !startup.IsChecked; }
        };
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit CodexBar" };
        quit.Click += (_, _) => Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    // Cost data is only available for Claude and Codex (local token-cost files).
    private static readonly HashSet<string> CostProviderIds =
        new(StringComparer.OrdinalIgnoreCase) { "claude", "codex" };

    private void PopulateWidgetsMenu(MenuItem root)
    {
        root.Items.Clear();

        root.Items.Add(BuildAddSubmenu("Usage", WidgetKind.Usage, _ => true));
        root.Items.Add(BuildAddSubmenu("Cost", WidgetKind.Cost, CostProviderIds.Contains));
        root.Items.Add(BuildAddSubmenu("Cost history", WidgetKind.CostHistory, CostProviderIds.Contains));
        root.Items.Add(BuildWindowGroupedSubmenu("Burn-down", WidgetKind.BurnDown, _ => true));
        root.Items.Add(BuildWindowGroupedSubmenu("Usage history", WidgetKind.UsageHistory, _ => true));

        root.Items.Add(new Separator());
        var removeAll = new MenuItem { Header = "Remove all widgets", IsEnabled = _widgets.Count > 0 };
        removeAll.Click += (_, _) => _widgets.RemoveAll();
        root.Items.Add(removeAll);
    }

    // A parent menu with Session / Weekly sub-submenus, each listing providers.
    private MenuItem BuildWindowGroupedSubmenu(string header, WidgetKind kind, Func<string, bool> providerFilter)
    {
        var menu = new MenuItem { Header = header };
        menu.Items.Add(BuildAddSubmenu("Session", kind, providerFilter, QuotaWindowKind.Session));
        menu.Items.Add(BuildAddSubmenu("Weekly", kind, providerFilter, QuotaWindowKind.Weekly));
        return menu;
    }

    private MenuItem BuildAddSubmenu(
        string header,
        WidgetKind kind,
        Func<string, bool> providerFilter,
        QuotaWindowKind window = QuotaWindowKind.Session)
    {
        var menu = new MenuItem { Header = header };
        var providers = _latestTiles
            .Where(t => !string.IsNullOrEmpty(t.Id) && providerFilter(t.Id))
            .ToList();

        if (providers.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No providers yet", IsEnabled = false });
            return menu;
        }

        foreach (var tile in providers)
        {
            var id = tile.Id;
            var item = new MenuItem { Header = tile.Name };
            item.Click += (_, _) => AddWidgetAndRefresh(id, kind, window);
            menu.Items.Add(item);
        }
        return menu;
    }

    private void AddWidgetAndRefresh(string providerId, WidgetKind kind, QuotaWindowKind window)
    {
        _widgets.AddWidget(providerId, kind, window);
        // Refresh promptly so the new widget (especially cost) populates without waiting.
        _ = RefreshUsageAsync();
    }

    private async Task StartEngineAsync()
    {
        try
        {
            var exePath = AppPaths.ResolveCodexBarExe();
            var runtimeDir = AppPaths.ResolveSwiftRuntimeDir();
            _serve = new ServeProcess(exePath, runtimeDir);
            await _serve.StartAsync(TimeSpan.FromSeconds(15));

            _client = new UsageClient(_serve.BaseUrl);
            SetStatus($"Connected — port {_serve.Port}");
            await RefreshUsageAsync();
            StartRefreshTimer();
        }
        catch (Exception ex)
        {
            SetStatus($"Engine error: {ex.Message}");
        }
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();
        _refreshTimer.Start();
    }

    private async Task RefreshUsageAsync()
    {
        if (_client is null || _serve is null || _refreshing) return;
        if (!_serve.IsRunning)
        {
            SetStatus("Engine stopped");
            return;
        }

        _refreshing = true;
        _usageVm.Status = "Refreshing…";
        try
        {
            var json = await _client.GetUsageJsonAsync();
            var results = UsageJson.Parse(json);
            var tiles = UsageViewModelBuilder.Build(results).ToList();
            var maxPercent = tiles
                .SelectMany(t => t.Windows)
                .Select(w => w.UsedPercent)
                .DefaultIfEmpty(0)
                .Max();
            var prefs = new NotificationPrefs(
                _ui.SessionQuotaNotificationsEnabled,
                _ui.QuotaWarningNotificationsEnabled,
                _ui.QuotaWarningThresholds);
            var notifications = _notifications.Evaluate(results, prefs);

            // Only pay for /cost when a cost widget is actually pinned. The widget
            // list lives on the UI thread, so read the flag there.
            var costs = new List<CostResult>();
            if (Dispatcher.Invoke(() => _widgets.NeedsCost))
            {
                try { costs = CostJson.Parse(await _client.GetCostJsonAsync()); }
                catch { /* cost is best-effort; leave empty on failure */ }
            }
            Dispatcher.Invoke(() =>
            {
                _usageVm.Replace(tiles);
                _usageVm.Status = $"Updated {DateTime.Now:HH:mm}";
                UpdateTrayIcon(maxPercent / 100.0, connected: true);
                foreach (var notification in notifications) ShowNotification(notification);
                _latestTiles = tiles;
                // Sample utilization history before handing data to the widgets.
                _history.Record(results, DateTimeOffset.Now);
                _widgets.UpdateData(new WidgetData(tiles, results, costs, _history));
            });
            SetTooltip(tiles.Count == 0
                ? "CodexBar — no providers enabled"
                : $"CodexBar — {Math.Round(maxPercent)}% peak across {tiles.Count} provider(s)");
        }
        catch (Exception ex)
        {
            _usageVm.Status = "Fetch failed";
            Dispatcher.Invoke(() => UpdateTrayIcon(0, connected: false));
            SetTooltip($"CodexBar — fetch failed: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetStatus(string status)
    {
        void Apply()
        {
            if (_statusItem is not null) _statusItem.Header = status;
            _usageVm.Status = status;
            if (_trayIcon is not null) _trayIcon.ToolTipText = $"CodexBar — {status}";
        }

        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.Invoke(Apply);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_config, _ui, onChanged: () => _ = RefreshUsageAsync());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ShowNotification(NotificationItem item)
    {
        // Shell balloon notifications (routed through the Action Center on Win 10/11);
        // no app packaging or extra dependency required.
        _trayIcon?.ShowBalloonTip(
            item.Title,
            item.Body,
            item.IsWarning ? BalloonIcon.Warning : BalloonIcon.Info);
    }

    private void UpdateTrayIcon(double fraction, bool connected)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IconSource = TrayIconFactory.Render(fraction, connected);
        }
    }

    private void SetTooltip(string tooltip)
    {
        void Apply()
        {
            if (_trayIcon is not null) _trayIcon.ToolTipText = tooltip;
        }

        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.Invoke(Apply);
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _refreshTimer?.Stop();
        _updateTimer?.Stop();
        _updateChecker.Dispose();
        _widgets.CloseAll();
        _usageWindow?.Close();
        _client?.Dispose();
        _serve?.Dispose();
        _trayIcon?.Dispose();
    }
}
