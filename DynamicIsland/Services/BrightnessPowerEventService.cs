using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Listens to Windows brightness changes delivered as a power-setting message.
/// This works without WMI/DDC/CI only where the OS publishes the event.
/// </summary>
public sealed class BrightnessPowerEventService : IDisposable
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private static readonly Guid CurrentMonitorBrightness =
        new("8FFEE2C6-2D01-46BE-ADB9-398ADDC5B4FF");

    private IntPtr _registration;
    public event Action<double>? BrightnessChanged;

    public static double? LastObservedPercent { get; private set; }
    public static string RegistrationStatus { get; private set; } = "Ожидает запуска AURA";

    public void Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _registration != IntPtr.Zero) return;
        Guid id = CurrentMonitorBrightness;
        _registration = RegisterPowerSettingNotification(hwnd, ref id, 0);
        RegistrationStatus = _registration == IntPtr.Zero
            ? $"Ошибка подписки: Win32 {Marshal.GetLastWin32Error()}"
            : "Подписка активна. Ожидание изменения яркости.";
    }

    public void HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmPowerBroadcast ||
            wParam.ToInt64() != PbtPowerSettingChange || lParam == IntPtr.Zero)
            return;
        try
        {
            Guid id = Marshal.PtrToStructure<Guid>(lParam);
            if (id != CurrentMonitorBrightness) return;

            // POWERBROADCAST_SETTING: GUID 16 bytes, DWORD DataLength, Data[0].
            int length = Marshal.ReadInt32(lParam, 16);
            if (length != 1 && length != 4) return;
            int value = length == 1
                ? Marshal.ReadByte(lParam, 20)
                : Marshal.ReadInt32(lParam, 20);
            if (value < 0 || value > 100) return;

            LastObservedPercent = value;
            RegistrationStatus = $"Последнее уведомление: {value}%";
            BrightnessChanged?.Invoke(value);
        }
        catch (Exception ex) when (ex is ArgumentException or AccessViolationException)
        {
            // Do not allow a malformed Windows notification to crash AURA.
        }
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_registration);
            _registration = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(
        IntPtr hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
