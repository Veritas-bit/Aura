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
    public static double ReadCurrentBrightness()
    {
        double internalBrightness = ReadInternalWmi();
        return internalBrightness >= 0 ? internalBrightness : ReadExternalDdc();
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

    private static double ReadExternalDdc()
    {
        try
        {
            // Read only the primary display (where AURA is positioned).
            // No writes or raw VCP commands are sent to external monitors.
            IntPtr monitor = MonitorFromPoint(new NativePoint(0, 0), 1); // DEFAULTTOPRIMARY
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
                    if (!GetMonitorCapabilities(physical.Handle, out uint caps, out _))
                        continue;
                    const uint MC_CAPS_BRIGHTNESS = 0x2;
                    if ((caps & MC_CAPS_BRIGHTNESS) == 0) continue;
                    if (!GetMonitorBrightness(physical.Handle, out uint min, out uint current, out uint max) ||
                        max <= min) continue;

                    return Math.Clamp(100.0 * (current - min) / (max - min), 0, 100);
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
}
