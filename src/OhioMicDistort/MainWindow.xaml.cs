using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OhioMicDistort.Models;
using OhioMicDistort.Services;

namespace OhioMicDistort;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly AudioDeviceService _audioDeviceService = new();
    private readonly EqualizerApoService _equalizerApoService = new();
    private readonly GlobalHotkeyService _hotkeyService = new();

    private AppSettings _settings = new();
    private bool _isDistortionActive;
    private bool _isListeningForHotkey;
    private bool _isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _audioDeviceService.InputLevelChanged += OnMicInputLevelChanged;
        _hotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();

        IntensitySlider.Value = Math.Clamp(_settings.IntensityPercent, 0.0, 100.0);
        ToasterClipCheckBox.IsChecked = _settings.EnableToasterClipEq;
        UpdateIntensityLabel();

        _hotkeyService.Initialize(this);
        RegisterCurrentHotkey();

        LoadCaptureDevices();
        CheckEqualizerApoStatus();

        _isInitialized = true;
        SyncEqualizerApoState();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Always bypass distortion when closing the app so the user's mic returns to normal
        try
        {
            if (_equalizerApoService.HasWriteAccess(_settings.EqualizerApoConfigDir))
            {
                _equalizerApoService.ApplyDistortionState(
                    _settings.EqualizerApoConfigDir,
                    MicComboBox.SelectedItem as MicDeviceInfo,
                    isDistortionActive: false,
                    _settings.IntensityPercent,
                    _settings.EnableToasterClipEq);
            }
        }
        catch
        {
            // Ignore shutdown errors
        }

        _settingsService.Save(_settings);
        _hotkeyService.Dispose();
        _audioDeviceService.Dispose();
    }

    private void LoadCaptureDevices()
    {
        var devices = _audioDeviceService.GetCaptureDevices();
        MicComboBox.ItemsSource = devices;

        if (devices.Count == 0)
        {
            EndpointGuidText.Text = "No active Microphone Capture devices found.";
            return;
        }

        MicDeviceInfo? selected = null;
        if (!string.IsNullOrWhiteSpace(_settings.SelectedMicEndpointGuid))
        {
            selected = devices.FirstOrDefault(d =>
                string.Equals(d.EndpointGuid, _settings.SelectedMicEndpointGuid, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= devices.FirstOrDefault(d => d.IsDefault) ?? devices[0];
        MicComboBox.SelectedItem = selected;
    }

    private void CheckEqualizerApoStatus()
    {
        bool installed = _equalizerApoService.IsEqualizerApoInstalled(_settings.EqualizerApoConfigDir);
        if (!installed)
        {
            ApoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            ApoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            ApoStatusText.Text = "Equalizer APO Not Found";
            PermissionBanner.Visibility = Visibility.Collapsed;
            StatusFooterText.Text = $"Install Equalizer APO to {_settings.EqualizerApoConfigDir}";
            return;
        }

        bool writable = _equalizerApoService.HasWriteAccess(_settings.EqualizerApoConfigDir);
        if (!writable)
        {
            ApoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15));
            ApoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 230, 138));
            ApoStatusText.Text = "Needs Write Permission";
            PermissionBanner.Visibility = Visibility.Visible;
            StatusFooterText.Text = "Click 'Grant Write Access' above so rules can update.";
            return;
        }

        ApoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
        ApoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(110, 231, 183));
        ApoStatusText.Text = "Equalizer APO Connected";
        PermissionBanner.Visibility = Visibility.Collapsed;
    }

    private void SyncEqualizerApoState()
    {
        if (!_isInitialized)
        {
            return;
        }

        var selectedMic = MicComboBox.SelectedItem as MicDeviceInfo;
        string preview = EqualizerApoService.BuildIncludeFileContent(
            selectedMic,
            _isDistortionActive,
            _settings.IntensityPercent,
            _settings.EnableToasterClipEq);

        ApoRulePreviewBox.Text = preview;

        if (!_equalizerApoService.IsEqualizerApoInstalled(_settings.EqualizerApoConfigDir))
        {
            return;
        }

        if (!_equalizerApoService.HasWriteAccess(_settings.EqualizerApoConfigDir))
        {
            PermissionBanner.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _equalizerApoService.ApplyDistortionState(
                _settings.EqualizerApoConfigDir,
                selectedMic,
                _isDistortionActive,
                _settings.IntensityPercent,
                _settings.EnableToasterClipEq);

            StatusFooterText.Text = _isDistortionActive
                ? $"Applied to {selectedMic?.FriendlyName ?? "Mic"} ({DateTime.Now:HH:mm:ss})"
                : $"Bypassed — Mic Clean ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Write Error: {ex.Message}";
        }
    }

    private void ToggleDistortion()
    {
        _isDistortionActive = !_isDistortionActive;
        UpdateToggleButtonVisuals();
        SyncEqualizerApoState();
    }

    private void UpdateToggleButtonVisuals()
    {
        if (_isDistortionActive)
        {
            DistortToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            DistortToggleButton.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            DistortToggleButton.Content = $"🔊 DISTORTION ACTIVE — EARRAPE ON  (Press {_settings.HotkeyDisplay} to Stop)";
        }
        else
        {
            DistortToggleButton.Background = new SolidColorBrush(Color.FromRgb(38, 43, 56));
            DistortToggleButton.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
            DistortToggleButton.Content = $"DISTORTION: OFF  (Click or Press {_settings.HotkeyDisplay} to Activate)";
        }
    }

    private void UpdateIntensityLabel()
    {
        double intensity = _settings.IntensityPercent;
        double preampDb = Math.Round((intensity / 100.0) * 60.0, 1);
        IntensityValueText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{intensity:F0}%  (+{preampDb:F1} dB Preamp)");
    }

    private void RegisterCurrentHotkey()
    {
        bool ok = _hotkeyService.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
        BindHotkeyButton.Content = ok
            ? $"{_settings.HotkeyDisplay} (Click to Change)"
            : $"{_settings.HotkeyDisplay} (In Use - Click to Rebind)";
        UpdateToggleButtonVisuals();
    }

    private void OnGlobalHotkeyPressed()
    {
        if (!_isListeningForHotkey)
        {
            ToggleDistortion();
        }
    }

    private void OnMicInputLevelChanged(double normalizedPeak)
    {
        Dispatcher.BeginInvoke(() =>
        {
            double percent = Math.Clamp(normalizedPeak * 100.0, 0.0, 100.0);
            MicLevelBar.Value = percent;
            MicLevelBar.Foreground = _isDistortionActive || percent > 85.0
                ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                : new SolidColorBrush(Color.FromRgb(16, 185, 129));
        });
    }

    private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicComboBox.SelectedItem is MicDeviceInfo mic)
        {
            _settings.SelectedMicEndpointGuid = mic.EndpointGuid;
            EndpointGuidText.Text = $"Device: {mic.EndpointGuid}";
            _audioDeviceService.StartInputLevelMonitor(mic.FullId);
            _settingsService.Save(_settings);
            SyncEqualizerApoState();
        }
    }

    private void RefreshMicsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadCaptureDevices();
        CheckEqualizerApoStatus();
        SyncEqualizerApoState();
    }

    private void OpenApoConfiguratorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_equalizerApoService.LaunchEqualizerApoConfigurator(_settings.EqualizerApoConfigDir))
        {
            MessageBox.Show(
                this,
                "Could not find Equalizer APO's DeviceSelector.exe or Configurator.exe.\n\n" +
                "Please ensure Equalizer APO is installed in C:\\Program Files\\EqualizerAPO.",
                "Equalizer APO Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void GrantPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerApoService.GrantConfigFolderWriteAccess(_settings.EqualizerApoConfigDir))
        {
            CheckEqualizerApoStatus();
            SyncEqualizerApoState();
        }
        else
        {
            MessageBox.Show(
                this,
                "Write access was not granted. You can also run Ohio Mic Distort as Administrator.",
                "Permission Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DistortToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleDistortion();
    }

    private void IntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.IntensityPercent = Math.Round(e.NewValue, 0);
        if (_isInitialized)
        {
            UpdateIntensityLabel();
            _settingsService.Save(_settings);
            SyncEqualizerApoState();
        }
    }

    private void ToasterClipCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _settings.EnableToasterClipEq = ToasterClipCheckBox.IsChecked == true;
        if (_isInitialized)
        {
            _settingsService.Save(_settings);
            SyncEqualizerApoState();
        }
    }

    private void BindHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _isListeningForHotkey = true;
        _hotkeyService.Unregister();
        BindHotkeyButton.Content = "Press any key... (Esc to cancel)";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isListeningForHotkey)
        {
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _isListeningForHotkey = false;
            RegisterCurrentHotkey();
            return;
        }

        // Ignore standalone modifier key presses while waiting for the main key
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        uint modifiers = GlobalHotkeyService.MOD_NONE;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= GlobalHotkeyService.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= GlobalHotkeyService.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= GlobalHotkeyService.MOD_ALT;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyVirtualKey = vk;
            _settings.HotkeyDisplay = GlobalHotkeyService.FormatHotkey(modifiers, key);
            _settingsService.Save(_settings);
        }

        _isListeningForHotkey = false;
        RegisterCurrentHotkey();
    }
}
