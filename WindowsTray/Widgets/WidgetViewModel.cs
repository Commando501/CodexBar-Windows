using System.ComponentModel;

namespace CodexBarTray;

/// <summary>
/// Bindable backing model for a single desktop usage widget. Holds the latest
/// <see cref="ProviderViewModel"/> for its provider (or null when that provider
/// is absent from the most recent /usage refresh) and a fallback display name so
/// the widget still labels itself before any data arrives.
/// </summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    public string ProviderId { get; }

    private string _fallbackName;
    public string FallbackName
    {
        get => _fallbackName;
        set { _fallbackName = value; OnChanged(nameof(FallbackName)); OnChanged(nameof(DisplayName)); }
    }

    private ProviderViewModel? _provider;
    public ProviderViewModel? Provider
    {
        get => _provider;
        set
        {
            _provider = value;
            OnChanged(nameof(Provider));
            OnChanged(nameof(HasProvider));
            OnChanged(nameof(NoData));
            OnChanged(nameof(DisplayName));
        }
    }

    public bool HasProvider => _provider is not null;
    public bool NoData => _provider is null;
    public string DisplayName => _provider?.Name is { Length: > 0 } name ? name : _fallbackName;

    public WidgetViewModel(string providerId, string fallbackName)
    {
        ProviderId = providerId;
        _fallbackName = fallbackName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
