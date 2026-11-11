using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Devices.Radios;

namespace DynamicIsland.Services;

/// <summary>
/// Системные переключатели: громкость (Core Audio COM), яркость (WMI),
/// Wi-Fi / Bluetooth (WinRT Radio API), тема Windows (реестр), питание.
/// Всё опасное обёрнуто в try/catch с безопасным фолбэком.
/// </summary>
public static class SystemControlService
{
    // ==================== Громкость (IAudioEndpointVolume) ====================

    private static IAudioEndpointVolume? _vol;

    private static IAudioEndpointVolume? GetVolume()
    {
        if (_vol != null) return _vol;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 1, IntPtr.Zero, out var obj);
            _vol = (IAudioEndpointVolume)obj;
        }
        catch { _vol = null; }
        return _vol;
    }

    /// <summary>0..100 или -1, если недоступно.</summary>
    public static double GetVolumeLevel()
    {
        try
        {
            var vol = GetVolume();
            if (vol == null) return -1;
            vol.GetMasterVolumeLevelScalar(out float v);
            return v is >= 0 and <= 1 ? Math.Round(v * 100) : -1;
        }
        catch { return -1; }
    }

    public static void SetVolumeLevel(double percent)
    {
        try { var g = Guid.Empty; GetVolume()?.SetMasterVolumeLevelScalar((float)(percent / 100.0), ref g); } catch { }
    }

    public static bool GetMute()
    {
        try
        {
            var vol = GetVolume();
            if (vol == null) return false;
            vol.GetMute(out bool m);
            return m;
        }
        catch { return false; }
    }

    public static void SetMute(bool mute)
    {
        try { var g = Guid.Empty; GetVolume()?.SetMute(mute, ref g); } catch { }
    }

    // ==================== Яркость (WMI) ====================

    private static bool? _brightnessSupported;

    public static bool IsBrightnessSupported()
    {
        if (_brightnessSupported.HasValue) return _brightnessSupported.Value;
        try
        {
            using var mc = new System.Management.ManagementClass(@"root\WMI", "WmiMonitorBrightness", null);
            _brightnessSupported = mc.GetInstances().Count > 0;
        }
        catch { _brightnessSupported = false; }
        return _brightnessSupported.Value;
    }

    public static double GetBrightness()
    {
        try
        {
            using var mc = new System.Management.ManagementClass(@"root\WMI", "WmiMonitorBrightness", null);
            foreach (var o in mc.GetInstances())
                return Convert.ToDouble(o["CurrentBrightness"]);
        }
        catch { }
        return -1;
    }

    public static void SetBrightness(double percent)
    {
        try
        {
            using var mc = new System.Management.ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            foreach (System.Management.ManagementObject o in mc.GetInstances())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Clamp(percent, 0, 100) });
                break;
            }
        }
        catch { }
    }

    // ==================== Wi-Fi / Bluetooth (WinRT Radio) ====================

    public static async Task<bool?> GetRadioStateAsync(RadioKind kind)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return null;
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            return radio?.State == RadioState.On;
        }
        catch { return null; }
    }

    /// <summary>true — состояние изменено; false — не удалось (открыть настройки вручную).</summary>
    public static async Task<bool> SetRadioStateAsync(RadioKind kind, bool on)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return false;
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            if (radio == null) return false;
            await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return true;
        }
        catch { return false; }
    }

    public static void OpenSettings(string page) =>
        Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });

    // ==================== Тема Windows ====================

    public static bool IsLightTheme()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return Convert.ToInt32(v) == 1;
        }
        catch { return false; }
    }

    public static void SetLightTheme(bool light)
    {
        try
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, writable: true);
            rk?.SetValue("AppsUseLightTheme", light ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            rk?.SetValue("SystemUsesLightTheme", light ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            // Оповещаем систему о смене темы
            SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet");
        }
        catch { }
    }

    // ==================== Питание / сеанс ====================

    public static void Shutdown() => RunHidden("shutdown.exe", "/s /t 3");
    public static void Restart() => RunHidden("shutdown.exe", "/r /t 3");
    public static void LogOff() => RunHidden("shutdown.exe", "/l");
    public static void TaskManager() { try { Process.Start("taskmgr.exe"); } catch { } }

    public static void Lock() { try { LockWorkStation(); } catch { } }
    public static void Sleep() => RunHidden("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void Launch(string command)
    {
        try
        {
            if (command.StartsWith("http", StringComparison.OrdinalIgnoreCase) || command.StartsWith("ms-"))
            {
                OpenUrl(command);
                return;
            }
            Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                // попытка через PATH / App Paths
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{command}\"")
                { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { }
        }
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
    }

    // ==================== Автозапуск ====================

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            const string name = "AURA";
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(name, $"\"{exe}\"");
            }
            else key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    // ==================== Win32 / COM объявления ====================

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_SETTINGCHANGE = 0x001A;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendNotifyMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    private enum EDataFlow { eRender = 0 }
    private enum ERole { eMultimedia = 1 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(IntPtr pguidEventContext);
        int VolumeStepDown(IntPtr pguidEventContext);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }
}
