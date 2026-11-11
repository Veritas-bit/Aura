using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AnimeEyes
{
    public partial class AnimeEyesControl : UserControl
    {
        private Ellipse leftPupil, rightPupil;
        private Ellipse leftHighlight1, leftHighlight2, rightHighlight1, rightHighlight2;
        private Ellipse leftBlush, rightBlush;
        private DispatcherTimer blinkTimer;

        public AnimeEyesControl()
        {
            InitializeComponent();
            CreateEyes();
            StartBlinkLoop();
            this.MouseMove += OnMouseMove;
        }

        private void CreateEyes()
        {
            var canvas = new Canvas { Width = 520, Height = 300, Background = Brushes.Transparent };
            Content = canvas;

            // Левый глаз
            var leftEye = CreateSingleEye(true);
            Canvas.SetLeft(leftEye, 30);
            Canvas.SetTop(leftEye, 20);
            canvas.Children.Add(leftEye);

            // Правый глаз
            var rightEye = CreateSingleEye(false);
            Canvas.SetLeft(rightEye, 270);
            Canvas.SetTop(rightEye, 20);
            canvas.Children.Add(rightEye);

            // Румянец
            leftBlush = CreateBlush();
            rightBlush = CreateBlush();
            Canvas.SetLeft(leftBlush, 55);
            Canvas.SetTop(leftBlush, 175);
            Canvas.SetLeft(rightBlush, 295);
            Canvas.SetTop(rightBlush, 175);
            canvas.Children.Add(leftBlush);
            canvas.Children.Add(rightBlush);

            // Маленький нос
            var nose = new Ellipse
            {
                Width = 9,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(255, 175, 185))
            };
            Canvas.SetLeft(nose, 255);
            Canvas.SetTop(nose, 155);
            canvas.Children.Add(nose);
        }

        private Canvas CreateSingleEye(bool isLeft)
        {
            var eye = new Canvas { Width = 220, Height = 200 };

            // Белок
            var sclera = new Ellipse
            {
                Width = 150,
                Height = 130,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(20, 20, 30)),
                StrokeThickness = 4
            };
            Canvas.SetLeft(sclera, 25);
            Canvas.SetTop(sclera, 25);
            eye.Children.Add(sclera);

            // Радужка (голубая)
            var iris = new Ellipse
            {
                Width = 68,
                Height = 78,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(40, 180, 255),
                    Color.FromRgb(0, 50, 140),
                    new Point(0.3, 0), new Point(0.7, 1))
            };
            Canvas.SetLeft(iris, 66);
            Canvas.SetTop(iris, 48);
            eye.Children.Add(iris);

            // Зрачок
            var pupil = new Ellipse { Width = 26, Height = 32, Fill = Brushes.Black };
            Canvas.SetLeft(pupil, 87);
            Canvas.SetTop(pupil, 71);
            eye.Children.Add(pupil);

            if (isLeft) leftPupil = pupil;
            else rightPupil = pupil;

            // Блики
            var h1 = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White };
            var h2 = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White, Opacity = 0.9 };
            Canvas.SetLeft(h1, 72);
            Canvas.SetTop(h1, 55);
            Canvas.SetLeft(h2, 105);
            Canvas.SetTop(h2, 95);
            eye.Children.Add(h1);
            eye.Children.Add(h2);

            if (isLeft)
            {
                leftHighlight1 = h1;
                leftHighlight2 = h2;
            }
            else
            {
                rightHighlight1 = h1;
                rightHighlight2 = h2;
            }

            // Ресницы
            var lashes = new Path
            {
                Stroke = Brushes.Black,
                StrokeThickness = 3.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse(isLeft
                    ? "M 30,55 Q 70,10 140,45 Q 165,58 185,62 M 45,48 Q 80,15 135,42"
                    : "M 35,55 Q 75,10 145,45 Q 170,58 190,62 M 50,48 Q 85,15 140,42")
            };
            eye.Children.Add(lashes);

            return eye;
        }

        private Ellipse CreateBlush()
        {
            return new Ellipse
            {
                Width = 78,
                Height = 32,
                Fill = new SolidColorBrush(Color.FromArgb(160, 255, 140, 170)),
                Opacity = 0.55
            };
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (leftPupil == null) return;

            Point mouse = e.GetPosition(this);
            Point center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

            double dx = Math.Clamp((mouse.X - center.X) / 16.0, -14, 14);
            double dy = Math.Clamp((mouse.Y - center.Y) / 20.0, -9, 9);

            Canvas.SetLeft(leftPupil, 87 + dx);
            Canvas.SetTop(leftPupil, 71 + dy);
            Canvas.SetLeft(rightPupil, 87 + dx);
            Canvas.SetTop(rightPupil, 71 + dy);

            Canvas.SetLeft(leftHighlight1, 72 + dx * 0.35);
            Canvas.SetLeft(rightHighlight1, 72 + dx * 0.35);
        }

        private void StartBlinkLoop()
        {
            blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            blinkTimer.Tick += (s, e) => Blink();
            blinkTimer.Start();
        }

        public void Blink()
        {
            if (leftPupil == null) return;

            leftPupil.Opacity = 0.05;
            rightPupil.Opacity = 0.05;
            leftHighlight1.Opacity = 0;
            rightHighlight1.Opacity = 0;
            leftHighlight2.Opacity = 0;
            rightHighlight2.Opacity = 0;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            timer.Tick += (s, e) =>
            {
                leftPupil.Opacity = 1;
                rightPupil.Opacity = 1;
                leftHighlight1.Opacity = 1;
                rightHighlight1.Opacity = 1;
                leftHighlight2.Opacity = 0.9;
                rightHighlight2.Opacity = 0.9;
                timer.Stop();
            };
            timer.Start();
        }

        public void SetEmotion(string emotion)
        {
            double blushOpacity = emotion switch
            {
                "blush" or "shy" => 0.9,
                "surprise" => 0.35,
                "sad" => 0.65,
                "angry" => 0.25,
                _ => 0.55
            };

            leftBlush.Opacity = blushOpacity;
            rightBlush.Opacity = blushOpacity;
        }
    }
}
