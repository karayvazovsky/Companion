using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Companion.Core;

namespace Companion;

public sealed record SettingsDeps(
    SettingsStore Settings,
    SecretStore Secrets,
    MemoryStore Memory,
    SpendTracker Spend,
    Func<string, CancellationToken, Task> ValidateKey,
    Action Applied,
    Action ResetPet);

public partial class SettingsWindow : Window
{
    private readonly SettingsDeps _deps;

    public SettingsWindow(SettingsDeps deps)
    {
        InitializeComponent();
        _deps = deps;
        BrainBox.ItemsSource = Enum.GetValues<BrainKind>();
        EverydayModelBox.ItemsSource = new[] { "claude-sonnet-5", "claude-opus-5-5", "claude-haiku-4-5" };
        HarderModelBox.ItemsSource = new[] { "claude-sonnet-5", "claude-opus-5-5", "claude-haiku-4-5" };
        EverydayBox.ItemsSource = Enum.GetValues<EffortLevel>();
        HarderBox.ItemsSource = Enum.GetValues<EffortLevel>();
        ScaleBox.ItemsSource = new[] { 3, 4, 6 };

        var settings = deps.Settings.Current;
        BrainBox.SelectedItem = settings.Brain;
        EverydayModelBox.Text = settings.SubscriptionEverydayModel;
        HarderModelBox.Text = settings.SubscriptionThinkHarderModel;
        HotkeyBox.Text = settings.Hotkey;
        EverydayBox.SelectedItem = settings.EverydayEffort;
        HarderBox.SelectedItem = settings.ThinkHarderEffort;
        CapBox.Text = settings.MonthlyCapUsd.ToString("0.##", CultureInfo.InvariantCulture);
        ScaleBox.SelectedItem = settings.PetScale;
        AutostartBox.IsChecked = settings.StartWithWindows;
        AutoTeleportBox.IsChecked = settings.AutoTeleport;
        SpendText.Text = $"Estimated spend this month: ${deps.Spend.ThisMonth:0.00} of ${deps.Spend.Cap:0.##}. A cap of 0 stops all questions.";
        KeyStatus.Text = deps.Secrets.HasKey ? "A key is saved (encrypted on this PC)." : "No key saved yet.";

        RefreshFacts();
        deps.Memory.Changed += OnMemoryChanged;
        Closed += (_, _) => deps.Memory.Changed -= OnMemoryChanged;
    }

    private void OnMemoryChanged() => Dispatcher.InvokeAsync(RefreshFacts);

    private void RefreshFacts()
    {
        FactsList.ItemsSource = _deps.Memory.Facts;
        FactBox.Text = "";
    }

    private void OnFactSelected(object sender, SelectionChangedEventArgs e) =>
        FactBox.Text = (FactsList.SelectedItem as Fact)?.Text ?? "";

    private void OnUpdateFact(object sender, RoutedEventArgs e)
    {
        if (FactsList.SelectedItem is Fact fact) _deps.Memory.Update(fact.Id, FactBox.Text);
    }

    private void OnDeleteFact(object sender, RoutedEventArgs e)
    {
        if (FactsList.SelectedItem is Fact fact) _deps.Memory.Remove(fact.Id);
    }

    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (!key.StartsWith("sk-", StringComparison.Ordinal))
        {
            KeyStatus.Text = "That doesn't look like an API key (they start with sk-).";
            return;
        }
        SaveKeyButton.IsEnabled = false;
        KeyStatus.Text = "Checking…";
        try
        {
            await _deps.ValidateKey(key, CancellationToken.None);
            _deps.Secrets.SaveApiKey(key);
            KeyBox.Clear();
            KeyStatus.Text = "The key works and is saved (encrypted on this PC).";
            _deps.Applied();
        }
        catch (ClaudeException ex)
        {
            KeyStatus.Text = ex.Kind switch
            {
                ClaudeErrorKind.Unauthorized => "That key was rejected.",
                ClaudeErrorKind.NoAccess => Brain.NoAccessMessage,
                _ => $"Couldn't check the key right now ({ex.Kind}).",
            };
        }
        finally
        {
            SaveKeyButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!HotkeyService.TryParse(HotkeyBox.Text, out _, out _))
        {
            MessageBox.Show(this, "The hotkey must look like Ctrl+Alt+Space: at least one of Ctrl/Alt/Shift/Win plus one key.", "Companion");
            return;
        }
        // Accept both "12.50" and "12,50" whatever the Windows number format is.
        var capText = CapBox.Text.Trim().Replace(',', '.');
        if (!decimal.TryParse(capText, NumberStyles.Number, CultureInfo.InvariantCulture, out var cap) || cap < 0)
        {
            MessageBox.Show(this, "The monthly cap must be a number like 30 or 12.50.", "Companion");
            return;
        }

        var settings = _deps.Settings.Current;
        settings.Brain = (BrainKind)BrainBox.SelectedItem;
        settings.SubscriptionEverydayModel = EverydayModelBox.Text.Trim();
        settings.SubscriptionThinkHarderModel = HarderModelBox.Text.Trim();
        settings.Hotkey = HotkeyBox.Text.Trim();
        settings.EverydayEffort = (EffortLevel)EverydayBox.SelectedItem;
        settings.ThinkHarderEffort = (EffortLevel)HarderBox.SelectedItem;
        settings.MonthlyCapUsd = cap;
        settings.PetScale = (int)ScaleBox.SelectedItem;
        settings.StartWithWindows = AutostartBox.IsChecked == true;
        settings.AutoTeleport = AutoTeleportBox.IsChecked == true;
        _deps.Settings.Save();
        _deps.Applied();
        Close();
    }

    private void OnResetPet(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Reset the pet? It becomes an egg again and forgets its name. Your memory facts and API key stay.",
            "Companion", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _deps.ResetPet();
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
