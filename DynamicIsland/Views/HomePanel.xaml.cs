using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public partial class HomePanel : UserControl
{
    private static readonly string NotesPath =
        Path.Combine(SettingsService.DataDir, "notes.txt");

    private DateTime? _alarmAt;
    private readonly DispatcherTimer _alarmTimer;
    private TimeSpan _timerLeft;
    private bool _timerRunning;
    private readonly DispatcherTimer _countdown;

    public HomePanel()
    {
        InitializeComponent();

        _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _alarmTimer.Tick += (_, _) => CheckAlarm();
        _alarmTimer.Start();

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => TickCountdown();

        try { if (File.Exists(NotesPath)) NotesBox.Text = File.ReadAllText(NotesPath); }
        catch { }
    }

    // ---------- Карточки ----------

    private void Tool_ScreenRecord(object s, RoutedEventArgs e) => SendHotkey(win: true, alt: true, key: 0x52);       // Win+Alt+R
    private void Tool_Screenshot(object s, RoutedEventArgs e) => SendHotkey(win: true, shift: true, key: 0x53);      // Win+Shift+S
    private void Tool_Clipboard(object s, RoutedEventArgs e) => SendHotkey(win: true, key: 0x56);                    // Win+V
    private void Tool_Explorer(object s, RoutedEventArgs e)
    {
        try { Process.Start("explorer.exe"); } catch { }
    }

    private void Tool_Notes(object s, RoutedEventArgs e) => ShowTool(NotesTool);
    private void Tool_Alarm(object s, RoutedEventArgs e) => ShowTool(AlarmTool);
    private void Tool_Calculator(object s, RoutedEventArgs e) => ShowTool(CalcTool);
    private void Tool_Timer(object s, RoutedEventArgs e) => ShowTool(TimerTool);

    private void ShowTool(UIElement tool)
    {
        ToolHost.Visibility = Visibility.Visible;
        foreach (var t in new[] { NotesTool, AlarmTool, CalcTool, TimerTool })
            t.Visibility = ReferenceEquals(t, tool) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseTool(object s, RoutedEventArgs e) => ToolHost.Visibility = Visibility.Collapsed;

    // ---------- Заметки ----------

    private void Notes_Changed(object s, TextChangedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            File.WriteAllText(NotesPath, NotesBox.Text);
        }
        catch { }
    }

    // ---------- Будильник ----------

    private void Alarm_Set(object s, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParse(AlarmTimeBox.Text, out var time))
        {
            AlarmStatus.Text = "Неверный формат. Пример: 07:30";
            return;
        }
        var at = DateTime.Today + time;
        if (at <= DateTime.Now) at = at.AddDays(1);
        _alarmAt = at;
        AlarmStatus.Text = $"Будильник установлен на {at:HH:mm}";
    }

    private void Alarm_Cancel(object s, RoutedEventArgs e)
    {
        _alarmAt = null;
        AlarmStatus.Text = "Будильник выключен";
    }

    private void CheckAlarm()
    {
        if (_alarmAt.HasValue && DateTime.Now >= _alarmAt.Value)
        {
            _alarmAt = null;
            AlarmStatus.Text = "Время пришло!";
            System.Media.SystemSounds.Exclamation.Play();
            if (Window.GetWindow(this) is MainWindow mw) mw.Expand("home");
        }
    }

    // ---------- Калькулятор ----------

    private void Calc_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try
        {
            var value = new DataTable().Compute(CalcInput.Text.Replace(',', '.'), null);
            CalcResult.Text = $"= {value}";
        }
        catch
        {
            CalcResult.Text = "= ошибка в выражении";
        }
    }

    // ---------- Таймер ----------

    private void Timer_Start(object s, RoutedEventArgs e)
    {
        if (!int.TryParse(TimerMinutesBox.Text, out int min) || min <= 0) min = 5;
        _timerLeft = TimeSpan.FromMinutes(min);
        _timerRunning = true;
        TimerDisplay.Text = FormatCountdown(_timerLeft);
        _countdown.Start();
    }

    private void Timer_Stop(object s, RoutedEventArgs e)
    {
        _timerRunning = false;
        _countdown.Stop();
    }

    private void TickCountdown()
    {
        if (!_timerRunning) return;
        _timerLeft = _timerLeft.Subtract(TimeSpan.FromSeconds(1));
        TimerDisplay.Text = FormatCountdown(_timerLeft);
        if (_timerLeft <= TimeSpan.Zero)
        {
            _timerRunning = false;
            _countdown.Stop();
            TimerDisplay.Text = "00:00";
            System.Media.SystemSounds.Beep.Play();
            if (Window.GetWindow(this) is MainWindow mw) mw.Expand("home");
        }
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        int hours = (int)Math.Floor(Math.Max(0, remaining.TotalHours));
        return hours > 0
            ? $"{hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : remaining.ToString(@"mm\:ss");
    }

    // ---------- Симуляция горячих клавиш ----------

    private static void SendHotkey(bool win = false, bool alt = false, bool shift = false, byte key = 0)
    {
        try
        {
            if (win) keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            if (alt) keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            if (shift) keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            if (key != 0) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
            if (shift) keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (alt) keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (win) keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    private const byte VK_LWIN = 0x5B;
    private const byte VK_MENU = 0x12;
    private const byte VK_SHIFT = 0x10;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
