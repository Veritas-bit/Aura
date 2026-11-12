using System.Management;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Read-only brightness observer: first built-in displays via WMI, then
/// external monitors only if they advertise DDC/CI brightness support.
/// All calls should run on a worker thread, never on the WPF dispatcher.
/// </summary>
public static class BrightnessMonitorService
{
    /// <summary>Returns 0..100 or -1 when the active display exposes no readable brightness.</summary>
    public static string LastDiagnostic { get; private set; } = "Brightness not tested";

    public static double ReadCurrentBrightness() => ReadCurrentBrightness(IntPtr.Zero);

    /// <summary>Read the monitor showing AURA, then use WMI for built-in panels.</summary>
    public static double ReadCurrentBrightness(IntPtr windowHandle)
    {
        // Query the built-in panel first. DDC/CI calls may take seconds or
        // stall on drivers that reject monitor-control requests.
        double wmi = ReadInternalWmi();
        if (wmi >= 0)
        {
            LastDiagnostic = $"WMI: встроенный экран, {wmi:0}%";
            return wmi;
        }

        double ddc = ReadExternalDdc(windowHandle);
        if (ddc >= 0)
        {
            LastDiagnostic = $"DDC/CI: внешний монитор, {ddc:0}%";
            return ddc;
        }
        LastDiagnostic = "Brightness unavailable: target monitor does not expose DDC/CI VCP 0x10 or WMI brightness";
        return -1;
    }

    private static double ReadInternalWmi()
    {
        try
        {
            using var search = new ManagementObjectSearcher(@"root\WMI",
                "SELECT Active, CurrentBrightness FROM WmiMonitorBrightness");
            using var result = search.Get();
            foreach (ManagementObject display in result)
            {
                using (display)
                {
                    if (display["Active"] is bool active && !active) continue;
                    if (display["CurrentBrightness"] is not null)
                        return Math.Clamp(Convert.ToDouble(display["CurrentBrightness"]), 0, 100);
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        return -1;
    }

    private static double ReadExternalDdc(IntPtr windowHandle)
    {
        try
        {
            // Use the display containing the AURA window instead of assuming (0,0).
            IntPtr monitor = windowHandle != IntPtr.Zero
                ? MonitorFromWindow(windowHandle, 2) // MONITOR_DEFAULTTONEAREST
                : MonitorFromPoint(new NativePoint(0, 0), 1); // MONITOR_DEFAULTTOPRIMARY
            if (monitor == IntPtr.Zero ||
                !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) ||
                count == 0 || count > 16)
                return -1;

            var monitors = new PhysicalMonitor[(int)count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors))
                return -1;

            try
            {
                foreach (var physical in monitors)
                {
                    if (physical.Handle == IntPtr.Zero) continue;
                    const uint MC_CAPS_BRIGHTNESS = 0x2;
                    bool highLevel = GetMonitorCapabilities(physical.Handle, out uint caps, out _) &&
                        (caps & MC_CAPS_BRIGHTNESS) != 0;
                    if (highLevel &&
                        GetMonitorBrightness(physical.Handle, out uint min, out uint current, out uint max) &&
                        max > min)
                        return Math.Clamp(100.0 * (current - min) / (max - min), 0, 100);

                    // Some monitors expose brightness VCP 0x10 but do not report
                    // the high-level MC_CAPS_BRIGHTNESS flag. Read-only fallback.
                    const byte VCP_LUMINANCE = 0x10;
                    if (GetVCPFeatureAndVCPFeatureReply(physical.Handle, VCP_LUMINANCE,
                        IntPtr.Zero, out uint currentVcp, out uint maxVcp) && maxVcp > 0)
                        return Math.Clamp(100.0 * currentVcp / maxVcp, 0, 100);
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (COMException) { }
        catch (SEHException) { }
        return -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorCapabilities(
        IntPtr physicalMonitor, out uint capabilities, out uint colorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        IntPtr physicalMonitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr physicalMonitor, byte vcpCode, IntPtr codeType,
        out uint currentValue, out uint maximumValue);
}
