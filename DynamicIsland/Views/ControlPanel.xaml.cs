using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using Windows.Devices.Radios;

namespace DynamicIsland.Views;

/// <summary>Функциональная панель: крупные плитки-переключатели, громкость/яркость, питание.</summary>
public partial class ControlPanel : UserControl
{
    private bool _suppressVolume;
    private bool _suppressBrightness;
    private bool _wifiOn, _btOn, _vpnOn, _darkTheme = true;
    private readonly DispatcherTimer _batteryTimer;

    public ControlPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await RefreshStates();
            RefreshBattery();
        };
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _batteryTimer.Tick += (_, _) => RefreshBattery();
        _batteryTimer.Start();
        Unloaded += (_, _) => _batteryTimer.Stop();
    }

    private async Task RefreshStates()
    {
        // Громкость
        var vol = SystemControlService.GetVolumeLevel();
        if (vol >= 0)
        {
            _suppressVolume = true;
            VolumeSlider.Value = vol;
            VolumeText.Text = $"{vol:0}%";
            _suppressVolume = false;
            UpdateMuteIcon(SystemControlService.GetMute());
        }
        else VolumeSlider.IsEnabled = false;

        // Яркость
        if (SystemControlService.IsBrightnessSupported())
        {
            var br = SystemControlService.GetBrightness();
            if (br >= 0)
            {
                _suppressBrightness = true;
                BrightnessSlider.Value = br;
                BrightnessText.Text = $"{br:0}%";
                _suppressBrightness = false;
            }
        }
        else
        {
            BrightnessSlider.IsEnabled = false;
            BrightnessText.Text = "н/д";
        }

        // Радио
        var wifi = await SystemControlService.GetRadioStateAsync(RadioKind.WiFi);
        if (wifi.HasValue) SetTile(WifiChip, WifiIcon, WifiStatus, _wifiOn = wifi.Value, "Включён", "Выключен");
        var bt = await SystemControlService.GetRadioStateAsync(RadioKind.Bluetooth);
        if (bt.HasValue) SetTile(BtChip, BtIcon, BtStatus, _btOn = bt.Value, "Включён", "Выключен");

        // Тема
        _darkTheme = !SystemControlService.IsLightTheme();
        SetTile(ThemeChip, ThemeIcon, ThemeStatus, _darkTheme, "Включена", "Выключена");
    }

    // ---------- Плитки-переключатели ----------

    /// <summary>Вкл: чип акцентный, иконка белая. Выкл: чип серый, иконка гаснет.</summary>
    private static void SetTile(Border chip, System.Windows.Shapes.Path icon, TextBlock status,
                                bool on, string onText, string offText)
    {
        chip.Background = on
            ? (Brush)Application.Current.FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        icon.Fill = on
            ? Brushes.White
            : (Brush)Application.Current.FindResource("TextDimBrush");
        status.Text = on ? onText : offText;
    }

    private async void Wifi_Click(object s, RoutedEventArgs e)
    {
        var target = !_wifiOn;
        SetTile(WifiChip, WifiIcon, WifiStatus, target, "Включён", "Выключен"); // оптимистично
        if (await SystemControlService.SetRadioStateAsync(RadioKind.WiFi, target))
            _wifiOn = target;
        else
        {
            SetTile(WifiChip, WifiIcon, WifiStatus, _wifiOn, "Включён", "Выключен");
            SystemControlService.OpenSettings("ms-settings:network-wifi");
        }
    }

    private async void Bt_Click(object s, RoutedEventArgs e)
    {
        var target = !_btOn;
        SetTile(BtChip, BtIcon, BtStatus, target, "Включён", "Выключен");
        if (await SystemControlService.SetRadioStateAsync(RadioKind.Bluetooth, target))
            _btOn = target;
        else
        {
            SetTile(BtChip, BtIcon, BtStatus, _btOn, "Включён", "Выключен");
            SystemControlService.OpenSettings("ms-settings:bluetooth");
        }
    }

    private void Vpn_Click(object s, RoutedEventArgs e)
    {
        _vpnOn = !_vpnOn;
        SetTile(VpnChip, VpnIcon, VpnStatus, _vpnOn, "Включён", "Выключен");
        SystemControlService.OpenSettings("ms-settings:network-vpn");
    }

    private void Theme_Click(object s, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        SetTile(ThemeChip, ThemeIcon, ThemeStatus, _darkTheme, "Включена", "Выключена");
        SystemControlService.SetLightTheme(!_darkTheme);
    }

    private void WifiSettings_Click(object s, RoutedEventArgs e) { e.Handled = true; SystemControlService.OpenSettings("ms-settings:network-wifi"); }
    private void BtSettings_Click(object s, RoutedEventArgs e) { e.Handled = true; SystemControlService.OpenSettings("ms-settings:bluetooth"); }
    private void VpnSettings_Click(object s, RoutedEventArgs e) { e.Handled = true; SystemControlService.OpenSettings("ms-settings:network-vpn"); }
    private void ThemeSettings_Click(object s, RoutedEventArgs e) { e.Handled = true; SystemControlService.OpenSettings("ms-settings:colors"); }
    private void Battery_Click(object s, RoutedEventArgs e) => SystemControlService.OpenSettings("ms-settings:batterysaver");

    // ---------- Батарея ----------

    private void RefreshBattery()
    {
        if (!GetSystemPowerStatus(out var ps) || (ps.BatteryFlag & 128) != 0)
        {
            BatteryStatus.Text = "Питание от сети";
            BatteryIcon.Fill = (Brush)FindResource("TextDimBrush");
            return;
        }

        bool charging = (ps.BatteryFlag & 8) != 0 || ps.ACLineStatus == 1;
        int pct = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1;

        string key = charging ? "ic_battery_charging"
            : pct < 0 ? "ic_battery_full"
            : pct <= 20 ? "ic_battery_low"
            : pct <= 60 ? "ic_battery_medium"
            : "ic_battery_full";
        if (Application.Current.FindResource(key) is Geometry g)
            BatteryIcon.Data = g;

        BatteryIcon.Fill = charging
            ? (Brush)Application.Current.FindResource("GreenBrush")
            : (Brush)FindResource("TextPriBrush");
        BatteryStatus.Text = pct >= 0
            ? (charging ? $"Зарядка · {pct}%" : $"Осталось {pct}%")
            : "Питание от сети";
    }

    // ---------- Громкость / яркость ----------

    private void UpdateMuteIcon(bool muted)
    {
        if (Application.Current.FindResource(muted ? "ic_volume_x" : "ic_volume_2") is Geometry g)
            MuteIcon.Data = g;
    }

    private void Volume_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolume) return;
        SystemControlService.SetVolumeLevel(e.NewValue);
        VolumeText.Text = $"{e.NewValue:0}%";
        if (e.NewValue > 0 && SystemControlService.GetMute()) SystemControlService.SetMute(false);
        UpdateMuteIcon(e.NewValue <= 0);
    }

    private void Mute_Click(object s, MouseButtonEventArgs e)
    {
        bool mute = !SystemControlService.GetMute();
        SystemControlService.SetMute(mute);
        UpdateMuteIcon(mute);
    }

    private void Brightness_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressBrightness) return;
        SystemControlService.SetBrightness(e.NewValue);
        BrightnessText.Text = $"{e.NewValue:0}%";
    }

    // ---------- Питание ----------

    private bool Confirm(string action) =>
        MessageBox.Show($"Точно {action}?", "AURA",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Shutdown_Click(object s, RoutedEventArgs e)
    { if (Confirm("выключить компьютер")) SystemControlService.Shutdown(); }

    private void Restart_Click(object s, RoutedEventArgs e)
    { if (Confirm("перезагрузить компьютер")) SystemControlService.Restart(); }

    private void LogOff_Click(object s, RoutedEventArgs e)
    { if (Confirm("выйти из сеанса")) SystemControlService.LogOff(); }

    private void Sleep_Click(object s, RoutedEventArgs e)
    { if (Confirm("перевести компьютер в сон")) SystemControlService.Sleep(); }

    private void Lock_Click(object s, RoutedEventArgs e) => SystemControlService.Lock();

    private void TaskMgr_Click(object s, RoutedEventArgs e) => SystemControlService.TaskManager();

    // ---------- Батарея: P/Invoke ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus ps);
}
