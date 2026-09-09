using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sonja.ReadAloud.Models;
using Sonja.ReadAloud.Services;
using MediaColor = System.Windows.Media.Color;

namespace Sonja.ReadAloud;

public partial class MainWindow : Window
{
    private SpeechConnectionOptions _speechOptions;
    private SpeechService _speechService;
    private readonly Func<AppSettings, Task<OperationResult>> _applySettings;
    private readonly Func<AppSettings, Task<OperationResult>> _testVoice;
    private readonly Func<SpeechCredentials, Task<OperationResult>> _saveCredentials;
    private AppSettings _currentSettings;
    private HotkeyDefinition _pendingHotkey;
    private bool _hasLoadedVoices;

    public MainWindow(
        AppSettings settings,
        SpeechConnectionOptions speechOptions,
        SpeechService speechService,
        Func<AppSettings, Task<OperationResult>> applySettings,
        Func<AppSettings, Task<OperationResult>> testVoice,
        Func<SpeechCredentials, Task<OperationResult>> saveCredentials)
    {
        InitializeComponent();

        Icon = LoadWindowIcon();

        _currentSettings = settings.Clone();
        _pendingHotkey = settings.Hotkey.Clone();
        _speechOptions = speechOptions;
        _speechService = speechService;
        _applySettings = applySettings;
        _testVoice = testVoice;
        _saveCredentials = saveCredentials;

        HotkeyTextBox.Text = _pendingHotkey.DisplayName;
        SpeechRegionBox.Text = speechOptions.Region ?? string.Empty;
        VoiceComboBox.Text = speechOptions.GetCompatibleVoice(settings.VoiceName);
        RateSlider.Value = settings.RatePercent;
        StyleComboBox.ItemsSource = SpeakingStyleOption.Catalog;
        StyleComboBox.SelectedItem = SpeakingStyleOption.Catalog.FirstOrDefault(
            option => option.Value.Equals(settings.Style, StringComparison.OrdinalIgnoreCase)) ?? SpeakingStyleOption.Natural;
        StyleDegreeSlider.Value = settings.StyleDegree;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        ShowNotificationsCheckBox.IsChecked = settings.ShowNotifications;
        ConnectionText.Text = speechOptions.ConnectionSummary;
        UpdateStyleHint();
        UpdateConnectionHint();
    }

    public void UpdateSpeechProvider(SpeechConnectionOptions options, SpeechService service)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateSpeechProvider(options, service));
            return;
        }

        _speechOptions = options;
        _speechService = service;
        ConnectionText.Text = options.ConnectionSummary;
        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            SpeechRegionBox.Text = options.Region;
        }

        _hasLoadedVoices = false;
        _ = LoadVoicesAsync();
    }

    private static System.Windows.Media.ImageSource? LoadWindowIcon()
    {
        try
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Sonja.ReadAloud.Assets.sonja.ico");
            if (stream is not null)
            {
                return System.Windows.Media.Imaging.BitmapFrame.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            }
        }
        catch
        {
            // Keep the default window icon if the embedded resource is unavailable.
        }

        return null;
    }

    public void SetStatus(string message, StatusLevel level)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message, level));
            return;
        }

        StatusText.Text = message;
        var color = level switch
        {
            StatusLevel.Ready => MediaColor.FromRgb(103, 232, 165),
            StatusLevel.Working => MediaColor.FromRgb(255, 140, 197),
            StatusLevel.Speaking => MediaColor.FromRgb(255, 79, 163),
            StatusLevel.Warning => MediaColor.FromRgb(251, 191, 36),
            StatusLevel.Error => MediaColor.FromRgb(248, 113, 113),
            _ => MediaColor.FromRgb(198, 163, 182)
        };
        StatusDot.Fill = new SolidColorBrush(color);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_hasLoadedVoices && _speechOptions.IsConfigured)
        {
            await LoadVoicesAsync();
        }
    }

    private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyTextBox.Text = "Press a shortcut…";
        HotkeyHint.Text = "Waiting for a key combination…";
        HotkeyHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(183, 163, 255));
    }

    private void HotkeyTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyTextBox.Text = _pendingHotkey.DisplayName;
        HotkeyHint.Text = "Shortcut is active system-wide after saving.";
        HotkeyHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(141, 152, 184));
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyDefinition.IsModifierKey(key))
        {
            return;
        }

        var candidate = HotkeyDefinition.FromKeyboard(key, Keyboard.Modifiers);
        if (!candidate.IsValid)
        {
            HotkeyHint.Text = "Include at least one modifier: Ctrl, Alt, Shift, or Windows.";
            HotkeyHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(251, 191, 36));
            return;
        }

        _pendingHotkey = candidate;
        HotkeyTextBox.Text = candidate.DisplayName;
        HotkeyHint.Text = "New shortcut captured. Save settings to activate it.";
        HotkeyHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(103, 232, 165));
        Keyboard.ClearFocus();
    }

    private void RateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RateValueText is not null)
        {
            RateValueText.Text = $"{(int)e.NewValue:+0;-0;0}%";
        }
    }

    private void StyleDegreeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (StyleDegreeValueText is not null)
        {
            StyleDegreeValueText.Text = $"{e.NewValue:0.0}×";
        }
    }

    private void StyleComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateStyleHint();
    }

    private void VoiceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateStyleHint();
    }

    private void UpdateStyleHint()
    {
        if (StyleHint is null || IntensityGrid is null)
        {
            return;
        }

        var styleActive = !string.IsNullOrEmpty(GetSelectedStyle());
        IntensityGrid.IsEnabled = styleActive;

        if (!styleActive)
        {
            StyleHint.Text = "Natural reads the text with no added emotion. Choose an attitude to add expression.";
            StyleHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(185, 139, 166));
            return;
        }

        if (SpeechService.SupportsExpressiveStyles(GetSelectedVoiceName()))
        {
            StyleHint.Text = "Attitude will be applied by Azure neural speaking styles.";
            StyleHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(103, 232, 165));
        }
        else
        {
            StyleHint.Text = "This voice may ignore attitude. Pick a · styles voice such as Aria, Jenny or Sara for the clearest effect.";
            StyleHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(251, 191, 36));
        }
    }

    private async void RefreshVoicesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadVoicesAsync();
    }

    private async Task LoadVoicesAsync()
    {
        RefreshVoicesButton.IsEnabled = false;
        var existingVoice = GetSelectedVoiceName();
        SetStatus("Loading cloud voices…", StatusLevel.Working);

        try
        {
            var result = await _speechService.GetVoicesAsync();
            if (!result.Success)
            {
                SetStatus(result.Message, StatusLevel.Error);
                return;
            }

            VoiceComboBox.ItemsSource = result.Voices;
            var matchingVoice = result.Voices.FirstOrDefault(
                voice => voice.ShortName.Equals(existingVoice, StringComparison.OrdinalIgnoreCase));
            if (matchingVoice is not null)
            {
                VoiceComboBox.SelectedItem = matchingVoice;
            }
            else if (result.Voices.Count > 0)
            {
                VoiceComboBox.SelectedIndex = 0;
            }
            _hasLoadedVoices = true;
            SetStatus($"Ready · select text and press {_currentSettings.Hotkey.DisplayName}", StatusLevel.Ready);
        }
        finally
        {
            RefreshVoicesButton.IsEnabled = true;
        }
    }

    private async void TestVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        TestVoiceButton.IsEnabled = false;
        SetStatus("Testing the selected voice…", StatusLevel.Speaking);
        try
        {
            var result = await _testVoice(BuildProposedSettings());
            SetStatus(
                result.Success ? "Voice test complete." : result.Message,
                result.Success ? StatusLevel.Ready : StatusLevel.Error);
        }
        finally
        {
            TestVoiceButton.IsEnabled = true;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SetStatus("Saving settings…", StatusLevel.Working);
        try
        {
            var proposed = BuildProposedSettings();
            var result = await _applySettings(proposed);
            if (result.Success)
            {
                _currentSettings = proposed.Clone();
                HotkeyHint.Text = "Shortcut is active system-wide.";
                HotkeyHint.Foreground = new SolidColorBrush(MediaColor.FromRgb(103, 232, 165));
            }
            else
            {
                SetStatus(result.Message, StatusLevel.Error);
            }
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void SaveConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var key = SpeechKeyBox.Password.Trim();
        var region = SpeechRegionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            SetConnectionHint("Enter your Azure Speech key.", StatusLevel.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            SetConnectionHint("Enter your Azure Speech region, e.g. westeurope.", StatusLevel.Warning);
            return;
        }

        SaveConnectionButton.IsEnabled = false;
        SetConnectionHint("Connecting…", StatusLevel.Working);
        SetStatus("Connecting to Azure Speech…", StatusLevel.Working);
        try
        {
            var result = await _saveCredentials(new SpeechCredentials(key, region, null));
            if (result.Success)
            {
                SpeechKeyBox.Clear();
                SetConnectionHint("Connected. Your key is saved securely for this Windows account.", StatusLevel.Ready);
            }
            else
            {
                SetConnectionHint(result.Message, StatusLevel.Error);
                SetStatus(result.Message, StatusLevel.Error);
            }
        }
        finally
        {
            SaveConnectionButton.IsEnabled = true;
        }
    }

    private void UpdateConnectionHint()
    {
        if (_speechOptions.IsAzureSpeechConfigured)
        {
            SetConnectionHint($"Connected · {_speechOptions.ConnectionSummary}", StatusLevel.Ready);
        }
        else
        {
            SetConnectionHint("Paste your key and region, then Save & connect. Example region: westeurope.", StatusLevel.Ready);
        }
    }

    private void SetConnectionHint(string message, StatusLevel level)
    {
        ConnectionHint.Text = message;
        var color = level switch
        {
            StatusLevel.Ready => MediaColor.FromRgb(103, 232, 165),
            StatusLevel.Working => MediaColor.FromRgb(183, 163, 255),
            StatusLevel.Warning => MediaColor.FromRgb(251, 191, 36),
            StatusLevel.Error => MediaColor.FromRgb(248, 113, 113),
            _ => MediaColor.FromRgb(185, 139, 166)
        };
        ConnectionHint.Foreground = new SolidColorBrush(color);
    }

    private AppSettings BuildProposedSettings()
    {
        return new AppSettings
        {
            Hotkey = _pendingHotkey.Clone(),
            VoiceName = GetSelectedVoiceName(),
            RatePercent = (int)RateSlider.Value,
            Style = GetSelectedStyle(),
            StyleDegree = StyleDegreeSlider.Value,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            ShowNotifications = ShowNotificationsCheckBox.IsChecked == true
        };
    }

    private string GetSelectedStyle()
    {
        return StyleComboBox.SelectedItem is SpeakingStyleOption option ? option.Value : string.Empty;
    }

    private string GetSelectedVoiceName()
    {
        if (VoiceComboBox.SelectedItem is VoiceOption voice)
        {
            return voice.ShortName;
        }

        return string.IsNullOrWhiteSpace(VoiceComboBox.Text)
            ? AppSettings.DefaultVoiceName
            : VoiceComboBox.Text.Trim();
    }
}