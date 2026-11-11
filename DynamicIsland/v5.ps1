Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MV {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    public static void Click(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
"@
$ws = "C:\Users\maked\Documents\kimi\tasks\2026-11-10\20-15-34-1a739f38"
function Shot($name, $x, $y, $w, $h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save("$ws\$name.png"); $g.Dispose(); $bmp.Dispose()
}
[MV]::SetCursorPos(300, 750)
Start-Sleep -Milliseconds 900
Shot "v5_collapsed" 600 0 720 110
[MV]::SetCursorPos(960, 40)
Start-Sleep -Milliseconds 1400
Shot "v5_expanded" 540 0 840 620
[MV]::SetCursorPos(300, 750)
Write-Output "done"
