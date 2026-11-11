using System.Windows;

namespace AnimeEyes
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Normal_Click(object sender, RoutedEventArgs e) => Eyes.SetEmotion("normal");
        private void Blush_Click(object sender, RoutedEventArgs e) => Eyes.SetEmotion("blush");
        private void Surprise_Click(object sender, RoutedEventArgs e) => Eyes.SetEmotion("surprise");
        private void Sad_Click(object sender, RoutedEventArgs e) => Eyes.SetEmotion("sad");
        private void Blink_Click(object sender, RoutedEventArgs e) => Eyes.Blink();
    }
}
