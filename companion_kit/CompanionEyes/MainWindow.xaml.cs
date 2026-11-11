using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CompanionEyes.Controls;

namespace CompanionEyes;
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _voiceDemo = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private double _v;
    public MainWindow()
    {
        InitializeComponent();
        _voiceDemo.Tick += (_, _) => { _v += .18; Eyes.SetVoiceLevel((Math.Sin(_v) + 1) / 2); };
    }
    private void Root_MouseMove(object sender, MouseEventArgs e) => Eyes.TrackPointer(e.GetPosition(Eyes));
    private void Root_MouseLeave(object sender, MouseEventArgs e) => Eyes.PointerLeft();
    private void Idle_Click(object sender, RoutedEventArgs e) { _voiceDemo.Stop(); Eyes.SetEmotion(CompanionEmotion.Neutral); Eyes.Wake(); }
    private void Search_Click(object sender, RoutedEventArgs e) { _voiceDemo.Stop(); Eyes.SetMode(CompanionMode.Searching); }
    private void Sleep_Click(object sender, RoutedEventArgs e) { _voiceDemo.Stop(); Eyes.SetMode(CompanionMode.Sleeping); }
    private void Alert_Click(object sender, RoutedEventArgs e) { _voiceDemo.Stop(); Eyes.NotifyAttention(); }
    private void Type_Click(object sender, RoutedEventArgs e) { _voiceDemo.Stop(); Eyes.SetTyping(true); }
    private void Voice_Click(object sender, RoutedEventArgs e) { Eyes.SetMode(CompanionMode.Voice); _voiceDemo.Start(); }
    protected override void OnClosed(EventArgs e) { _voiceDemo.Stop(); Eyes.Dispose(); base.OnClosed(e); }
}
