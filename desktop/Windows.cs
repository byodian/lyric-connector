using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LyricConnector
{
    public sealed class LyricWindow : Window
    {
        private readonly Border surface;
        private readonly Border panelBackground;
        private readonly TextBlock previous;
        private readonly TextBlock current;
        private readonly TextBlock next;
        private readonly GradientStop karaokeFillStart;
        private readonly GradientStop karaokeFillEnd;
        private readonly GradientStop karaokeBaseStart;
        private readonly GradientStop karaokeBaseEnd;
        private Color karaokeFillColor;
        private Color karaokeBaseColor;
        private double karaokeProgress = 1;
        private readonly Button previousTrack;
        private readonly Button playPause;
        private readonly Button nextTrack;
        private readonly Button closeOverlay;
        private readonly StackPanel controls;
        private readonly Border progressTrack;
        private readonly Border progressFill;
        private double progressRatio;
        private double backgroundOpacity;
        private bool through;
        private bool previousAvailable;
        private bool playPauseAvailable;
        private bool nextAvailable;
        private bool playPauseShowsPlay = true;
        private byte playPauseCommand;
        public event Action<byte> RemoteCommandRequested;
        public event Action<double> OffsetAdjustmentRequested;
        public event Action LyricsRequested;
        public event Action SettingsRequested;
        public event Action HideRequested;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetStyle(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetStyle(IntPtr window, int index, IntPtr value);

        public LyricWindow(DesktopSettings settings)
        {
            Title = "Lyric Connector · 桌面歌词";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = settings.Width;
            Height = Math.Max(136, settings.FontSize * 3 + 32);
            MinWidth = 360;
            MinHeight = Math.Max(126, settings.FontSize * 3 + 24);
            var area = SystemParameters.WorkArea;
            Left = settings.Left < 0 ? area.Left + (area.Width - Width) / 2 : Math.Max(area.Left, Math.Min(settings.Left, area.Right - 120));
            Top = settings.Top < 0 ? area.Bottom - Height - 44 : Math.Max(area.Top, Math.Min(settings.Top, area.Bottom - 60));
            surface = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            panelBackground = new Border { CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 5, 40, 5) };
            karaokeFillColor = (Color)ColorConverter.ConvertFromString(settings.CurrentLineColor);
            karaokeBaseColor = (Color)ColorConverter.ConvertFromString(settings.NextLineColor);
            karaokeFillStart = new GradientStop(karaokeFillColor, 0);
            karaokeFillEnd = new GradientStop(karaokeFillColor, 1);
            karaokeBaseStart = new GradientStop(karaokeBaseColor, 1);
            karaokeBaseEnd = new GradientStop(karaokeBaseColor, 1);
            var karaokeBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
                MappingMode = BrushMappingMode.RelativeToBoundingBox };
            karaokeBrush.GradientStops.Add(karaokeFillStart);
            karaokeBrush.GradientStops.Add(karaokeFillEnd);
            karaokeBrush.GradientStops.Add(karaokeBaseStart);
            karaokeBrush.GradientStops.Add(karaokeBaseEnd);
            previous = new TextBlock { TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.55 };
            current = new TextBlock { TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeights.Medium, Foreground = karaokeBrush, Margin = new Thickness(0, 1, 0, 1) };
            next = new TextBlock { TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 1) };
            previousTrack = PlaybackButton("播放上一首", PlaybackGlyph(RemoteCommand.PreviousTrack));
            playPause = PlaybackButton("播放或暂停", PlaybackGlyph(RemoteCommand.Play));
            nextTrack = PlaybackButton("播放下一首", PlaybackGlyph(RemoteCommand.NextTrack));
            var offsetInk = new SolidColorBrush(Color.FromRgb(248, 244, 255));
            var decreaseOffset = PlaybackButton("减少歌词提前量 0.5 秒", new TextBlock
                { Text = "−0.5s", FontSize = 10, FontWeight = FontWeights.Medium, Foreground = offsetInk });
            decreaseOffset.Width = 42;
            decreaseOffset.Padding = new Thickness(0, 2, 0, 2);
            decreaseOffset.Margin = new Thickness(1, 0, 1, 0);
            var increaseOffset = PlaybackButton("增加歌词提前量 0.5 秒", new TextBlock
                { Text = "+0.5s", FontSize = 10, FontWeight = FontWeights.Medium, Foreground = offsetInk });
            increaseOffset.Width = 42;
            increaseOffset.Padding = new Thickness(0, 2, 0, 2);
            increaseOffset.Margin = new Thickness(1, 0, 1, 0);
            var lyricsViewButton = PlaybackButton("查看歌词全文", AppleMusicGlyph());
            lyricsViewButton.Margin = new Thickness(8, 0, 2, 0);
            closeOverlay = PlaybackButton("隐藏悬浮歌词窗口", new TextBlock
                { Text = "×", FontSize = 18, FontWeight = FontWeights.Medium, Foreground = offsetInk });
            closeOverlay.Width = 26;
            closeOverlay.Height = 26;
            previousTrack.Click += delegate { RequestRemoteCommand(RemoteCommand.PreviousTrack); };
            playPause.Click += delegate { RequestRemoteCommand(playPauseCommand); };
            nextTrack.Click += delegate { RequestRemoteCommand(RemoteCommand.NextTrack); };
            decreaseOffset.Click += delegate { RequestOffsetAdjustment(-0.5); };
            increaseOffset.Click += delegate { RequestOffsetAdjustment(0.5); };
            lyricsViewButton.Click += delegate
            {
                var handler = LyricsRequested;
                if (handler != null) handler();
            };
            closeOverlay.Click += delegate
            {
                var handler = HideRequested;
                if (handler != null) handler();
            };
            controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0), Opacity = 0, IsHitTestVisible = false };
            var offsetControls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            offsetControls.Children.Add(decreaseOffset);
            offsetControls.Children.Add(increaseOffset);
            controls.Children.Add(offsetControls);
            controls.Children.Add(previousTrack);
            controls.Children.Add(playPause);
            controls.Children.Add(nextTrack);
            controls.Children.Add(lyricsViewButton);
            progressFill = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(132, 86, 186)), CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
            progressTrack = new Border { Width = 220, Height = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1),
                Background = new SolidColorBrush(Color.FromArgb(128, 150, 146, 160)), CornerRadius = new CornerRadius(1),
                Child = progressFill, IsHitTestVisible = false };
            progressTrack.SizeChanged += delegate { UpdateProgressFill(); };
            var content = new Grid();
            content.Children.Add(panelBackground);
            text.Children.Add(previous);
            text.Children.Add(current);
            text.Children.Add(next);
            text.Children.Add(progressTrack);
            text.Children.Add(controls);
            content.Children.Add(text);
            closeOverlay.HorizontalAlignment = HorizontalAlignment.Right;
            closeOverlay.VerticalAlignment = VerticalAlignment.Top;
            closeOverlay.Margin = new Thickness(0, 2, 2, 0);
            closeOverlay.Opacity = 0;
            closeOverlay.IsHitTestVisible = false;
            content.Children.Add(closeOverlay);
            surface.Child = content;
            Content = surface;
            surface.MouseEnter += delegate { SetHoverState(true); };
            surface.MouseLeave += delegate { SetHoverState(false); };
            surface.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
            {
                if (args.ClickCount == 2)
                {
                    var handler = SettingsRequested;
                    if (handler != null) handler();
                    args.Handled = true;
                    return;
                }
                if (!through)
                {
                    DragMove();
                    args.Handled = true;
                }
            };
            SourceInitialized += delegate { Apply(settings); };
            SetPlaybackControls(false, false, RemoteCommand.TogglePlayPause, false, false);
            SetText("", "等待 iPhone 的播放状态", "桌面控制窗口负责歌词与显示设置");
        }

        public void SetText(string before, string active, string after)
        {
            previous.Text = String.IsNullOrEmpty(before) ? "\u00A0" : before;
            current.Text = String.IsNullOrEmpty(active) ? "\u00A0" : active;
            next.Text = String.IsNullOrEmpty(after) ? "\u00A0" : after;
        }
        public void SetKaraokeProgress(double progress)
        {
            karaokeProgress = Math.Max(0, Math.Min(1, progress));
            if (karaokeProgress <= 0)
            {
                karaokeFillStart.Color = karaokeFillEnd.Color = karaokeBaseStart.Color = karaokeBaseEnd.Color = karaokeBaseColor;
                karaokeFillEnd.Offset = karaokeBaseStart.Offset = 0;
            }
            else if (karaokeProgress >= 1)
            {
                karaokeFillStart.Color = karaokeFillEnd.Color = karaokeBaseStart.Color = karaokeBaseEnd.Color = karaokeFillColor;
                karaokeFillEnd.Offset = karaokeBaseStart.Offset = 1;
            }
            else
            {
                karaokeFillStart.Color = karaokeFillEnd.Color = karaokeFillColor;
                karaokeBaseStart.Color = karaokeBaseEnd.Color = karaokeBaseColor;
                karaokeFillEnd.Offset = karaokeBaseStart.Offset = karaokeProgress;
            }
        }

        public void SetProgress(double position, double duration)
        {
            progressTrack.Visibility = duration > 0 ? Visibility.Visible : Visibility.Collapsed;
            progressRatio = duration > 0 ? Math.Max(0, Math.Min(1, position / duration)) : 0;
            UpdateProgressFill();
        }

        private void UpdateProgressFill()
        {
            progressFill.Width = progressTrack.ActualWidth * progressRatio;
        }

        public void SetPlaybackControls(bool paused, bool canPrevious, byte command, bool canPlayPause, bool canNext)
        {
            previousAvailable = canPrevious;
            playPauseAvailable = canPlayPause;
            nextAvailable = canNext;
            playPauseCommand = command;
            if (playPauseShowsPlay != paused)
            {
                playPauseShowsPlay = paused;
                playPause.Content = PlaybackGlyph(paused ? RemoteCommand.Play : RemoteCommand.Pause);
            }
            UpdatePlaybackControls();
        }

        private static Button PlaybackButton(string name, UIElement icon)
        {
            var button = new Button { Content = icon, Width = 34, Height = 24, Padding = new Thickness(2), Margin = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), FocusVisualStyle = null,
                ToolTip = name };
            AutomationProperties.SetName(button, name);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "buttonSurface";
            var templatedParent = new RelativeSource(RelativeSourceMode.TemplatedParent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = templatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = templatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = templatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(112, 136, 122, 155)), "buttonSurface"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(180, 136, 122, 155)), "buttonSurface"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            template.Triggers.Add(disabled);
            button.Template = template;
            return button;
        }

        private static Canvas PlaybackGlyph(byte command)
        {
            var icon = new Canvas { Width = 24, Height = 20, IsHitTestVisible = false };
            var ink = new SolidColorBrush(Color.FromRgb(132, 86, 186));
            if (command == RemoteCommand.PreviousTrack)
            {
                var bar = new System.Windows.Shapes.Rectangle { Width = 2, Height = 15, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, 2); Canvas.SetTop(bar, 2); icon.Children.Add(bar);
                icon.Children.Add(PlaybackTriangle(new Point(19, 2), new Point(7, 10), new Point(19, 18), ink));
            }
            else if (command == RemoteCommand.NextTrack)
            {
                icon.Children.Add(PlaybackTriangle(new Point(5, 2), new Point(17, 10), new Point(5, 18), ink));
                var bar = new System.Windows.Shapes.Rectangle { Width = 2, Height = 15, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, 20); Canvas.SetTop(bar, 2); icon.Children.Add(bar);
            }
            else if (command == RemoteCommand.Pause)
            {
                var first = new System.Windows.Shapes.Rectangle { Width = 3, Height = 15, Fill = ink, RadiusX = 1, RadiusY = 1 };
                var second = new System.Windows.Shapes.Rectangle { Width = 3, Height = 15, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(first, 7); Canvas.SetTop(first, 2);
                Canvas.SetLeft(second, 14); Canvas.SetTop(second, 2);
                icon.Children.Add(first); icon.Children.Add(second);
            }
            else icon.Children.Add(PlaybackTriangle(new Point(7, 2), new Point(19, 10), new Point(7, 18), ink));
            return icon;
        }

        private static Canvas AppleMusicGlyph()
        {
            var icon = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
            var tile = new System.Windows.Shapes.Rectangle { Width = 19, Height = 19, RadiusX = 4, RadiusY = 4,
                Fill = new LinearGradientBrush(Color.FromRgb(250, 35, 59), Color.FromRgb(151, 57, 224), 45) };
            icon.Children.Add(tile);

            var ink = Brushes.White;
            var beamGeometry = new StreamGeometry();
            using (var context = beamGeometry.Open())
            {
                context.BeginFigure(new Point(8.2, 4.2), true, true);
                context.LineTo(new Point(15.7, 2.3), true, false);
                context.LineTo(new Point(15.7, 4.8), true, false);
                context.LineTo(new Point(8.2, 6.7), true, false);
            }
            beamGeometry.Freeze();
            icon.Children.Add(new System.Windows.Shapes.Path { Data = beamGeometry, Fill = ink, IsHitTestVisible = false });

            var leftStem = new System.Windows.Shapes.Rectangle { Width = 1.6, Height = 8.4, RadiusX = 0.8, RadiusY = 0.8, Fill = ink };
            Canvas.SetLeft(leftStem, 8.2);
            Canvas.SetTop(leftStem, 5.0);
            icon.Children.Add(leftStem);
            var rightStem = new System.Windows.Shapes.Rectangle { Width = 1.6, Height = 8.4, RadiusX = 0.8, RadiusY = 0.8, Fill = ink };
            Canvas.SetLeft(rightStem, 14.1);
            Canvas.SetTop(rightStem, 3.3);
            icon.Children.Add(rightStem);

            var leftNote = new System.Windows.Shapes.Ellipse { Width = 5, Height = 3.6, Fill = ink,
                RenderTransform = new RotateTransform(-22, 2.5, 1.8), IsHitTestVisible = false };
            Canvas.SetLeft(leftNote, 4.1);
            Canvas.SetTop(leftNote, 12.1);
            icon.Children.Add(leftNote);
            var rightNote = new System.Windows.Shapes.Ellipse { Width = 5, Height = 3.6, Fill = ink,
                RenderTransform = new RotateTransform(-22, 2.5, 1.8), IsHitTestVisible = false };
            Canvas.SetLeft(rightNote, 10.1);
            Canvas.SetTop(rightNote, 10.4);
            icon.Children.Add(rightNote);

            return icon;
        }

        private static System.Windows.Shapes.Path PlaybackTriangle(Point first, Point second, Point third, Brush fill)
        {
            var points = new[] { first, second, third };
            var entry = new Point[3];
            var exit = new Point[3];
            const double cornerRadius = 1.1;
            for (var i = 0; i < points.Length; i++)
            {
                var vertex = points[i];
                var previous = points[(i + 2) % points.Length];
                var following = points[(i + 1) % points.Length];
                var previousLength = Math.Sqrt(Math.Pow(previous.X - vertex.X, 2) + Math.Pow(previous.Y - vertex.Y, 2));
                var followingLength = Math.Sqrt(Math.Pow(following.X - vertex.X, 2) + Math.Pow(following.Y - vertex.Y, 2));
                var radius = Math.Min(cornerRadius, Math.Min(previousLength, followingLength) * 0.25);
                entry[i] = new Point(vertex.X + (previous.X - vertex.X) * radius / previousLength,
                    vertex.Y + (previous.Y - vertex.Y) * radius / previousLength);
                exit[i] = new Point(vertex.X + (following.X - vertex.X) * radius / followingLength,
                    vertex.Y + (following.Y - vertex.Y) * radius / followingLength);
            }
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(entry[0], true, true);
                for (var i = 0; i < points.Length; i++)
                {
                    context.QuadraticBezierTo(points[i], exit[i], true, true);
                    context.LineTo(entry[(i + 1) % points.Length], true, true);
                }
            }
            geometry.Freeze();
            return new System.Windows.Shapes.Path { Data = geometry, Fill = fill, Stretch = Stretch.None, Width = 24, Height = 20,
                IsHitTestVisible = false };
        }

        private void SetControlsVisible(bool visible)
        {
            controls.IsHitTestVisible = visible;
            closeOverlay.IsHitTestVisible = visible;
            controls.Opacity = visible ? 1 : 0;
            closeOverlay.Opacity = visible ? 1 : 0;
        }

        private void SetHoverState(bool hovered)
        {
            SetControlsVisible(hovered);
            UpdatePanelBackground(hovered);
        }

        private void UpdatePanelBackground(bool hovered)
        {
            var opacity = hovered ? 0.85 : backgroundOpacity;
            panelBackground.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 27, 23, 41));
        }

        private void RequestRemoteCommand(byte command)
        {
            var handler = RemoteCommandRequested;
            if (handler != null) handler(command);
        }

        private void RequestOffsetAdjustment(double delta)
        {
            var handler = OffsetAdjustmentRequested;
            if (handler != null) handler(delta);
        }

        private void UpdatePlaybackControls()
        {
            previousTrack.IsEnabled = previousAvailable && !through;
            playPause.IsEnabled = playPauseAvailable && !through;
            nextTrack.IsEnabled = nextAvailable && !through;
            previousTrack.ToolTip = through ? "请先关闭鼠标穿透，再操作播放控制。" : previousAvailable ? "播放上一首" : "当前播放器不支持上一首";
            playPause.ToolTip = through ? "请先关闭鼠标穿透，再操作播放控制。" : playPauseAvailable ? "控制 iPhone 播放或暂停" : "当前播放器未提供播放控制";
            nextTrack.ToolTip = through ? "请先关闭鼠标穿透，再操作播放控制。" : nextAvailable ? "播放下一首" : "当前播放器不支持下一首";
        }

        public void Apply(DesktopSettings settings)
        {
            through = settings.ClickThrough;
            UpdatePlaybackControls();
            Topmost = settings.Topmost;
            previous.FontFamily = current.FontFamily = next.FontFamily = new FontFamily(settings.Font);
            karaokeFillColor = (Color)ColorConverter.ConvertFromString(settings.CurrentLineColor);
            karaokeBaseColor = (Color)ColorConverter.ConvertFromString(settings.NextLineColor);
            previous.Foreground = next.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(settings.NextLineColor);
            SetKaraokeProgress(karaokeProgress);
            current.FontSize = settings.FontSize;
            previous.FontSize = next.FontSize = Math.Max(12, settings.FontSize * 0.72);
            var minimumHeight = Math.Max(126, Math.Ceiling(settings.FontSize * 3 + 24));
            MinHeight = minimumHeight;
            if (Height < minimumHeight)
            {
                var bottom = Top + Height;
                Height = minimumHeight;
                Top = Math.Max(SystemParameters.WorkArea.Top, bottom - Height);
            }
            backgroundOpacity = settings.BackgroundOpacity;
            UpdatePanelBackground(surface.IsMouseOver);
            var window = new WindowInteropHelper(this).Handle;
            if (window != IntPtr.Zero)
            {
                var flags = GetStyle(window, -20).ToInt64() | 0x80;
                flags = through ? flags | 0x20 : flags & ~0x20;
                SetStyle(window, -20, new IntPtr(flags));
            }
        }
    }

    public sealed class ControlWindow : Window
    {
        private const int MaxLyricsRetries = 3;
        private readonly PlaybackModel model = new PlaybackModel(Stopwatch.Frequency);
        private readonly DesktopSettings settings;
        private readonly string settingsPath;
        private readonly LyricsStore store;
        private readonly LyricWindow overlay;
        private readonly DispatcherTimer ticker;
        private readonly DispatcherTimer reconnectTimer;
        private readonly DispatcherTimer lyricsRetryTimer;
        private readonly ComboBox devices = new ComboBox { MinWidth = 210 };
        private readonly TextBlock sourceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock trackTitle = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock artist = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock progress = new TextBlock();
        private readonly TextBlock lyricStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox searchTitle = new TextBox();
        private readonly TextBox searchArtist = new TextBox();
        private readonly ListBox candidates = new ListBox { Height = 112 };
        private readonly Button overlayVisibility;
        private readonly CheckBox clickThrough = new CheckBox { Content = "鼠标穿透（Ctrl + Alt + L 切换）" };
        private readonly TextBlock offsetLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 76, TextAlignment = TextAlignment.Center };
        private CancellationTokenSource sourceCancellation;
        private Task sourceTask = Task.FromResult(0);
        private bool reconnectAttemptInProgress;
        private AmsSource activeSource;
        private CancellationTokenSource queryCancellation;
        private bool lyricsRetryManual;
        private int lyricsRetryCount;
        private int lyricsRetryDelaySeconds;
        private string lyricsRetryTitle;
        private string lyricsRetryArtist;
        private long lyricsRetryRevision = -1;
        private long connectionGeneration;
        private long candidateRevision = -1;
        private Lyrics lyrics;
        private LyricRecord selected;
        private Window lyricsViewer;
        private Border[] lyricsViewerRows = new Border[0];
        private TextBlock[] lyricsViewerTexts = new TextBlock[0];
        private int highlightedViewerLine = Int32.MinValue;
        private bool closing;
        private bool initialized;
        private bool cleanupStarted;
        private bool cleanupComplete;
        private bool hotkey;
        private readonly System.Drawing.Icon trayImage;
        private readonly System.Windows.Forms.ContextMenuStrip trayMenu;
        private readonly System.Windows.Forms.NotifyIcon trayIcon;
        private readonly string requestedDevice;
        public bool HadSnapshot { get; private set; }
        public bool HasTimedLyrics { get { return lyrics != null && lyrics.Synced; } }
        public LyricWindow Overlay { get { return overlay; } }
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        public ControlWindow(string requestedDevice, string iconPath)
        {
            this.requestedDevice = requestedDevice;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricConnector");
            Directory.CreateDirectory(directory);
            settingsPath = Path.Combine(directory, "settings.json");
            settings = DesktopSettings.Defaults();
            if (File.Exists(settingsPath))
            {
                try { settings = new JavaScriptSerializer().Deserialize<DesktopSettings>(File.ReadAllText(settingsPath)); }
                catch (Exception error) { Console.WriteLine("SETTINGS: " + error.Message); }
            }
            if (String.IsNullOrEmpty(settings.CurrentLineColor)) settings.CurrentLineColor = DesktopSettings.Defaults().CurrentLineColor;
            if (String.IsNullOrEmpty(settings.NextLineColor)) settings.NextLineColor = DesktopSettings.Defaults().NextLineColor;
            store = new LyricsStore(Path.Combine(directory, "lyrics"));
            overlay = new LyricWindow(settings);
            overlay.RemoteCommandRequested += SendRemoteCommand;
            overlay.LyricsRequested += delegate { ShowText(); };
            overlay.OffsetAdjustmentRequested += delegate(double delta)
            {
                settings.Offset += delta;
                UpdateOffset();
            };
            overlay.HideRequested += delegate
            {
                settings.ShowLyrics = false;
                UpdateOverlayVisibility();
            };
            overlay.SettingsRequested += ShowControlWindow;
            overlayVisibility = Button(settings.ShowLyrics == false ? "显示窗口" : "隐藏窗口", delegate
            {
                settings.ShowLyrics = settings.ShowLyrics == false;
                UpdateOverlayVisibility();
            });
            overlayVisibility.ToolTip = settings.ShowLyrics == false ? "重新显示整个悬浮窗口" : "隐藏整个悬浮窗口";
            Title = "Lyric Connector · 桌面控制";
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
            trayImage = new System.Drawing.Icon(iconPath);
            trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("打开桌面控制").Click += delegate { ShowControlWindow(); };
            trayMenu.Items.Add("显示悬浮歌词").Click += delegate
            {
                settings.ShowLyrics = true;
                UpdateOverlayVisibility();
            };
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add("退出 Lyric Connector").Click += delegate { ExitApplication(); };
            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayImage,
                Text = "Lyric Connector · 点击打开桌面控制",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs args)
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left) ShowControlWindow();
            };
            Width = 520;
            Height = 810;
            MinWidth = 440;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 242, 248));
            Foreground = new SolidColorBrush(Color.FromRgb(39, 31, 57));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
            root.Children.Add(new TextBlock { Text = "LYRIC CONNECTOR", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(120, 83, 128)) });
            root.Children.Add(new TextBlock { Text = "音乐在手机，歌词在桌面", FontSize = 22, Margin = new Thickness(0, 6, 0, 18) });
            root.Children.Add(Row(devices, Button("刷新", async delegate { await RefreshDevicesAsync(false); }), Button("连接", async delegate { await ConnectAsync(); })));
            sourceStatus.Margin = new Thickness(0, 9, 0, 16);
            root.Children.Add(sourceStatus);
            root.Children.Add(trackTitle);
            artist.Margin = new Thickness(0, 4, 0, 6);
            root.Children.Add(artist);
            root.Children.Add(progress);
            lyricStatus.Margin = new Thickness(0, 10, 0, 14);
            root.Children.Add(lyricStatus);
            root.Children.Add(Label("歌词匹配 · 视频标题与频道名可在这里修正"));
            searchTitle.Margin = new Thickness(0, 5, 0, 5);
            searchTitle.ToolTip = "歌曲名称";
            searchArtist.ToolTip = "歌手名称；可留空以扩大检索";
            root.Children.Add(searchTitle);
            root.Children.Add(searchArtist);
            root.Children.Add(Row(Button("搜索候选", async delegate { await FindLyricsAsync(true, searchTitle.Text, searchArtist.Text, false); }), Button("导入 LRC", delegate { Import(); })));
            root.Children.Add(candidates);
            root.Children.Add(Row(Button("使用所选歌词", delegate { Choose(); }), Button("查看歌词文本", delegate { ShowText(); })));
            root.Children.Add(Label("悬浮窗口"));
            root.Children.Add(Row(new TextBlock { Text = "悬浮歌词窗口", VerticalAlignment = VerticalAlignment.Center }, overlayVisibility));
            var font = new ComboBox { IsEditable = true, Width = 226, Text = settings.Font };
            foreach (var name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei", "KaiTi" }) font.Items.Add(name);
            font.LostKeyboardFocus += delegate { if (font.Text.Trim().Length > 0) { settings.Font = font.Text.Trim(); overlay.Apply(settings); } };
            root.Children.Add(Row(new TextBlock { Text = "字体", VerticalAlignment = VerticalAlignment.Center }, font));
            var size = new Slider { Minimum = 16, Maximum = 56, Value = settings.FontSize, Width = 245, TickFrequency = 1, IsSnapToTickEnabled = true };
            size.ValueChanged += delegate { settings.FontSize = size.Value; overlay.Apply(settings); };
            root.Children.Add(Row(new TextBlock { Text = "字号", VerticalAlignment = VerticalAlignment.Center }, size));
            var opacity = new Slider { Minimum = 0, Maximum = 1, Value = settings.BackgroundOpacity, Width = 245 };
            opacity.ValueChanged += delegate { settings.BackgroundOpacity = opacity.Value; overlay.Apply(settings); };
            root.Children.Add(Row(new TextBlock { Text = "背景透明度", VerticalAlignment = VerticalAlignment.Center }, opacity));
            var currentColor = new TextBox { Text = settings.CurrentLineColor, Width = 86 };
            var nextColor = new TextBox { Text = settings.NextLineColor, Width = 86 };
            root.Children.Add(Row(new TextBlock { Text = "当前行颜色", VerticalAlignment = VerticalAlignment.Center }, currentColor,
                Button("应用", delegate { ApplyTextColor(currentColor, true); })));
            root.Children.Add(ColorPalette(currentColor, true));
            root.Children.Add(Row(new TextBlock { Text = "下一行颜色", VerticalAlignment = VerticalAlignment.Center }, nextColor,
                Button("应用", delegate { ApplyTextColor(nextColor, false); })));
            root.Children.Add(ColorPalette(nextColor, false));
            var topmost = new CheckBox { Content = "歌词置顶", IsChecked = settings.Topmost, Margin = new Thickness(0, 7, 12, 7) };
            topmost.Click += delegate { settings.Topmost = topmost.IsChecked == true; overlay.Apply(settings); };
            clickThrough.IsChecked = settings.ClickThrough;
            clickThrough.Click += delegate { settings.ClickThrough = clickThrough.IsChecked == true; overlay.Apply(settings); };
            root.Children.Add(topmost);
            root.Children.Add(clickThrough);
            root.Children.Add(Row(new TextBlock { Text = "歌词提前", VerticalAlignment = VerticalAlignment.Center },
                Button("− 0.25s", delegate { settings.Offset -= 0.25; UpdateOffset(); }), offsetLabel,
                Button("+ 0.25s", delegate { settings.Offset += 0.25; UpdateOffset(); }), Button("归零", delegate { settings.Offset = 0; UpdateOffset(); })));
            root.Children.Add(new TextBlock { Text = "正值让歌词提前。未穿透时可拖动歌词窗口，右下角可调整大小。关闭控制窗口会隐藏到系统托盘，点击托盘图标可重新打开；右键托盘图标可退出程序。", TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(112, 100, 128)), Margin = new Thickness(0, 7, 0, 0), FontSize = 11 });
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UpdateOffset();
            ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            ticker.Tick += delegate { Render(); };
            reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            reconnectTimer.Tick += async delegate { await TryAutoReconnectAsync(); };
            lyricsRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            lyricsRetryTimer.Tick += async delegate
            {
                lyricsRetryTimer.Stop();
                if (closing || !model.Ready || lyricsRetryRevision != model.Revision) return;
                await FindLyricsAsync(lyricsRetryManual, lyricsRetryTitle, lyricsRetryArtist, true);
            };
            SourceInitialized += delegate
            {
                var handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).AddHook(OnNativeMessage);
                hotkey = RegisterHotKey(handle, 1, 0x4003, 0x4C);
                if (!hotkey) Console.WriteLine("HOTKEY: Ctrl+Alt+L unavailable; use control window to unlock.");
            };
            Loaded += async delegate
            {
                if (initialized) return;
                initialized = true;
                if (settings.ShowLyrics != false) overlay.Show();
                ticker.Start();
                await RefreshDevicesAsync(true);
            };
            Closing += async delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (!closing)
                {
                    args.Cancel = true;
                    Hide();
                    return;
                }
                if (cleanupComplete) return;
                args.Cancel = true;
                if (cleanupStarted) return;
                cleanupStarted = true;
                ticker.Stop();
                reconnectTimer.Stop();
                lyricsRetryTimer.Stop();
                connectionGeneration++;
                if (queryCancellation != null) queryCancellation.Cancel();
                if (sourceCancellation != null) sourceCancellation.Cancel();
                try { await sourceTask; }
                catch (Exception error) { Console.WriteLine("SOURCE_STOP: " + error.Message); }
                settings.Left = overlay.Left; settings.Top = overlay.Top; settings.Width = overlay.Width;
                try { File.WriteAllText(settingsPath, new JavaScriptSerializer().Serialize(settings)); }
                catch (Exception error) { Console.WriteLine("SETTINGS: " + error.Message); }
                if (hotkey) UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
                trayImage.Dispose();
                overlay.Close();
                store.Dispose();
                cleanupComplete = true;
                Dispatcher.BeginInvoke(new Action(Close));
            };
        }

        private void ShowControlWindow()
        {
            if (closing) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            Activate();
        }

        internal void ExitApplication()
        {
            if (closing) return;
            closing = true;
            Close();
        }

        private static TextBlock Label(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 7) }; }
        private static StackPanel Row(params UIElement[] elements)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            foreach (var element in elements)
            {
                ((FrameworkElement)element).Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(element);
            }
            return row;
        }
        private static Button Button(string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Background = new SolidColorBrush(Color.FromRgb(232, 221, 239)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(199, 178, 210)) };
            button.Click += action;
            return button;
        }
        private WrapPanel ColorPalette(TextBox input, bool currentLine)
        {
            var choices = new[,] {
                { "暖白", "#FFF1D6" }, { "白色", "#FFFFFF" }, { "金黄", "#FFD166" }, { "珊瑚", "#FF8A65" },
                { "粉色", "#F4D7E7" }, { "玫红", "#F06292" }, { "薄荷", "#7DE2D1" }, { "青色", "#72DDF7" },
                { "天蓝", "#8AB4F8" }, { "紫色", "#C4A1FF" }
            };
            var palette = new WrapPanel { Margin = new Thickness(92, 0, 0, 3) };
            for (var index = 0; index < choices.GetLength(0); index++)
            {
                var selectedColor = choices[index, 1];
                var label = choices[index, 0] + " " + selectedColor;
                var color = (Color)ColorConverter.ConvertFromString(selectedColor);
                var swatch = new Button
                {
                    Width = 25,
                    Height = 25,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 7, 5),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(120, 109, 130)),
                    BorderThickness = new Thickness(1),
                    ToolTip = label
                };
                AutomationProperties.SetName(swatch, label);
                swatch.Click += delegate { input.Text = selectedColor; ApplyTextColor(input, currentLine); };
                palette.Children.Add(swatch);
            }
            return palette;
        }
        private IntPtr OnNativeMessage(IntPtr window, int message, IntPtr first, IntPtr second, ref bool handled)
        {
            if (message == 0x312 && first.ToInt32() == 1)
            {
                settings.ClickThrough = !settings.ClickThrough;
                clickThrough.IsChecked = settings.ClickThrough;
                overlay.Apply(settings);
                handled = true;
            }
            return IntPtr.Zero;
        }
        private void UpdateOffset() { offsetLabel.Text = settings.Offset.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " s"; }
        private void UpdateOverlayVisibility()
        {
            var visible = settings.ShowLyrics != false;
            if (visible)
            {
                if (!overlay.IsVisible) overlay.Show();
            }
            else overlay.Hide();
            overlayVisibility.Content = visible ? "隐藏窗口" : "显示窗口";
            overlayVisibility.ToolTip = visible ? "隐藏整个悬浮窗口" : "重新显示整个悬浮窗口";
        }

        private void ApplyTextColor(TextBox input, bool currentLine)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(input.Text.Trim());
                if (currentLine) settings.CurrentLineColor = color.ToString();
                else settings.NextLineColor = color.ToString();
                input.Text = color.ToString();
                overlay.Apply(settings);
                lyricStatus.Text = "歌词颜色已更新，关闭窗口后保存。颜色可使用 #RRGGBB 格式。";
            }
            catch (FormatException) { lyricStatus.Text = "颜色格式无效，请使用 #RRGGBB（例如 #F4D7E7）。"; }
        }

        private async Task RefreshDevicesAsync(bool autoConnect)
        {
            sourceStatus.Text = "正在读取已配对设备…";
            try
            {
                var found = await AmsSource.DevicesAsync(CancellationToken.None);
                if (closing) return;
                devices.ItemsSource = found;
                var target = found.FirstOrDefault(item => item.Id == settings.DeviceId);
                if (target == null && requestedDevice.Length > 0) target = found.SingleOrDefault(item => item.Name == requestedDevice);
                devices.SelectedItem = target;
                sourceStatus.Text = found.Count == 0 ? "未发现已配对的蓝牙 LE 设备，请先在 Windows 中连接 iPhone。" : "选择你的 iPhone 后连接。";
                if (autoConnect)
                {
                    if (target != null) await ConnectAsync();
                    else StartAutoReconnect();
                }
            }
            catch (Exception error)
            {
                sourceStatus.Text = "设备读取失败：" + error.Message;
                if (autoConnect) StartAutoReconnect();
            }
        }

        private void StartAutoReconnect()
        {
            if (closing || (String.IsNullOrEmpty(settings.DeviceId) && requestedDevice.Length == 0)) return;
            sourceStatus.Text = "等待 iPhone 蓝牙可用，将自动重连。";
            if (!reconnectTimer.IsEnabled) reconnectTimer.Start();
        }

        private async Task TryAutoReconnectAsync()
        {
            if (closing || activeSource != null || reconnectAttemptInProgress) return;
            reconnectAttemptInProgress = true;
            var generation = connectionGeneration;
            try
            {
                var found = await AmsSource.DevicesAsync(CancellationToken.None);
                if (closing || activeSource != null || generation != connectionGeneration) return;
                var target = found.FirstOrDefault(item => item.Id == settings.DeviceId);
                if (target == null && requestedDevice.Length > 0) target = found.SingleOrDefault(item => item.Name == requestedDevice);
                if (target == null)
                {
                    sourceStatus.Text = "等待 iPhone 蓝牙可用，将继续自动重连。";
                    return;
                }
                devices.ItemsSource = found;
                devices.SelectedItem = target;
                sourceStatus.Text = "已检测到 iPhone，正在自动重连…";
                await ConnectAsync();
            }
            catch (Exception error)
            {
                if (!closing && generation == connectionGeneration)
                {
                    Console.WriteLine("AMS_RECONNECT_ERROR: " + error.Message);
                    sourceStatus.Text = "iPhone 自动重连暂时失败，正在重试。";
                }
            }
            finally { reconnectAttemptInProgress = false; }
        }

        private async Task ConnectAsync()
        {
            var device = devices.SelectedItem as DeviceChoice;
            if (device == null) { sourceStatus.Text = "请先选择你的 iPhone。"; return; }
            reconnectTimer.Stop();
            var generation = ++connectionGeneration;
            activeSource = null;
            UpdatePlaybackControls();
            if (sourceCancellation != null) sourceCancellation.Cancel();
            await sourceTask;
            if (generation != connectionGeneration || closing) return;
            sourceCancellation = new CancellationTokenSource();
            settings.DeviceId = device.Id;
            Accept(new SourceUpdate { Kind = UpdateKind.Transition, Revision = model.Revision + 1, Message = "正在连接 " + device.Name + "…" });
            var source = new AmsSource(device, update => Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!closing && generation == connectionGeneration) Accept(update);
            })), model.Revision);
            activeSource = source;
            sourceTask = Task.Run(() => source.RunAsync(sourceCancellation.Token));
        }

        private void Accept(SourceUpdate update)
        {
            if (update.Revision < model.Revision) return;
            if (update.Kind == UpdateKind.Disconnected) activeSource = null;
            var changed = model.Accept(update);
            sourceStatus.Text = model.Status + (model.Player.Length > 0 ? " · " + model.Player : "");
            if (update.Kind == UpdateKind.Disconnected) StartAutoReconnect();
            else if (update.Kind == UpdateKind.Snapshot) reconnectTimer.Stop();
            Console.WriteLine("AMS: " + update.Kind + " r" + update.Revision + " " + model.Status);
            if (changed || update.Kind == UpdateKind.Transition || update.Kind == UpdateKind.Disconnected)
            {
                if (queryCancellation != null) queryCancellation.Cancel();
                lyricsRetryTimer.Stop();
                lyricsRetryCount = 0;
                lyricsRetryDelaySeconds = 0;
                lyrics = null; selected = null; candidates.ItemsSource = null; candidateRevision = -1;
                lyricStatus.Text = "";
                RefreshLyricsViewer();
            }
            if (model.Ready)
            {
                var query = SongQuery.Infer(model.Track);
                trackTitle.Text = query.Title;
                trackTitle.ToolTip = "手机原始标题：" + model.Track.Title;
                artist.Text = query.Artist + (query.Inferred ? " · 从视频标题提取，需核对版本" : "");
                artist.ToolTip = "手机艺人字段：" + model.Track.Artist;
                HadSnapshot = true;
                if (changed)
                {
                    searchTitle.Text = query.Title;
                    searchArtist.Text = query.Artist;
                    var pending = FindLyricsAsync(false, "", "", false);
                }
            }
            else { trackTitle.Text = "等待歌曲信息"; artist.Text = ""; progress.Text = ""; }
            Render();
        }

        private async Task FindLyricsAsync(bool manual, string requestedTitle, string requestedArtist, bool retryAttempt)
        {
            lyricsRetryTimer.Stop();
            if (!retryAttempt)
            {
                lyricsRetryCount = 0;
                lyricsRetryDelaySeconds = 0;
            }
            if (!model.Ready) { lyricStatus.Text = "歌曲就绪后可检索歌词。"; return; }
            var title = manual ? requestedTitle.Trim() : "";
            var artistName = manual ? requestedArtist.Trim() : "";
            if (manual && title.Length == 0) { lyricStatus.Text = "请输入歌曲名称。"; return; }
            if (queryCancellation != null) queryCancellation.Cancel();
            queryCancellation = new CancellationTokenSource();
            var token = queryCancellation.Token;
            var revision = model.Revision;
            var track = model.Track;
            candidateRevision = -1;
            candidates.ItemsSource = null;
            lyricStatus.Text = "正在查询歌词…";
            try
            {
                var result = manual
                    ? new LyricResult { Candidates = LyricMatcher.Rank(track, title, artistName,
                        await store.SearchAsync(title, artistName, token)) }
                    : await store.FindAsync(track, token);
                if (token.IsCancellationRequested || closing || revision != model.Revision) return;
                lyricsRetryCount = 0;
                lyricsRetryDelaySeconds = 0;
                if (result.Selected != null) SetLyrics(result.Selected);
                else
                {
                    candidateRevision = revision;
                    candidates.ItemsSource = result.Candidates;
                    var query = SongQuery.Infer(track);
                    var inference = !manual && query.Inferred ? "已提取歌名《" + query.Title + "》。" : "";
                    var expanded = !manual && result.ExpandedSearch ? "曲名直接匹配未命中，已扩大到专辑 / 歌手候选。" : "";
                    var bestIndex = result.Candidates.FindIndex(match => match.Record.instrumental || match.Record.Document().PlainText.Length > 0);
                    if (bestIndex >= 0)
                    {
                        var best = result.Candidates[bestIndex];
                        candidates.SelectedItem = best;
                        if (best.ConfidenceScore < 80)
                            lyricStatus.Text = inference + expanded + "找到候选，但最高置信度为 " + best.ConfidenceScore
                                + "%（" + best.Confidence + "），未自动应用；请核对标题和版本后点击“使用所选歌词”。";
                        else
                        {
                            SetLyrics(best.Record);
                            var closeAlternative = result.Candidates.Where((match, index) => index != bestIndex)
                                .Any(match => best.ConfidenceScore - match.ConfidenceScore < 8);
                            var lyricType = best.Record.instrumental ? "纯音乐" : lyrics.Synced ? "同步歌词" : "纯文本歌词";
                            lyricStatus.Text = inference + expanded + "已自动选择《" + best.Record.trackName + "》的" + lyricType + "（相似度 "
                                + best.Score + "% · 置信度 " + best.ConfidenceScore + "%（" + best.Confidence + "））" + (closeAlternative
                                    ? "；候选列表中还有相近结果，可随时切换。" : "。");
                        }
                    }
                    else lyricStatus.Text = inference + expanded + (result.Candidates.Count == 0
                        ? "未找到歌词。可修正标题 / 歌手，或导入本地 LRC。"
                        : "找到候选，但没有可显示的歌词内容；请修正检索信息或导入本地 LRC。");
                }
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested && !closing && revision == model.Revision)
                    HandleTransientLyricsFailure(manual, title, artistName, retryAttempt, revision, "歌词请求超时");
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && !closing && revision == model.Revision)
                {
                    Console.WriteLine("LYRICS_ERROR: " + error.Message);
                    var serviceError = error as System.Net.Http.HttpRequestException;
                    var serviceUnavailable = serviceError != null && serviceError.Message.StartsWith("歌词服务", StringComparison.Ordinal);
                    if (serviceUnavailable)
                        HandleTransientLyricsFailure(manual, title, artistName, retryAttempt, revision, "歌词服务暂时不可用");
                    else lyricStatus.Text = "歌词查询暂时失败，请检查网络后重试，或导入本地 LRC。";
                }
            }
        }

        private void HandleTransientLyricsFailure(bool manual, string title, string artistName, bool retryAttempt, long revision, string reason)
        {
            if (retryAttempt) lyricsRetryCount++;
            if (lyricsRetryCount >= MaxLyricsRetries)
            {
                lyricStatus.Text = reason + "，已自动重试 " + MaxLyricsRetries + " 次；请手动搜索候选或导入本地 LRC。";
                return;
            }
            ScheduleLyricsRetry(manual, title, artistName, revision);
            lyricStatus.Text = reason + "，" + lyricsRetryDelaySeconds + " 秒后自动重试（" + (lyricsRetryCount + 1)
                + "/" + MaxLyricsRetries + "）。";
        }

        private void ScheduleLyricsRetry(bool manual, string title, string artistName, long revision)
        {
            lyricsRetryManual = manual;
            lyricsRetryTitle = title;
            lyricsRetryArtist = artistName;
            lyricsRetryRevision = revision;
            lyricsRetryDelaySeconds = 10 * (1 << lyricsRetryCount);
            lyricsRetryTimer.Interval = TimeSpan.FromSeconds(lyricsRetryDelaySeconds);
            lyricsRetryTimer.Start();
        }

        private void SetLyrics(LyricRecord record)
        {
            lyricsRetryTimer.Stop();
            lyricsRetryCount = 0;
            lyricsRetryDelaySeconds = 0;
            store.Save(model.Track, record);
            DisplayLyrics(record);
            lyricStatus.Text = record.Source + " · " + (record.instrumental ? "纯音乐" : lyrics.Synced ? lyrics.Lines.Count + " 行同步歌词" : "纯文本歌词，无时间轴");
        }
        private void DisplayLyrics(LyricRecord record)
        {
            selected = record;
            lyrics = record.Document();
            Console.WriteLine("LYRICS: " + record.Source + " · " + (record.instrumental ? "纯音乐" : lyrics.Synced ? lyrics.Lines.Count + " 行同步歌词" : "纯文本歌词，无时间轴"));
            RefreshLyricsViewer();
            Render();
        }
        private void Choose()
        {
            if (!model.Ready || candidateRevision != model.Revision) return;
            var match = candidates.SelectedItem as LyricMatch;
            if (match == null) { lyricStatus.Text = "先选择一个候选。"; return; }
            try { if (queryCancellation != null) queryCancellation.Cancel(); SetLyrics(match.Record); }
            catch (Exception error) { lyricStatus.Text = "保存歌词失败：" + error.Message; }
        }
        private void Import()
        {
            if (!model.Ready) { lyricStatus.Text = "歌曲就绪后可导入对应歌词。"; return; }
            var revision = model.Revision;
            var dialog = new OpenFileDialog { Filter = "歌词文件 (*.lrc;*.txt)|*.lrc;*.txt|所有文件|*.*" };
            if (dialog.ShowDialog(this) != true || revision != model.Revision || !model.Ready) return;
            try
            {
                if (queryCancellation != null) queryCancellation.Cancel();
                SetLyrics(store.Import(model.Track, dialog.FileName));
            }
            catch (Exception error) { lyricStatus.Text = "歌词导入失败：" + error.Message; }
        }
        private void ShowText()
        {
            if (lyrics == null) { lyricStatus.Text = "尚未选择歌词。"; return; }
            if (lyricsViewer != null && lyricsViewer.IsVisible) { lyricsViewer.Activate(); return; }
            lyricsViewer = new Window { Title = "歌词文本", Width = 620, Height = 640, Owner = this, Icon = this.Icon };
            lyricsViewer.Closed += delegate
            {
                lyricsViewer = null;
                lyricsViewerRows = new Border[0];
                lyricsViewerTexts = new TextBlock[0];
                highlightedViewerLine = Int32.MinValue;
            };
            RefreshLyricsViewer();
            lyricsViewer.Show();
            Render();
        }

        private void RefreshLyricsViewer()
        {
            if (lyricsViewer == null) return;
            highlightedViewerLine = Int32.MinValue;
            lyricsViewerRows = new Border[0];
            lyricsViewerTexts = new TextBlock[0];
            if (lyrics == null)
            {
                lyricsViewer.Content = new TextBlock { Text = "等待当前歌曲歌词…", Margin = new Thickness(16),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                return;
            }
            if (!lyrics.Synced)
            {
                lyricsViewer.Content = new TextBox { Text = lyrics.PlainText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
                return;
            }

            var rows = new StackPanel();
            lyricsViewerRows = new Border[lyrics.Lines.Count];
            lyricsViewerTexts = new TextBlock[lyrics.Lines.Count];
            for (var i = 0; i < lyrics.Lines.Count; i++)
            {
                var text = new TextBlock { Text = lyrics.Lines[i].Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 5, 8, 5) };
                var row = new Border { Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(3, 0, 0, 0), Child = text };
                lyricsViewerRows[i] = row;
                lyricsViewerTexts[i] = text;
                rows.Children.Add(row);
            }
            lyricsViewer.Content = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(12) };
            UpdateLyricsViewer(CurrentLyricIndex());
        }

        private int CurrentLyricIndex()
        {
            if (!model.Ready || lyrics == null || !lyrics.Synced) return -1;
            var position = model.Position(Stopwatch.GetTimestamp());
            if (model.Track.Duration > 0 && position >= model.Track.Duration) return -1;
            var index = lyrics.IndexAt(position, settings.Offset);
            return index >= 0 && !String.IsNullOrWhiteSpace(lyrics.Lines[index].Text) ? index : -1;
        }

        private void UpdateLyricsViewer(int index)
        {
            if (lyricsViewer == null || !lyricsViewer.IsVisible || lyricsViewerRows.Length == 0) return;
            if (index < 0 || index >= lyricsViewerRows.Length) index = -1;
            if (index == highlightedViewerLine) return;
            if (highlightedViewerLine >= 0 && highlightedViewerLine < lyricsViewerRows.Length)
            {
                lyricsViewerRows[highlightedViewerLine].Background = Brushes.Transparent;
                lyricsViewerRows[highlightedViewerLine].BorderBrush = Brushes.Transparent;
                lyricsViewerTexts[highlightedViewerLine].ClearValue(TextBlock.ForegroundProperty);
                lyricsViewerTexts[highlightedViewerLine].FontWeight = FontWeights.Normal;
            }
            highlightedViewerLine = index;
            if (index < 0) return;
            var color = (Color)ColorConverter.ConvertFromString(settings.CurrentLineColor);
            lyricsViewerRows[index].Background = new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B));
            lyricsViewerRows[index].BorderBrush = new SolidColorBrush(color);
            lyricsViewerTexts[index].Foreground = new SolidColorBrush(color);
            lyricsViewerTexts[index].FontWeight = FontWeights.SemiBold;
            lyricsViewerRows[index].BringIntoView();
        }

        private string OverlayConnectionHint()
        {
            if (model.Status == "等待连接 iPhone") return "请在桌面控制窗口连接 iPhone";
            if (model.Status.StartsWith("正在连接 ", StringComparison.Ordinal)) return "正在连接 iPhone";
            if (model.Status == "正在切换歌曲") return "iPhone 已连接 · 正在同步歌曲信息";
            if (model.Status == "正在切换播放器") return "iPhone 已连接 · 正在同步播放器信息";
            if (model.Status == "正在校准歌曲信息") return "iPhone 已连接 · 正在校准歌曲信息";
            if (model.Status == "等待手机提供有效进度") return "iPhone 已连接 · 正在等待播放进度";
            if (model.Status.IndexOf("连接", StringComparison.Ordinal) >= 0
                || model.Status.IndexOf("蓝牙", StringComparison.Ordinal) >= 0)
                return reconnectTimer.IsEnabled ? "iPhone 连接中断 · 正在自动重连" : "iPhone 连接中断 · 请检查蓝牙";
            if (reconnectTimer.IsEnabled) return "iPhone 播放信息暂不可用 · 正在自动重连";
            return "正在等待 iPhone 播放信息";
        }

        private string OverlayLyricsHint()
        {
            var status = lyricStatus.Text;
            if (status == "正在查询歌词…") return "正在搜索歌词";
            if ((status.StartsWith("歌词服务暂时不可用", StringComparison.Ordinal)
                || status.StartsWith("歌词请求超时", StringComparison.Ordinal))
                && lyricsRetryCount >= MaxLyricsRetries)
                return "已重试 " + MaxLyricsRetries + " 次 · 请在桌面手动搜索候选";
            if (status.StartsWith("歌词服务暂时不可用", StringComparison.Ordinal))
            {
                if (lyricsRetryTimer.IsEnabled) return "歌词服务不可用 · " + lyricsRetryDelaySeconds + " 秒后重试 "
                    + (lyricsRetryCount + 1) + "/" + MaxLyricsRetries;
                return "歌词服务不可用 · 可在桌面重试";
            }
            if (status.StartsWith("歌词请求超时", StringComparison.Ordinal))
            {
                if (lyricsRetryTimer.IsEnabled) return "歌词请求超时 · " + lyricsRetryDelaySeconds + " 秒后重试 "
                    + (lyricsRetryCount + 1) + "/" + MaxLyricsRetries;
                return "歌词请求超时 · 可在桌面重试或导入 LRC";
            }
            if (status.IndexOf("找到候选，但最高置信度", StringComparison.Ordinal) >= 0) return "找到低置信度候选 · 请在桌面核对";
            if (status.IndexOf("没有可显示的歌词内容", StringComparison.Ordinal) >= 0) return "候选歌词无可显示内容 · 可在桌面修正检索信息或导入 LRC";
            if (candidates.Items.Count > 0) return "有歌词候选 · 请在桌面核对选择";
            if (status.IndexOf("未找到歌词", StringComparison.Ordinal) >= 0) return "未找到歌词 · 可在桌面修正信息或导入 LRC";
            if (status.StartsWith("歌词查询暂时失败", StringComparison.Ordinal)) return "歌词查询失败 · 可在桌面重试或导入 LRC";
            if (status.StartsWith("歌词导入失败", StringComparison.Ordinal)) return "歌词导入失败 · 可在桌面查看原因";
            if (status.StartsWith("保存歌词失败", StringComparison.Ordinal)) return "歌词保存失败 · 可在桌面查看原因";
            if (status.StartsWith("请输入歌曲名称", StringComparison.Ordinal)) return "请在桌面输入歌曲名称后搜索";
            return "等待歌词 · 可在桌面搜索或导入 LRC";
        }

        private void Render()
        {
            UpdatePlaybackControls();
            if (!model.Ready)
            {
                UpdateLyricsViewer(-1);
                overlay.SetProgress(0, 0);
                overlay.SetKaraokeProgress(1);
                overlay.SetText("", model.Status, OverlayConnectionHint());
                return;
            }
            var position = model.Position(Stopwatch.GetTimestamp());
            overlay.SetProgress(position, model.Track.Duration);
            var title = trackTitle.Text;
            progress.Text = model.Track.Duration > 0
                ? String.Format(CultureInfo.InvariantCulture, "{0:mm\\:ss} / {1:mm\\:ss}", TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(model.Track.Duration))
                : String.Format(CultureInfo.InvariantCulture, "已播放 {0:mm\\:ss}", TimeSpan.FromSeconds(position));
            if (selected != null && selected.instrumental) { UpdateLyricsViewer(-1); overlay.SetKaraokeProgress(1); overlay.SetText("", title, "纯音乐"); return; }
            if (lyrics == null) { UpdateLyricsViewer(-1); overlay.SetKaraokeProgress(1); overlay.SetText("", title, OverlayLyricsHint()); return; }
            if (!lyrics.Synced) { UpdateLyricsViewer(-1); overlay.SetKaraokeProgress(1); overlay.SetText("", title, "此歌词为纯文本 · 可在桌面查看全文"); return; }
            var index = lyrics.IndexAt(position, settings.Offset);
            var previousLine = index > 0 ? lyrics.Lines[index - 1].Text : "";
            var currentLine = index < 0 ? title : lyrics.Lines[index].Text;
            var nextLine = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Text : "";
            var ended = model.Track.Duration > 0 && position >= model.Track.Duration;
            if (ended || String.IsNullOrWhiteSpace(currentLine))
            {
                UpdateLyricsViewer(-1);
                if (ended && index >= 0) previousLine = lyrics.Lines[index].Text;
                currentLine = title;
                overlay.SetKaraokeProgress(1);
            }
            else
            {
                UpdateLyricsViewer(index);
                overlay.SetKaraokeProgress(index < 0 ? 1 : lyrics.ProgressAt(index, position, model.Track.Duration, settings.Offset));
            }
            overlay.SetText(previousLine, currentLine, nextLine);
        }

        private void UpdatePlaybackControls()
        {
            var source = activeSource;
            var connected = source != null && model.Ready;
            var paused = model.Paused;
            var toggle = connected && source.SupportsRemoteCommand(RemoteCommand.TogglePlayPause);
            var command = toggle ? RemoteCommand.TogglePlayPause : paused ? RemoteCommand.Play : RemoteCommand.Pause;
            var canToggle = connected && (toggle || source.SupportsRemoteCommand(command));
            overlay.SetPlaybackControls(paused,
                connected && source.SupportsRemoteCommand(RemoteCommand.PreviousTrack), command, canToggle,
                connected && source.SupportsRemoteCommand(RemoteCommand.NextTrack));
        }

        private async void SendRemoteCommand(byte command)
        {
            var source = activeSource;
            if (source == null || !model.Ready) return;
            try
            {
                await source.SendRemoteCommandAsync(command);
                if (!closing && source == activeSource) lyricStatus.Text = "播放操作已发送到 iPhone，等待播放器更新状态。";
            }
            catch (Exception error)
            {
                if (!closing && source == activeSource)
                {
                    Console.WriteLine("AMS_REMOTE_ERROR: " + error.Message);
                    lyricStatus.Text = "iPhone 暂未执行此操作，请确认播放器仍在播放后重试。";
                }
            }
        }

        public void Capture(string path)
        {
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(path)) encoder.Save(output);
            overlay.UpdateLayout();
            bitmap = new RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(overlay);
            encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(Path.GetDirectoryName(path), "desktop-overlay.png"))) encoder.Save(output);
            Console.WriteLine("SMOKE: snapshot=" + HadSnapshot + " timedLyrics=" + HasTimedLyrics);
        }
    }

    public static class Entry
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        public static void Run(string deviceName, int smokeSeconds, string screenshot, string iconPath)
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("LyricConnector.Desktop"));
            var application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new ControlWindow(deviceName, iconPath);
            if (smokeSeconds > 0)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(smokeSeconds) };
                timer.Tick += delegate { timer.Stop(); window.Capture(screenshot); window.ExitApplication(); };
                timer.Start();
            }
            application.Run(window);
        }
    }
}
