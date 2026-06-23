using System.Windows;
using System.Windows.Controls;

namespace CodexBarTray;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly Action _onChanged;

    public SettingsWindow(ConfigService config, Action onChanged)
    {
        _config = config;
        _onChanged = onChanged;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            GlobalStatus.Text = "Loading providers…";
            var providers = await _config.GetProvidersAsync();
            var keyPresence = await _config.GetKeyPresenceAsync();

            var rows = providers.Select(p => new ProviderSettingRow
            {
                Id = p.Provider,
                DisplayName = p.DisplayName,
                AuthKind = AuthClassifier.Classify(p.Provider),
                Enabled = p.Enabled,
                HasKey = keyPresence.TryGetValue(p.Provider, out var has) && has,
            }).ToList();

            ProviderList.ItemsSource = rows;
            GlobalStatus.Text = $"{rows.Count} providers";
        }
        catch (Exception ex)
        {
            GlobalStatus.Text = $"Failed to load: {ex.Message}";
        }
    }

    private async void OnEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ProviderSettingRow row } checkBox) return;
        var enabled = checkBox.IsChecked == true;
        try
        {
            await _config.SetEnabledAsync(row.Id, enabled);
            row.Enabled = enabled;
            row.Status = enabled ? "enabled" : "disabled";
            _onChanged();
        }
        catch (Exception ex)
        {
            // Revert the visual toggle on failure.
            checkBox.IsChecked = row.Enabled;
            row.Status = $"failed: {ex.Message}";
        }
    }

    private async void OnSaveKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderSettingRow row } button) return;
        if (button.CommandParameter is not PasswordBox keyBox) return;

        var key = keyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            row.Status = "enter a key first";
            return;
        }

        try
        {
            await _config.SetApiKeyAsync(row.Id, key);
            keyBox.Clear();
            row.HasKey = true;
            row.Enabled = true; // set-api-key auto-enables
            row.Status = "key saved — provider enabled";
            _onChanged();
        }
        catch (Exception ex)
        {
            row.Status = $"failed: {ex.Message}";
        }
    }
}
