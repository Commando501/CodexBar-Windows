namespace CodexBarTray;

/// <summary>
/// Owns the live desktop widget windows: restores saved ones on launch, adds and
/// removes them on request, persists their positions, and pushes fresh provider
/// data into each on every /usage refresh. All members must be called on the UI
/// thread (they create and mutate WPF windows).
/// </summary>
public sealed class WidgetManager
{
    private readonly WidgetStore _store;
    private readonly List<DesktopWidget> _widgets = new();
    private IReadOnlyList<ProviderViewModel> _latest = Array.Empty<ProviderViewModel>();

    /// <summary>Raised when a widget's "Refresh" is invoked; the app re-fetches usage.</summary>
    public event Action? RefreshRequested;

    public WidgetManager(WidgetStore store) => _store = store;

    public int Count => _widgets.Count;

    /// <summary>Recreate windows for every persisted widget config.</summary>
    public void RestoreSaved()
    {
        foreach (var config in _store.Widgets.ToList())
            CreateWindow(config);
    }

    public DesktopWidget AddWidget(string providerId)
    {
        var config = new WidgetConfig { ProviderId = providerId };
        _store.Widgets.Add(config);
        _store.Save();
        return CreateWindow(config);
    }

    public void RemoveWidget(DesktopWidget widget)
    {
        _widgets.Remove(widget);
        _store.Widgets.RemoveAll(c => c.Id == widget.WidgetId);
        _store.Save();
        widget.Close();
    }

    public void RemoveAll()
    {
        foreach (var widget in _widgets.ToList())
            RemoveWidget(widget);
    }

    /// <summary>Push the latest usage tiles into every open widget.</summary>
    public void UpdateData(IReadOnlyList<ProviderViewModel> tiles)
    {
        _latest = tiles;
        foreach (var widget in _widgets)
            widget.ViewModel.Provider = Find(widget.ViewModel.ProviderId);
    }

    /// <summary>Close all windows without forgetting them (used on app exit).</summary>
    public void CloseAll()
    {
        foreach (var widget in _widgets)
            widget.Close();
        _widgets.Clear();
    }

    private DesktopWidget CreateWindow(WidgetConfig config)
    {
        var vm = new WidgetViewModel(config.ProviderId, UsageViewModelBuilder.DisplayName(config.ProviderId))
        {
            Provider = Find(config.ProviderId),
        };
        var widget = new DesktopWidget(config, vm);
        widget.RemoveRequested += RemoveWidget;
        widget.RefreshRequested += () => RefreshRequested?.Invoke();
        widget.PositionChanged += OnPositionChanged;
        _widgets.Add(widget);
        widget.Show();
        return widget;
    }

    private void OnPositionChanged(DesktopWidget widget)
    {
        var config = _store.Widgets.FirstOrDefault(c => c.Id == widget.WidgetId);
        if (config is null) return;
        config.Left = widget.Left;
        config.Top = widget.Top;
        _store.Save();
    }

    private ProviderViewModel? Find(string providerId) =>
        _latest.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
}
