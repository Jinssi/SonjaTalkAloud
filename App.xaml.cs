using System.Threading;
using System.Windows;
using Sonja.ReadAloud.Models;
using Sonja.ReadAloud.Services;

namespace Sonja.ReadAloud;

public partial class App : System.Windows.Application
{
	private const string SingleInstanceName = "Local\\Sonja.ReadAloud.SingleInstance";

	private Mutex? _singleInstanceMutex;
	private MainWindow? _mainWindow;
	private TrayIconService? _trayIcon;
	private GlobalHotkeyService? _hotkeyService;
	private SelectedTextService? _selectedTextService;
	private SpeechService? _speechService;
	private SettingsStore? _settingsStore;
	private StartupService? _startupService;
	private AppSettings _settings = new();
	private readonly SemaphoreSlim _selectionGate = new(1, 1);
	private bool _isExiting;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		_singleInstanceMutex = new Mutex(true, SingleInstanceName, out var isFirstInstance);
		if (!isFirstInstance)
		{
			System.Windows.MessageBox.Show(
				"Sonja Read Aloud is already running. Open it from the notification area.",
				"Sonja Read Aloud",
				MessageBoxButton.OK,
				MessageBoxImage.Information);
			Shutdown();
			return;
		}

		try
		{
			var environment = DotEnvLoader.Load();
			var speechOptions = SpeechConnectionOptions.FromEnvironment(environment);

			_settingsStore = new SettingsStore();
			_startupService = new StartupService();
			_settings = _settingsStore.Load();
			_selectedTextService = new SelectedTextService();
			_speechService = new SpeechService(speechOptions);

			_mainWindow = new MainWindow(
				_settings,
				speechOptions,
				_speechService,
				ApplySettingsAsync,
				TestVoiceAsync);
			MainWindow = _mainWindow;
			_mainWindow.Closing += MainWindow_Closing;
			_mainWindow.Show();

			_hotkeyService = new GlobalHotkeyService(_mainWindow);
			_hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;

			_trayIcon = new TrayIconService(
				ShowSettings,
				() => _ = TestVoiceFromTrayAsync(),
				ExitApplication);

			var registration = _hotkeyService.TryRegister(_settings.Hotkey);
			if (!registration.Success)
			{
				_mainWindow.SetStatus(registration.Message, StatusLevel.Error);
				_trayIcon.ShowNotification("Shortcut unavailable", registration.Message, NotificationKind.Error);
			}
			else if (!speechOptions.IsConfigured)
			{
				const string message = "No speech provider is configured. Add Azure Speech or Azure OpenAI TTS settings to .env.";
				_mainWindow.SetStatus(message, StatusLevel.Error);
				_trayIcon.ShowNotification("Speech setup needed", message, NotificationKind.Warning);
			}
			else
			{
				_mainWindow.SetStatus($"Ready · select text and press {_settings.Hotkey.DisplayName}", StatusLevel.Ready);
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(
				$"Sonja Read Aloud could not start.\n\n{ex.Message}",
				"Startup error",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
			Shutdown();
		}
	}

	private async void HotkeyService_HotkeyPressed(object? sender, EventArgs e)
	{
		if (_speechService is null || _selectedTextService is null || _mainWindow is null)
		{
			return;
		}

		if (_speechService.IsSpeaking)
		{
			await _speechService.StopAsync();
			_mainWindow.SetStatus($"Stopped · press {_settings.Hotkey.DisplayName} to read", StatusLevel.Ready);
			return;
		}

		if (!await _selectionGate.WaitAsync(0))
		{
			return;
		}

		try
		{
			if (!_speechService.IsConfigured)
			{
				const string message = "Azure speech credentials are missing or invalid in .env.";
				_mainWindow.SetStatus(message, StatusLevel.Error);
				_trayIcon?.ShowNotification("Speech not configured", message, NotificationKind.Error);
				return;
			}

			_mainWindow.SetStatus("Reading the current selection…", StatusLevel.Working);
			var selection = await _selectedTextService.CaptureAsync();
			if (!selection.Success)
			{
				_mainWindow.SetStatus(selection.Message, StatusLevel.Warning);
				if (_settings.ShowNotifications)
				{
					_trayIcon?.ShowNotification("Nothing to read", selection.Message, NotificationKind.Warning);
				}

				return;
			}

			_mainWindow.SetStatus($"Speaking {selection.Text!.Length:N0} characters · press {_settings.Hotkey.DisplayName} to stop", StatusLevel.Speaking);
			_ = SpeakSelectionAsync(selection.Text);
		}
		catch (Exception ex)
		{
			_mainWindow.SetStatus($"Could not read the selection: {ex.Message}", StatusLevel.Error);
			_trayIcon?.ShowNotification("Read aloud failed", ex.Message, NotificationKind.Error);
		}
		finally
		{
			_selectionGate.Release();
		}
	}

	private async Task SpeakSelectionAsync(string text)
	{
		if (_speechService is null || _mainWindow is null)
		{
			return;
		}

		var result = await _speechService.SpeakAsync(text, _settings.VoiceName, _settings.RatePercent);
		if (result.Success)
		{
			_mainWindow.SetStatus($"Ready · select text and press {_settings.Hotkey.DisplayName}", StatusLevel.Ready);
		}
		else if (!result.WasStopped)
		{
			_mainWindow.SetStatus(result.Message, StatusLevel.Error);
			_trayIcon?.ShowNotification("Speech failed", result.Message, NotificationKind.Error);
		}
	}

	private async Task<OperationResult> ApplySettingsAsync(AppSettings proposed)
	{
		if (_hotkeyService is null || _settingsStore is null || _startupService is null)
		{
			return OperationResult.Fail("The app is not ready yet.");
		}

		var previous = _settings.Clone();
		var hotkeyResult = _hotkeyService.TryRegister(proposed.Hotkey);
		if (!hotkeyResult.Success)
		{
			return hotkeyResult;
		}

		var startupResult = _startupService.Configure(proposed.StartWithWindows);
		if (!startupResult.Success)
		{
			_hotkeyService.TryRegister(previous.Hotkey);
			return startupResult;
		}

		try
		{
			await _settingsStore.SaveAsync(proposed);
			_settings = proposed.Clone();
			_mainWindow?.SetStatus($"Saved · select text and press {_settings.Hotkey.DisplayName}", StatusLevel.Ready);
			return OperationResult.Ok("Settings saved.");
		}
		catch (Exception ex)
		{
			_hotkeyService.TryRegister(previous.Hotkey);
			_startupService.Configure(previous.StartWithWindows);
			return OperationResult.Fail($"Settings could not be saved: {ex.Message}");
		}
	}

	private Task<OperationResult> TestVoiceAsync(AppSettings proposed)
	{
		return _speechService is null
			? Task.FromResult(OperationResult.Fail("The speech service is not ready."))
			: _speechService.SpeakAsync(
				"Sonja Read Aloud is ready. Select text in any application, then press your shortcut.",
				proposed.VoiceName,
				proposed.RatePercent);
	}

	private async Task TestVoiceFromTrayAsync()
	{
		var result = await TestVoiceAsync(_settings);
		if (!result.Success && !result.WasStopped)
		{
			_trayIcon?.ShowNotification("Voice test failed", result.Message, NotificationKind.Error);
		}
	}

	private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		if (_isExiting)
		{
			return;
		}

		e.Cancel = true;
		_mainWindow?.Hide();
		_trayIcon?.ShowNotification(
			"Sonja is still running",
			$"Select text and press {_settings.Hotkey.DisplayName}. Open settings from the notification area.",
			NotificationKind.Info);
	}

	private void ShowSettings()
	{
		if (_mainWindow is null)
		{
			return;
		}

		_mainWindow.Show();
		if (_mainWindow.WindowState == WindowState.Minimized)
		{
			_mainWindow.WindowState = WindowState.Normal;
		}

		_mainWindow.Activate();
		_mainWindow.Topmost = true;
		_mainWindow.Topmost = false;
		_mainWindow.Focus();
	}

	private void ExitApplication()
	{
		_isExiting = true;
		_hotkeyService?.Dispose();
		_trayIcon?.Dispose();
		_mainWindow?.Close();
		Shutdown();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_hotkeyService?.Dispose();
		_trayIcon?.Dispose();
		_speechService?.Dispose();
		_singleInstanceMutex?.Dispose();
		_selectionGate.Dispose();
		base.OnExit(e);
	}
}

