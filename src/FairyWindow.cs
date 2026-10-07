using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ApiFairy
{
    // The little handheld that sits on your desktop. Transparent everywhere except the fairy and her
    // speech bubble, so clicks pass straight through to whatever is underneath.
    public sealed class FairyWindow : Window
    {
        public const double Wd = 300, Ht = 260;
        // Snapshots for the README skip animations so the picture shows the settled pose.
        public static bool Still;
        const double DeviceW = 84, DeviceH = 100;

        public event EventHandler OpenRequested;
        public event EventHandler HideRequested;
        public event EventHandler QuitRequested;
        public event EventHandler Moved;
        public event EventHandler BubbleClosed;

        readonly WriteableBitmap screen = new WriteableBitmap(Sprite.W, Sprite.H, 96, 96, PixelFormats.Bgra32, null);
        readonly Sprite.Clock clock = new Sprite.Clock();
        readonly DispatcherTimer tick = new DispatcherTimer();
        readonly bool reduceMotion = !SystemParameters.ClientAreaAnimation;

        readonly StackPanel bubbleBox;
        readonly TextBlock bubbleText;
        readonly Border badge;
        readonly TextBlock badgeText;
        readonly Canvas deviceHost;
        readonly ScaleTransform pop = new ScaleTransform(1, 1);
        readonly TranslateTransform bubbleShift = new TranslateTransform();
        readonly RotateTransform wingL = new RotateTransform(0, 34, 27), wingR = new RotateTransform(0, 2, 27);

        string steadyMood = "happy";
        DateTime alertUntil = DateTime.MinValue, bubbleUntil = DateTime.MaxValue;
        bool down, dragging, bubbleHover;
        Point downScreen;
        double downLeft, downTop;

        public FairyWindow()
        {
            Title = "API Fairy";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = Wd;
            Height = Ht;
            UseLayoutRounding = true;
            Icon = Pixels.IconSource(32);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // speech bubble with a tail pointing down at the fairy
            bubbleText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = Theme.Mono, FontSize = 12.5, Foreground = Theme.Ink, LineHeight = 17 };
            var close = new TextBlock { Text = "×", FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Theme.Ink, Margin = new Thickness(8, -3, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(close, "Close message");
            close.MouseLeftButtonUp += (s, e) => { e.Handled = true; HideBubble(); var h = BubbleClosed; if (h != null) h(this, EventArgs.Empty); };
            var bubbleGrid = new Grid();
            bubbleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bubbleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bubbleGrid.Children.Add(bubbleText);
            Grid.SetColumn(close, 1);
            bubbleGrid.Children.Add(close);
            var bubble = new Border
            {
                Background = Theme.Pale, BorderBrush = Theme.Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 9, 10, 11), Child = bubbleGrid, Cursor = Cursors.Hand,
            };
            bubble.MouseLeftButtonUp += (s, e) => Open();
            var tailFill = new Polygon { Points = new PointCollection { new Point(0, 0), new Point(16, 0), new Point(6, 13) }, Fill = Theme.Pale };
            var tailLine = new Polyline { Points = new PointCollection { new Point(1.5, 0), new Point(6, 12), new Point(14.5, 0) }, Stroke = Theme.Ink, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
            var tail = new Grid { Width = 16, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -3, 62, 0) };
            tail.Children.Add(tailFill);
            tail.Children.Add(tailLine);
            bubbleBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 280, Margin = new Thickness(0, 0, 8, -2), Visibility = Visibility.Collapsed, RenderTransform = bubbleShift };
            bubbleBox.Children.Add(bubble);
            bubbleBox.Children.Add(tail);
            bubbleBox.MouseEnter += (s, e) => bubbleHover = true;
            bubbleBox.MouseLeave += (s, e) => bubbleHover = false;
            root.Children.Add(bubbleBox);

            // the handheld: wings, shell, LCD and label
            deviceHost = new Canvas { Width = 140, Height = 118, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 6), Background = null, Cursor = Cursors.Hand };
            Grid.SetRow(deviceHost, 1);
            var left = Wing(false, wingL);
            Canvas.SetLeft(left, 0); Canvas.SetTop(left, 10);
            var right = Wing(true, wingR);
            Canvas.SetLeft(right, 104); Canvas.SetTop(right, 10);
            deviceHost.Children.Add(left);
            deviceHost.Children.Add(right);

            var img = new Image { Source = screen, Width = 64, Height = 48, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var lcd = new Border { Background = Theme.Lcd, BorderBrush = Theme.Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(9), Child = img, HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = "FAIRY", FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(lcd);
            stack.Children.Add(label);
            var inner = new Border { Width = DeviceW, Height = DeviceH, CornerRadius = new CornerRadius(24, 24, 28, 28), Background = Theme.Shell, BorderBrush = Theme.Ink, BorderThickness = new Thickness(3), Child = stack };
            var device = new Border
            {
                CornerRadius = new CornerRadius(27, 27, 31, 31), Background = Theme.Shell, Padding = new Thickness(3), Child = inner,
                Effect = new DropShadowEffect { Color = Theme.ShellC, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 },
                RenderTransform = pop, RenderTransformOrigin = new Point(0.5, 1),
            };
            Canvas.SetLeft(device, 25); Canvas.SetTop(device, 8);
            deviceHost.Children.Add(device);

            badgeText = new TextBlock { FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Theme.Shell, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge = new Border { MinWidth = 24, Height = 24, Padding = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(12), Background = Theme.Ink, BorderBrush = Theme.Shell, BorderThickness = new Thickness(2), Child = badgeText, Visibility = Visibility.Collapsed };
            Canvas.SetLeft(badge, 98); Canvas.SetTop(badge, 0);
            deviceHost.Children.Add(badge);
            root.Children.Add(deviceHost);
            System.Windows.Automation.AutomationProperties.SetName(deviceHost, "API Fairy. Click to see your keys.");

            deviceHost.MouseLeftButtonDown += OnDown;
            deviceHost.MouseMove += OnMove;
            deviceHost.MouseLeftButtonUp += OnUp;
            deviceHost.LostMouseCapture += (s, e) => down = false;
            deviceHost.MouseEnter += (s, e) => Flap(2);

            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Open API Fairy", (s, e) => Open()));
            menu.Items.Add(MenuItem("Hide until something happens", (s, e) => Raise(HideRequested)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Quit", (s, e) => Raise(QuitRequested)));
            deviceHost.ContextMenu = menu;

            Content = root;
            tick.Interval = TimeSpan.FromMilliseconds(280);
            tick.Tick += (s, e) => Tick();
            IsVisibleChanged += (s, e) => { if (IsVisible) tick.Start(); else tick.Stop(); };
            Draw();
        }

        static MenuItem MenuItem(string text, RoutedEventHandler click)
        {
            var m = new MenuItem { Header = text };
            m.Click += click;
            return m;
        }

        void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        void Open() { HideBubble(); Raise(OpenRequested); }

        static Path Wing(bool mirrored, RotateTransform rotate)
        {
            var g = Geometry.Parse("M34,24 C25,3 3,0 3,14 C3,25 21,29 34,26 Z M34,29 C24,30 8,37 12,46 C16,53 29,42 34,31 Z").Clone();
            if (mirrored) g.Transform = new MatrixTransform(-1, 0, 0, 1, 36, 0);
            return new Path { Data = g, Fill = Theme.Pale, Stroke = Theme.Ink, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, RenderTransform = rotate };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Native.MakeOverlay(new WindowInteropHelper(this).Handle);
        }

        // ---- what the fairy shows ----

        public void SetMood(string mood) { steadyMood = mood ?? "happy"; }

        public void SetBadge(int n)
        {
            badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            badgeText.Text = n > 99 ? "99+" : n.ToString();
        }

        public bool BubbleVisible { get { return bubbleBox.Visibility == Visibility.Visible; } }

        // Pop up with a message. Sticky messages stay until clicked; the rest fade after a while.
        public void Say(string text, bool sticky)
        {
            bubbleText.Text = text;
            bubbleBox.Visibility = Visibility.Visible;
            bubbleUntil = sticky ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(14);
            alertUntil = DateTime.UtcNow.AddSeconds(12);
            Topmost = false;
            Topmost = true;
            if (!reduceMotion && !Still)
            {
                var spring = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5 } };
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                bubbleBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                bubbleShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase() });
                Flap(6);
            }
            Draw();
        }

        public void HideBubble()
        {
            bubbleBox.Visibility = Visibility.Collapsed;
            bubbleUntil = DateTime.MaxValue;
        }

        void Flap(int times)
        {
            if (reduceMotion) return;
            var a = new DoubleAnimation(0, -16, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(times) };
            var b = new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(times) };
            wingL.BeginAnimation(RotateTransform.AngleProperty, a);
            wingR.BeginAnimation(RotateTransform.AngleProperty, b);
        }

        string CurrentMood()
        {
            if (DateTime.UtcNow < alertUntil) return "alert";
            if (steadyMood == "happy" && Native.IdleTime() > TimeSpan.FromMinutes(10)) return "sleep";
            return steadyMood;
        }

        void Tick()
        {
            if (BubbleVisible && !bubbleHover && DateTime.UtcNow > bubbleUntil)
            {
                HideBubble();
                Raise(BubbleClosed);
            }
            Draw();
        }

        void Draw()
        {
            Pixels.Write(screen, clock.Next(CurrentMood(), reduceMotion));
        }

        // ---- click to open, drag to move ----

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            down = true;
            dragging = false;
            downScreen = PointToScreen(e.GetPosition(this));
            downLeft = Left;
            downTop = Top;
            deviceHost.CaptureMouse();
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!down) return;
            var now = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            var delta = new Vector(now.X - downScreen.X, now.Y - downScreen.Y);
            if (source != null && source.CompositionTarget != null) delta = source.CompositionTarget.TransformFromDevice.Transform(delta);
            if (!dragging && delta.Length > 4) dragging = true;
            if (dragging)
            {
                Left = downLeft + delta.X;
                Top = downTop + delta.Y;
            }
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!down) return;
            down = false;
            deviceHost.ReleaseMouseCapture();
            e.Handled = true;
            if (dragging)
            {
                ClampToScreen();
                Raise(Moved);
            }
            else Open();
        }

        public void PlaceDefault()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Wd - 4;
            Top = wa.Bottom - Ht - 4;
        }

        public void ClampToScreen()
        {
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
            Left = Math.Max(l, Math.Min(Left, r - Wd));
            Top = Math.Max(t, Math.Min(Top, b - Ht));
        }
    }
}
