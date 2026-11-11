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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LyricConnector
{
    internal sealed class DeviceLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var device = value as DeviceChoice;
            if (device == null) return "";
            if (!String.IsNullOrWhiteSpace(device.Name)) return device.Name;
            return device.Id.Length > 24 ? device.Id.Substring(0, 12) + "…" + device.Id.Substring(device.Id.Length - 8) : device.Id;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class CandidateFieldConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var record = values[0] as LyricRecord;
            if (record == null) return DependencyProperty.UnsetValue;
            switch ((string)parameter)
            {
                case "duration": return record.duration.HasValue && record.duration.Value > 0
                    ? TimeSpan.FromSeconds(record.duration.Value).ToString(@"mm\:ss", CultureInfo.InvariantCulture) : "—";
                case "kind": return record.instrumental ? "纯音乐" : record.Document().Synced ? "同步" : "文本";
                case "current": return Object.ReferenceEquals(record, values[1]) ? Visibility.Visible : Visibility.Hidden;
                default: throw new ArgumentException("Unknown candidate field");
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class LyricWindow : Window
    {
        private readonly Border surface;
        private readonly Border panelBackground;
        private readonly LyricTextView lyricText = new LyricTextView();
        private readonly Button previousTrack;
        private readonly Button playPause;
        private readonly Button nextTrack;
        private readonly Button closeOverlay;
        private readonly Thumb resizeHandle;
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
            ResizeMode = ResizeMode.NoResize;
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
            lyricText.Apply(settings);
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
            var lyricsViewButton = PlaybackButton("查看歌词全文", LyricsGlyph());
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
            text.Children.Add(lyricText);
            text.Children.Add(progressTrack);
            text.Children.Add(controls);
            content.Children.Add(text);
            closeOverlay.HorizontalAlignment = HorizontalAlignment.Right;
            closeOverlay.VerticalAlignment = VerticalAlignment.Top;
            closeOverlay.Margin = new Thickness(0, 2, 2, 0);
            closeOverlay.Opacity = 0;
            closeOverlay.IsHitTestVisible = false;
            content.Children.Add(closeOverlay);
            resizeHandle = new Thumb
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                Opacity = 0,
                IsHitTestVisible = false,
                ToolTip = "拖动以调整窗口大小",
                Template = ResizeHandleTemplate()
            };
            resizeHandle.DragDelta += delegate(object sender, DragDeltaEventArgs args)
            {
                Width = Math.Max(MinWidth, Width + args.HorizontalChange);
                Height = Math.Max(MinHeight, Height + args.VerticalChange);
            };
            content.Children.Add(resizeHandle);
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
            lyricText.SetText(before, active, after);
        }

        public void SetKaraokeProgress(double progress)
        {
            lyricText.SetKaraokeProgress(progress);
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

        private static ControlTemplate ResizeHandleTemplate()
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(3, 13), false, false);
                context.LineTo(new Point(13, 3), true, false);
                context.BeginFigure(new Point(8, 15), false, false);
                context.LineTo(new Point(15, 8), true, false);
                context.BeginFigure(new Point(13, 15), false, false);
                context.LineTo(new Point(15, 13), true, false);
            }
            geometry.Freeze();

            var template = new ControlTemplate(typeof(Thumb));
            var hitArea = new FrameworkElementFactory(typeof(Border));
            hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var glyph = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            glyph.SetValue(FrameworkElement.WidthProperty, 14.0);
            glyph.SetValue(FrameworkElement.HeightProperty, 14.0);
            glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            glyph.SetValue(System.Windows.Shapes.Path.DataProperty, geometry);
            glyph.SetValue(System.Windows.Shapes.Path.StrokeProperty, new SolidColorBrush(Color.FromRgb(248, 244, 255)));
            glyph.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.3);
            glyph.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            glyph.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            glyph.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
            glyph.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Fill);
            hitArea.AppendChild(glyph);
            template.VisualTree = hitArea;
            return template;
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

        private static Canvas LyricsGlyph()
        {
            var icon = new Canvas { Width = 24, Height = 20, IsHitTestVisible = false };
            var ink = new SolidColorBrush(Color.FromRgb(132, 86, 186));

            var bubbleGeometry = new StreamGeometry();
            using (var context = bubbleGeometry.Open())
            {
                context.BeginFigure(new Point(7.6, 3.2), false, true);
                context.LineTo(new Point(16.4, 3.2), true, false);
                context.ArcTo(new Point(19.2, 6.0), new Size(2.8, 2.8), 0, false, SweepDirection.Clockwise, true, false);
                context.LineTo(new Point(19.2, 10.8), true, false);
                context.ArcTo(new Point(16.4, 13.6), new Size(2.8, 2.8), 0, false, SweepDirection.Clockwise, true, false);
                context.LineTo(new Point(9.2, 13.6), true, false);
                context.BezierTo(new Point(8.0, 13.6), new Point(6.6, 14.8), new Point(5.8, 16.6), true, false);
                context.BezierTo(new Point(5.5, 15.2), new Point(4.8, 13.2), new Point(4.8, 10.8), true, false);
                context.LineTo(new Point(4.8, 6.0), true, false);
                context.ArcTo(new Point(7.6, 3.2), new Size(2.8, 2.8), 0, false, SweepDirection.Clockwise, true, false);
            }
            bubbleGeometry.Freeze();

            var bubble = new System.Windows.Shapes.Path
            {
                Data = bubbleGeometry,
                Stroke = ink,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 24,
                Height = 20,
                Stretch = Stretch.None,
                IsHitTestVisible = false
            };
            icon.Children.Add(bubble);

            var quotesGeometry = new StreamGeometry();
            using (var context = quotesGeometry.Open())
            {
                context.BeginFigure(new Point(9.2, 6.2), true, true);
                context.ArcTo(new Point(11.2, 6.2), new Size(1.0, 1.0), 0, true, SweepDirection.Clockwise, true, false);
                context.BezierTo(new Point(11.2, 7.3), new Point(10.8, 8.4), new Point(9.8, 9.3), true, false);
                context.BezierTo(new Point(10.2, 8.5), new Point(10.5, 7.8), new Point(10.2, 7.2), true, false);
                context.ArcTo(new Point(9.2, 6.2), new Size(1.0, 1.0), 0, false, SweepDirection.Clockwise, true, false);

                context.BeginFigure(new Point(12.8, 6.2), true, true);
                context.ArcTo(new Point(14.8, 6.2), new Size(1.0, 1.0), 0, true, SweepDirection.Clockwise, true, false);
                context.BezierTo(new Point(14.8, 7.3), new Point(14.4, 8.4), new Point(13.4, 9.3), true, false);
                context.BezierTo(new Point(13.8, 8.5), new Point(14.1, 7.8), new Point(13.8, 7.2), true, false);
                context.ArcTo(new Point(12.8, 6.2), new Size(1.0, 1.0), 0, false, SweepDirection.Clockwise, true, false);
            }
            quotesGeometry.Freeze();

            var quotes = new System.Windows.Shapes.Path
            {
                Data = quotesGeometry,
                Fill = ink,
                Width = 24,
                Height = 20,
                Stretch = Stretch.None,
                IsHitTestVisible = false
            };
            icon.Children.Add(quotes);

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
            var interactiveHover = hovered && !through;
            SetControlsVisible(interactiveHover);
            resizeHandle.Opacity = interactiveHover ? 1 : 0;
            resizeHandle.IsHitTestVisible = interactiveHover;
            UpdatePanelBackground(interactiveHover);
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
            lyricText.Apply(settings);
            var minimumHeight = Math.Max(126, Math.Ceiling(settings.FontSize * 3 + 24));
            MinHeight = minimumHeight;
            if (Height < minimumHeight)
            {
                var bottom = Top + Height;
                Height = minimumHeight;
                Top = Math.Max(SystemParameters.WorkArea.Top, bottom - Height);
            }
            backgroundOpacity = settings.BackgroundOpacity;
            SetHoverState(surface.IsMouseOver);
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
        private const double CandidateRowHeight = 40;
        private const int MaxVisibleCandidates = 5;
        private readonly PlaybackModel model = new PlaybackModel(Stopwatch.Frequency);
        private readonly DesktopSettings settings;
        private readonly string settingsPath;
        private readonly LyricsStore store;
        private readonly LyricWindow overlay;
        private readonly DispatcherTimer ticker;
        private readonly DispatcherTimer reconnectTimer;
        private readonly DispatcherTimer lyricsRetryTimer;
        private readonly ComboBox devices = new ComboBox { MinWidth = 140 };
        private readonly TextBlock sourceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock trackTitle = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock artist = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock progress = new TextBlock();
        private readonly TextBlock playbackConnectionStatus = new TextBlock();
        private readonly Border playbackConnectionBadge = new Border();
        private readonly TextBlock lyricStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox searchTitle = new TextBox();
        private readonly TextBox searchArtist = new TextBox();
        private readonly ListBox candidates = new ListBox { MaxHeight = CandidateRowHeight * MaxVisibleCandidates };
        private readonly TextBlock candidateEmpty = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        private readonly LyricTextView preview = new LyricTextView();
        private readonly Border previewBackground = new Border { CornerRadius = new CornerRadius(8) };
        private readonly TextBlock previewLabel = new TextBlock();
        private readonly ProgressBar playbackProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, IsHitTestVisible = false, Visibility = Visibility.Hidden };
        private readonly TextBlock lyricSource = new TextBlock();
        private readonly TextBlock connectionState = new TextBlock();
        private readonly StackPanel connectionDetails = new StackPanel();
        private readonly Button manageConnection;
        private bool connectionDetailsExpanded;
        private readonly Button refreshDevices;
        private readonly Button connect;
        private readonly Button search;
        private readonly Button choose;
        private readonly Button viewLyrics;
        private readonly Button importLyrics;
        private readonly ToggleButton overlayVisibility;
        private readonly Button musicTab;
        private readonly Button displayTab;
        private readonly ScrollViewer musicPage;
        private readonly ScrollViewer displayPage;
        private readonly Grid settingsPages = new Grid();
        private readonly Border settingsTabs;
        private readonly Border settingsDivider = new Border { Width = 1, Background = Ink("#E3DEE9"), Margin = new Thickness(16, 0, 16, 0) };
        private bool displaySettingsSelected;
        private readonly Border connectionGuidePanel;
        private readonly CheckBox clickThrough = new CheckBox { Content = "鼠标穿透（Ctrl + Alt + L 切换）" };
        private readonly TextBlock offsetLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 76, TextAlignment = TextAlignment.Center };
        private CancellationTokenSource sourceCancellation;
        private Task sourceTask = Task.FromResult(0);
        private bool reconnectAttemptInProgress;
        private bool manuallyDisconnected;
        private bool connectionBusy;
        private bool refreshingDevices;
        private bool searchingLyrics;
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

        public ControlWindow(string requestedDevice, string iconPath, string directory)
        {
            this.requestedDevice = requestedDevice;
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
            overlayVisibility = new ToggleButton { Content = "显示悬浮窗口", IsChecked = settings.ShowLyrics != false, Style = ControlTheme.Switch };
            overlayVisibility.Click += delegate
            {
                settings.ShowLyrics = overlayVisibility.IsChecked == true;
                UpdateOverlayVisibility();
            };
            overlayVisibility.ToolTip = "显示或隐藏桌面悬浮歌词";
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
            Width = 700;
            Height = 790;
            MinWidth = 620;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 74, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(5), UseAeroCaptionButtons = false
            });
            Background = Ink("#FAF8FF");
            Foreground = Ink("#272235");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            UseLayoutRounding = true;
            var root = new Grid { Margin = new Thickness(24, 16, 24, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var appMark = new Border { Width = 42, Height = 44, CornerRadius = new CornerRadius(11), Background = Ink("#684080"),
                Child = Glyph("music", Brushes.White, 23) };
            header.Children.Add(appMark);
            var brand = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new TextBlock { Text = "Lyric Connector", FontSize = 22, FontWeight = FontWeights.SemiBold });
            brand.Children.Add(new TextBlock { Text = "音乐在手机，歌词在桌面", FontSize = 13, Foreground = Ink("#51465E"), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(brand, 1);
            header.Children.Add(brand);
            var caption = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -7, -12, 0) };
            var minimize = CaptionButton("最小化", "minimize", delegate { System.Windows.SystemCommands.MinimizeWindow(this); });
            var maximize = CaptionButton("最大化", "maximize", delegate
            {
                if (WindowState == WindowState.Maximized) System.Windows.SystemCommands.RestoreWindow(this);
                else System.Windows.SystemCommands.MaximizeWindow(this);
            });
            StateChanged += delegate
            {
                maximize.Tag = ControlTheme.Glyph(WindowState == WindowState.Maximized ? "restore" : "maximize");
                maximize.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
                AutomationProperties.SetName(maximize, maximize.ToolTip.ToString());
            };
            caption.Children.Add(minimize);
            caption.Children.Add(maximize);
            caption.Children.Add(CaptionButton("关闭到托盘", "close", delegate { System.Windows.SystemCommands.CloseWindow(this); }));
            Grid.SetColumn(caption, 2);
            header.Children.Add(caption);
            root.Children.Add(header);

            trackTitle.Text = "等待歌曲信息";
            artist.Text = "连接 iPhone 后播放音乐";
            trackTitle.FontSize = 17;
            trackTitle.FontWeight = FontWeights.SemiBold;
            trackTitle.Foreground = Ink("#FAF5FF");
            trackTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            trackTitle.TextWrapping = TextWrapping.NoWrap;
            artist.FontSize = 11;
            artist.Foreground = Ink("#E4D9EC");
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            artist.TextWrapping = TextWrapping.NoWrap;
            artist.VerticalAlignment = VerticalAlignment.Center;
            progress.FontSize = 10;
            progress.Foreground = Ink("#C6B7D4");
            progress.HorizontalAlignment = HorizontalAlignment.Right;
            progress.VerticalAlignment = VerticalAlignment.Center;
            progress.Margin = new Thickness(12, 0, 0, 0);
            var trackInfo = new Grid();
            trackInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            trackInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            trackInfo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            trackInfo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            trackInfo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            trackTitle.Margin = new Thickness(0, 0, 12, 0);
            trackInfo.Children.Add(trackTitle);

            Grid.SetRow(artist, 1);
            trackInfo.Children.Add(artist);

            playbackConnectionStatus.FontSize = 10;
            playbackConnectionStatus.VerticalAlignment = VerticalAlignment.Center;
            var connectionIcon = Glyph("bluetooth", Brushes.White, 11);
            connectionIcon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
                new Binding("Foreground") { Source = playbackConnectionStatus });
            connectionIcon.Margin = new Thickness(0, 0, 5, 0);
            var badgeContent = new StackPanel { Orientation = Orientation.Horizontal };
            badgeContent.Children.Add(connectionIcon);
            badgeContent.Children.Add(playbackConnectionStatus);
            playbackConnectionBadge.CornerRadius = new CornerRadius(11);
            playbackConnectionBadge.BorderThickness = new Thickness(1);
            playbackConnectionBadge.Padding = new Thickness(7, 2, 7, 2);
            playbackConnectionBadge.HorizontalAlignment = HorizontalAlignment.Right;
            playbackConnectionBadge.VerticalAlignment = VerticalAlignment.Top;
            playbackConnectionBadge.Child = badgeContent;
            Grid.SetColumn(playbackConnectionBadge, 1);
            trackInfo.Children.Add(playbackConnectionBadge);
            Grid.SetRow(progress, 1);
            Grid.SetColumn(progress, 1);
            trackInfo.Children.Add(progress);
            playbackProgress.Foreground = new LinearGradientBrush(Color.FromRgb(198, 126, 240), Color.FromRgb(230, 183, 255), 0);
            playbackProgress.Background = Ink("#665573");
            playbackProgress.BorderThickness = new Thickness(0);
            playbackProgress.Margin = new Thickness(0, 5, 0, 0);
            playbackProgress.Template = ControlTheme.PlaybackProgress;
            AutomationProperties.SetName(playbackProgress, "歌曲播放进度");
            Grid.SetRow(playbackProgress, 2);
            Grid.SetColumnSpan(playbackProgress, 2);
            trackInfo.Children.Add(playbackProgress);
            var trackStrip = new Border { Background = new LinearGradientBrush(Color.FromRgb(61, 40, 81), Color.FromRgb(40, 32, 52), 15),
                CornerRadius = new CornerRadius(8), MinHeight = 64,
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8), Child = trackInfo };
            Grid.SetRow(trackStrip, 1);
            root.Children.Add(trackStrip);

            musicTab = Button("音乐与歌词", delegate { SelectSettingsPage(false); });
            displayTab = Button("悬浮窗口", delegate { SelectSettingsPage(true); });
            musicTab.MinWidth = displayTab.MinWidth = 120;
            musicTab.Margin = new Thickness(0, 0, 12, 0);
            musicTab.MinHeight = displayTab.MinHeight = 36;
            musicTab.Padding = displayTab.Padding = new Thickness(12, 5, 12, 5);
            musicTab.Template = displayTab.Template = ButtonTemplate(0, 16);
            var tabButtons = new StackPanel { Orientation = Orientation.Horizontal };
            tabButtons.Children.Add(musicTab);
            tabButtons.Children.Add(displayTab);
            settingsTabs = new Border { BorderBrush = Ink("#E3DEE9"), BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 12), Child = tabButtons };
            Grid.SetRow(settingsTabs, 2);
            root.Children.Add(settingsTabs);

            musicPage = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            displayPage = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            musicPage.Resources.Add(typeof(ScrollBar), ControlTheme.VerticalScrollBar);
            displayPage.Resources.Add(typeof(ScrollBar), ControlTheme.VerticalScrollBar);
            var musicContent = new StackPanel { Margin = new Thickness(0, 0, 8, 4) };
            connectionState.FontSize = 12;
            connectionState.Foreground = Ink("#71697D");
            connectionState.VerticalAlignment = VerticalAlignment.Center;
            var guideButton = Button("连接帮助", delegate { connectionGuidePanel.Visibility = connectionGuidePanel.IsVisible ? Visibility.Collapsed : Visibility.Visible; });
            guideButton.MinHeight = 28;
            guideButton.Padding = new Thickness(6, 2, 6, 2);
            guideButton.Background = Brushes.Transparent;
            guideButton.BorderThickness = new Thickness(0);
            guideButton.Foreground = Ink("#75558E");
            var connectionActions = new StackPanel { Orientation = Orientation.Horizontal };
            connectionState.Margin = new Thickness(0, 0, 12, 0);
            connectionActions.Children.Add(connectionState);
            manageConnection = Button("管理设备", delegate
            {
                connectionDetailsExpanded = !connectionDetailsExpanded;
                UpdateConnectionControls();
            });
            manageConnection.Padding = new Thickness(6, 2, 6, 2);
            manageConnection.MinHeight = 28;
            manageConnection.Background = Brushes.Transparent;
            manageConnection.BorderThickness = new Thickness(0);
            manageConnection.Foreground = Ink("#75558E");
            connectionActions.Children.Add(manageConnection);
            musicContent.Children.Add(SectionHeader("设备连接", connectionActions));
            var deviceGrid = new Grid { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            deviceGrid.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = connectionDetails });
            deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StyleField(devices);
            AutomationProperties.SetName(devices, "已配对的 iPhone");
            devices.SetBinding(FrameworkElement.ToolTipProperty, new Binding("SelectedItem.Id") { Source = devices });
            ToolTipService.SetShowOnDisabled(devices, true);
            var deviceItem = new FrameworkElementFactory(typeof(DockPanel));
            var phone = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            phone.SetValue(System.Windows.Shapes.Path.DataProperty, ControlTheme.Glyph("phone"));
            phone.SetValue(System.Windows.Shapes.Path.StrokeProperty, Ink("#44354F"));
            phone.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.2);
            phone.SetValue(DockPanel.DockProperty, Dock.Left);
            phone.SetValue(FrameworkElement.WidthProperty, 16.0);
            phone.SetValue(FrameworkElement.HeightProperty, 18.0);
            phone.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
            deviceItem.AppendChild(phone);
            var deviceText = new FrameworkElementFactory(typeof(TextBlock));
            deviceText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new DeviceLabelConverter() });
            deviceText.SetBinding(TextBlock.ToolTipProperty, new Binding("Id"));
            deviceText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            deviceItem.AppendChild(deviceText);
            devices.ItemTemplate = new DataTemplate(typeof(DeviceChoice)) { VisualTree = deviceItem };
            devices.SelectionChanged += delegate { UpdateConnectionControls(); };
            devices.Margin = new Thickness(0, 0, 8, 0);
            refreshDevices = Button("刷新", async delegate { await RefreshDevicesAsync(false); });
            connect = Button("连接", async delegate
            {
                if (activeSource != null || connectionBusy) await DisconnectAsync();
                else await ConnectAsync();
            });
            refreshDevices.Tag = ControlTheme.Glyph("refresh");
            connect.Tag = ControlTheme.Glyph("connect");
            refreshDevices.MinWidth = connect.MinWidth = 86;
            refreshDevices.Margin = new Thickness(0, 0, 8, 0);
            deviceGrid.Children.Add(devices);
            Grid.SetColumn(refreshDevices, 1);
            deviceGrid.Children.Add(refreshDevices);
            Grid.SetColumn(connect, 2);
            deviceGrid.Children.Add(connect);
            connectionDetails.Children.Add(deviceGrid);
            var connectionFeedback = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            connectionFeedback.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            connectionFeedback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceStatus.FontSize = 12;
            sourceStatus.Foreground = Ink("#71697D");
            sourceStatus.VerticalAlignment = VerticalAlignment.Center;
            sourceStatus.Margin = new Thickness(0, 0, 12, 0);
            connectionFeedback.Children.Add(sourceStatus);
            Grid.SetColumn(guideButton, 1);
            connectionFeedback.Children.Add(guideButton);
            connectionDetails.Children.Add(connectionFeedback);
            connectionGuidePanel = CreateConnectionGuide();
            connectionDetails.Children.Add(connectionGuidePanel);
            musicContent.Children.Add(connectionDetails);
            musicContent.Children.Add(Divider());

            lyricSource.FontSize = 12;
            lyricSource.Foreground = Ink("#71697D");
            lyricSource.Text = "来源：LRCLIB";
            musicContent.Children.Add(SectionHeader("歌曲与歌词", lyricSource));
            searchTitle.ToolTip = "歌曲名称";
            searchArtist.ToolTip = "歌手名称；留空可扩大搜索范围";
            StyleField(searchTitle);
            StyleField(searchArtist);
            searchTitle.KeyDown += async delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Enter && search.IsEnabled)
                    await FindLyricsAsync(true, searchTitle.Text, searchArtist.Text, false);
            };
            var searchFields = new Grid { Margin = new Thickness(0, 0, 0, 12), MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left };
            searchFields.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = musicContent });
            searchFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var titleField = Field("歌曲名称", searchTitle);
            titleField.Margin = new Thickness(0, 0, 12, 0);
            var artistField = Field("歌手", searchArtist);
            Grid.SetColumn(artistField, 1);
            searchFields.Children.Add(titleField);
            searchFields.Children.Add(artistField);
            musicContent.Children.Add(searchFields);
            var searchActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            search = PrimaryButton("搜索歌词", async delegate { await FindLyricsAsync(true, searchTitle.Text, searchArtist.Text, false); });
            search.Tag = ControlTheme.Glyph("search");
            search.Margin = new Thickness(0, 0, 10, 0);
            importLyrics = Button("导入 LRC", delegate { Import(); });
            importLyrics.Tag = ControlTheme.Glyph("import");
            searchActions.Children.Add(search);
            searchActions.Children.Add(importLyrics);
            musicContent.Children.Add(searchActions);
            candidates.Margin = new Thickness(0, 2, 0, 6);
            candidates.Background = Brushes.Transparent;
            candidates.BorderBrush = Brushes.Transparent;
            candidates.Template = ControlTheme.Candidates;
            candidates.BorderThickness = new Thickness(0);
            ScrollViewer.SetHorizontalScrollBarVisibility(candidates, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(candidates, ScrollBarVisibility.Auto);
            candidates.SelectionChanged += delegate { UpdateLyricActions(); };
            candidates.ItemTemplate = CandidateTemplate();
            var candidateStyle = new Style(typeof(ListBoxItem));
            candidateStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, CandidateRowHeight));
            candidateStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            candidateStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
            candidateStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            candidateStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Ink("#E3DEE9")));
            candidateStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            candidateStyle.Setters.Add(new Setter(Control.TemplateProperty, CandidateItemTemplate()));
            candidates.ItemContainerStyle = candidateStyle;
            musicContent.Children.Add(candidates);
            candidateEmpty.Text = "播放歌曲后自动匹配，也可搜索或导入歌词。";
            candidateEmpty.FontSize = 12;
            candidateEmpty.TextAlignment = TextAlignment.Left;
            candidateEmpty.HorizontalAlignment = HorizontalAlignment.Left;
            candidateEmpty.Foreground = Ink("#71697D");
            candidateEmpty.Margin = new Thickness(0, 6, 0, 8);
            musicContent.Children.Add(candidateEmpty);
            var candidateActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            choose = Button("使用所选歌词", delegate { Choose(); });
            choose.Margin = new Thickness(0, 0, 8, 0);
            viewLyrics = Button("查看歌词全文", delegate { ShowText(); });
            candidateActions.Children.Add(choose);
            candidateActions.Children.Add(viewLyrics);
            musicContent.Children.Add(candidateActions);
            lyricStatus.Margin = new Thickness(0, 5, 0, 4);
            lyricStatus.FontSize = 12;
            lyricStatus.Foreground = Ink("#71697D");
            musicContent.Children.Add(lyricStatus);
            musicPage.Content = musicContent;

            var displayContent = new StackPanel { Margin = new Thickness(0, 0, 8, 4) };
            overlayVisibility.MinWidth = 104;
            displayContent.Children.Add(SectionHeader("悬浮歌词", overlayVisibility));
            previewLabel.Text = "样式示例";
            previewLabel.FontSize = 12;
            previewLabel.Foreground = Ink("#71697D");
            previewLabel.Margin = new Thickness(0, 0, 0, 5);
            displayContent.Children.Add(previewLabel);
            preview.Margin = new Thickness(16, 9, 16, 9);
            previewBackground.Child = preview;
            var previewDesktop = new Border { Background = new LinearGradientBrush(Color.FromRgb(45, 53, 63), Color.FromRgb(76, 57, 76), 25), CornerRadius = new CornerRadius(7),
                MinHeight = 84, Child = previewBackground };
            previewDesktop.ToolTip = "示例桌面背景；歌词字体、颜色和面板不透明度与悬浮窗一致。";
            AutomationProperties.SetName(previewDesktop, "悬浮歌词外观预览");
            displayContent.Children.Add(previewDesktop);
            var offsetButtons = new WrapPanel();
            var decreaseOffset = Button("− 0.25 秒", delegate { settings.Offset -= 0.25; UpdateOffset(); });
            var increaseOffset = Button("+ 0.25 秒", delegate { settings.Offset += 0.25; UpdateOffset(); });
            var resetOffset = Button("归零", delegate { settings.Offset = 0; UpdateOffset(); });
            foreach (var button in new[] { decreaseOffset, increaseOffset, resetOffset })
            {
                button.Background = Ink("#E7DAF0");
                button.Foreground = Ink("#5D3C76");
                button.BorderThickness = new Thickness(0);
                button.FontWeight = FontWeights.SemiBold;
            }
            offsetButtons.Children.Add(decreaseOffset);
            offsetButtons.Children.Add(offsetLabel);
            offsetButtons.Children.Add(increaseOffset);
            resetOffset.Margin = new Thickness(8, 0, 0, 0);
            offsetButtons.Children.Add(resetOffset);
            var offsetRow = SettingRow("歌词时间", offsetButtons);
            offsetRow.Margin = new Thickness(0, 8, 0, 0);
            offsetRow.ToolTip = "正值让歌词提前显示，负值让歌词延后显示。";
            displayContent.Children.Add(offsetRow);
            displayContent.Children.Add(Divider());
            displayContent.Children.Add(SectionHeading("文字样式", ""));
            var presetButtons = new WrapPanel();
            displayContent.Children.Add(SettingRow("快捷预设", presetButtons));
            var font = new ComboBox { IsEditable = false, IsTextSearchEnabled = true, MaxWidth = 420 };
            font.Items.Add(settings.Font);
            foreach (var name in Fonts.SystemFontFamilies.Select(family => family.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Where(name => !String.Equals(name, settings.Font, StringComparison.OrdinalIgnoreCase))) font.Items.Add(name);
            font.SelectedItem = settings.Font;
            font.SelectionChanged += delegate { settings.Font = (string)font.SelectedItem; ApplyAppearance(); };
            StyleField(font);
            displayContent.Children.Add(SettingRow("字体", font));
            var size = new Slider { Minimum = 16, Maximum = 56, Value = settings.FontSize, TickFrequency = 1, IsSnapToTickEnabled = true };
            var sizeInput = new TextBox { Text = settings.FontSize.ToString("0", CultureInfo.InvariantCulture), Width = 50, TextAlignment = TextAlignment.Right };
            StyleField(sizeInput);
            AutomationProperties.SetName(sizeInput, "歌词字号，px");
            sizeInput.ToolTip = "字号：16–56 px，按 Enter 或离开输入框应用";
            var sizeControl = SliderControl(size, sizeInput, "px");
            var sizeError = new TextBlock { FontSize = 12, Foreground = Ink("#A6423D"), Visibility = Visibility.Collapsed };
            Action commitSize = delegate
            {
                double value;
                if (!Double.TryParse(sizeInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 16 || value > 56)
                {
                    sizeError.Text = "请输入 16–56 的整数字号。";
                    sizeError.Visibility = Visibility.Visible;
                    return;
                }
                sizeError.Visibility = Visibility.Collapsed;
                size.Value = value;
                sizeInput.Text = value.ToString("0", CultureInfo.InvariantCulture);
            };
            sizeInput.LostKeyboardFocus += delegate { commitSize(); };
            sizeInput.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Enter) { commitSize(); args.Handled = true; }
            };
            size.ValueChanged += delegate
            {
                settings.FontSize = size.Value;
                sizeInput.Text = size.Value.ToString("0", CultureInfo.InvariantCulture);
                sizeError.Visibility = Visibility.Collapsed;
                ApplyAppearance();
            };
            displayContent.Children.Add(SettingRow("字号", sizeControl));
            displayContent.Children.Add(SettingRow("", sizeError));
            var currentColor = new TextBox { Text = settings.CurrentLineColor };
            var nextColor = new TextBox { Text = settings.NextLineColor };
            displayContent.Children.Add(ColorSetting("当前行", currentColor, true));
            displayContent.Children.Add(ColorSetting("前后行", nextColor, false));
            displayContent.Children.Add(Divider());
            displayContent.Children.Add(SectionHeading("背景与交互", ""));
            var opacity = new Slider { Minimum = 0, Maximum = 1, Value = settings.BackgroundOpacity, TickFrequency = 0.01, IsSnapToTickEnabled = true };
            var opacityValue = new TextBox { Text = Math.Round(settings.BackgroundOpacity * 100).ToString(CultureInfo.InvariantCulture),
                TextAlignment = TextAlignment.Right };
            StyleField(opacityValue);
            AutomationProperties.SetName(opacityValue, "背景不透明度，%");
            opacityValue.ToolTip = "输入 0–100 的整数百分比，按 Enter 或离开输入框应用";
            var opacityError = new TextBlock { FontSize = 12, Foreground = Ink("#A6423D"), Visibility = Visibility.Collapsed };
            Action commitOpacity = delegate
            {
                int value;
                if (!Int32.TryParse(opacityValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0 || value > 100)
                {
                    opacityError.Text = "请输入 0–100 的整数百分比。";
                    opacityError.Visibility = Visibility.Visible;
                    return;
                }
                opacityError.Visibility = Visibility.Collapsed;
                opacity.Value = value / 100.0;
                opacityValue.Text = value.ToString(CultureInfo.InvariantCulture);
            };
            opacityValue.LostKeyboardFocus += delegate { commitOpacity(); };
            opacityValue.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Enter) { commitOpacity(); args.Handled = true; }
            };
            opacity.ToolTip = "0% 完全透明，100% 完全不透明；悬停悬浮窗时临时提高背景不透明度。";
            opacity.ValueChanged += delegate
            {
                settings.BackgroundOpacity = opacity.Value;
                opacityValue.Text = Math.Round(opacity.Value * 100).ToString(CultureInfo.InvariantCulture);
                opacityError.Visibility = Visibility.Collapsed;
                ApplyAppearance();
            };
            displayContent.Children.Add(SettingRow("背景不透明度", SliderControl(opacity, opacityValue, "%")));
            displayContent.Children.Add(SettingRow("", opacityError));
            var topmost = new CheckBox { Style = ControlTheme.Switch, IsChecked = settings.Topmost, VerticalAlignment = VerticalAlignment.Center, ToolTip = "将悬浮歌词显示在其他窗口上方" };
            topmost.Click += delegate { settings.Topmost = topmost.IsChecked == true; ApplyAppearance(); };
            displayContent.Children.Add(SettingRow("窗口置顶", topmost));
            clickThrough.Style = ControlTheme.Switch;
            clickThrough.Content = "Ctrl + Alt + L 切换";
            clickThrough.IsChecked = settings.ClickThrough;
            clickThrough.VerticalAlignment = VerticalAlignment.Center;
            clickThrough.ToolTip = "开启后可点击悬浮窗下方的窗口；使用快捷键关闭穿透。";
            clickThrough.Click += delegate { settings.ClickThrough = clickThrough.IsChecked == true; ApplyAppearance(); };
            displayContent.Children.Add(SettingRow("鼠标穿透", clickThrough));
            var presets = new[] {
                new { Name = "简约", Size = 16.0, Opacity = 0.0 },
                new { Name = "标准", Size = 24.0, Opacity = 0.45 },
                new { Name = "大字体", Size = 32.0, Opacity = 0.64 }
            };
            foreach (var preset in presets)
            {
                var button = Button(preset.Name, delegate
                {
                    size.Value = preset.Size;
                    sizeInput.Text = preset.Size.ToString("0", CultureInfo.InvariantCulture);
                    sizeError.Visibility = Visibility.Collapsed;
                    opacity.Value = preset.Opacity;
                    opacityValue.Text = (preset.Opacity * 100).ToString("0", CultureInfo.InvariantCulture);
                    opacityError.Visibility = Visibility.Collapsed;
                    ApplyAppearance();
                    SaveSettings();
                });
                button.Margin = new Thickness(0, 0, 8, 0);
                button.ToolTip = preset.Size.ToString("0", CultureInfo.InvariantCulture) + "px / "
                    + (preset.Opacity == 0 ? "透明背景" : (preset.Opacity * 100).ToString("0", CultureInfo.InvariantCulture) + "% 背景不透明度");
                AutomationProperties.SetHelpText(button, button.ToolTip.ToString());
                Action refreshPreset = delegate
                {
                    var matches = Math.Abs(size.Value - preset.Size) < 0.01 && Math.Abs(opacity.Value - preset.Opacity) < 0.005;
                    button.Background = matches ? Ink("#EEE5F9") : Brushes.Transparent;
                    button.BorderBrush = matches ? Ink("#77528F") : Ink("#CFC3DB");
                    button.FontWeight = matches ? FontWeights.SemiBold : FontWeights.Normal;
                    AutomationProperties.SetItemStatus(button, matches ? "当前预设" : "");
                };
                size.ValueChanged += delegate { refreshPreset(); };
                opacity.ValueChanged += delegate { refreshPreset(); };
                refreshPreset();
                presetButtons.Children.Add(button);
            }
            displayContent.Children.Add(Divider());
            displayContent.Children.Add(new TextBlock { Text = "设置即时生效，退出程序时保存。关闭此窗口后，程序继续在托盘运行。",
                TextWrapping = TextWrapping.Wrap, Foreground = Ink("#71697D"), Margin = new Thickness(0, 12, 0, 4), FontSize = 12 });
            displayPage.Content = displayContent;

            settingsPages.ColumnDefinitions.Add(new ColumnDefinition());
            settingsPages.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsPages.ColumnDefinitions.Add(new ColumnDefinition());
            settingsPages.Children.Add(musicPage);
            Grid.SetColumn(settingsDivider, 1);
            settingsPages.Children.Add(settingsDivider);
            settingsPages.Children.Add(displayPage);
            Grid.SetRow(settingsPages, 3);
            root.Children.Add(settingsPages);
            var windowSurface = new Border { Background = Background, BorderBrush = Ink("#D9D2E0"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = root };
            Content = windowSurface;
            SizeChanged += delegate { UpdateSettingsLayout(); };
            SelectSettingsPage(false);
            UpdateOffset();
            UpdateCandidatePlaceholder();
            UpdateLyricActions();
            ApplyAppearance();
            ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            ticker.Tick += delegate { Render(); };
            reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
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
                SaveSettings();
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

        private Border CreateConnectionGuide()
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = "首次连接 iPhone", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 34, 52)), Margin = new Thickness(0, 0, 0, 8) });
            content.Children.Add(new TextBlock { Text = "1. 在 iPhone 和电脑上开启蓝牙，并在 Windows 蓝牙设置中配对 iPhone。",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(76, 68, 88)),
                Margin = new Thickness(0, 0, 0, 6) });
            content.Children.Add(new TextBlock { Text = "2. 回到这里，点“刷新”，选择 iPhone，再点“连接”。",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(76, 68, 88)),
                Margin = new Thickness(0, 0, 0, 6) });
            content.Children.Add(new TextBlock { Text = "3. 连接成功后，可播放音乐检查歌词读取。连接失败时可刷新设备后重试。",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(76, 68, 88)),
                Margin = new Thickness(0, 0, 0, 10) });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var openBluetoothSettings = Button("打开 Windows 蓝牙设置", delegate { OpenBluetoothSettings(); });
            openBluetoothSettings.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(openBluetoothSettings);
            actions.Children.Add(Button("稍后连接", delegate { connectionGuidePanel.Visibility = Visibility.Collapsed; }));
            content.Children.Add(actions);
            return new Border { BorderBrush = Ink("#D9C9E6"), BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed, Child = content };
        }

        private void OpenBluetoothSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
            catch (Exception error) { sourceStatus.Text = "无法打开 Windows 蓝牙设置：" + error.Message; }
        }

        private void SaveSettings()
        {
            settings.Left = overlay.Left;
            settings.Top = overlay.Top;
            settings.Width = overlay.Width;
            try { File.WriteAllText(settingsPath, new JavaScriptSerializer().Serialize(settings)); }
            catch (Exception error) { Console.WriteLine("SETTINGS: " + error.Message); }
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

        private void SelectSettingsPage(bool showDisplay)
        {
            displaySettingsSelected = showDisplay;
            StyleTab(musicTab, !showDisplay);
            StyleTab(displayTab, showDisplay);
            UpdateSettingsLayout();
        }

        private void UpdateSettingsLayout()
        {
            var combined = ActualWidth >= 1100;
            settingsTabs.Visibility = combined ? Visibility.Collapsed : Visibility.Visible;
            settingsDivider.Visibility = combined ? Visibility.Visible : Visibility.Collapsed;
            settingsPages.Margin = combined ? new Thickness(0, 14, 0, 0) : new Thickness(0);
            settingsPages.ColumnDefinitions[0].Width = new GridLength(combined ? 55 : 1, GridUnitType.Star);
            settingsPages.ColumnDefinitions[2].Width = combined ? new GridLength(45, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(displayPage, combined ? 2 : 0);
            musicPage.Visibility = combined || !displaySettingsSelected ? Visibility.Visible : Visibility.Collapsed;
            displayPage.Visibility = combined || displaySettingsSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void StyleTab(Button button, bool selected)
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(93, 61, 122) : Color.FromRgb(113, 105, 124));
            button.BorderBrush = selected ? Ink("#75558E") : Brushes.Transparent;
            button.BorderThickness = new Thickness(0, 0, 0, 3);
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private static SolidColorBrush Ink(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze();
            return brush;
        }

        private static Border Divider()
        {
            return new Border { Height = 1, Background = Ink("#E3DEE9"), Margin = new Thickness(0, 12, 0, 12) };
        }

        private static Grid SectionHeader(string title, FrameworkElement action)
        {
            var header = new Grid { MinHeight = 28, Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(HeadingLabel(title));
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            header.Children.Add(action);
            return header;
        }

        private static StackPanel SectionHeading(string title, string detail)
        {
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            heading.Children.Add(HeadingLabel(title));
            if (!String.IsNullOrEmpty(detail))
                heading.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = Ink("#71697D"), Margin = new Thickness(0, 3, 0, 0) });
            return heading;
        }

        private static StackPanel Field(string label, Control control)
        {
            AutomationProperties.SetName(control, label);
            var field = new StackPanel();
            field.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Ink("#71697D"), Margin = new Thickness(0, 0, 0, 4) });
            field.Children.Add(control);
            return field;
        }

        private static Grid SettingRow(string label, FrameworkElement control)
        {
            var row = new Grid { MinHeight = 36, Margin = new Thickness(0, 0, 0, 2) };
            row.SetBinding(UIElement.VisibilityProperty, new Binding("Visibility") { Source = control });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ink("#272235"), Margin = new Thickness(0, 0, 12, 0) });
            AutomationProperties.SetName(control, label);
            control.VerticalAlignment = VerticalAlignment.Center;
            var host = new Grid();
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = host });
            host.Children.Add(control);
            Grid.SetColumn(host, 1);
            row.Children.Add(host);
            return row;
        }

        private static Grid SliderControl(Slider slider, TextBox input, string unit)
        {
            var row = new Grid { MaxWidth = 520 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.Template = SliderTemplate();
            slider.Margin = new Thickness(0, 0, 16, 0);
            slider.Foreground = Ink("#75558E");
            AutomationProperties.SetName(slider, unit == "px" ? "歌词字号" : "背景不透明度");
            row.Children.Add(slider);
            input.Width = 40;
            input.MinHeight = 32;
            input.Background = Brushes.Transparent;
            input.BorderThickness = new Thickness(0);
            input.Padding = new Thickness(4, 3, 3, 3);
            var number = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            number.Children.Add(input);
            number.Children.Add(new TextBlock { Text = unit, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0), Foreground = Ink("#71697D") });
            var surface = new Border { Background = Ink("#FFFEFF"), BorderBrush = Ink("#CFC3DB"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), MinHeight = 32, Child = number };
            surface.MouseLeftButtonDown += delegate { input.Focus(); };
            Grid.SetColumn(surface, 1);
            row.Children.Add(surface);
            return row;
        }

        private static ControlTemplate SliderTemplate()
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
  <Grid x:Name='sliderSurface' Height='24' Background='Transparent'>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
           Value='{TemplateBinding Value}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.DecreaseLarge}' Background='#75558E' Height='4' Focusable='False'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='{x:Type RepeatButton}'>
              <Border Background='{TemplateBinding Background}' CornerRadius='2'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width='16' Height='16'>
          <Thumb.Template>
            <ControlTemplate TargetType='{x:Type Thumb}'>
              <Border Background='#75558E' BorderBrush='#F7F6FA' BorderThickness='1' CornerRadius='8'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.IncreaseLarge}' Background='#D9D2E1' Height='4' Focusable='False'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='{x:Type RepeatButton}'>
              <Border Background='{TemplateBinding Background}' CornerRadius='2'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>");
        }

        private UIElement ColorSetting(string label, TextBox input, bool currentLine)
        {
            StyleField(input);
            input.Text = FormatColor((Color)ColorConverter.ConvertFromString(input.Text));
            AutomationProperties.SetName(input, label + "颜色");
            input.ToolTip = "HEX 颜色，按 Enter 或离开输入框应用；兼容含透明度的颜色值。";
            var chip = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
                BorderBrush = Ink("#B9B1C4"), BorderThickness = new Thickness(1), Background = Ink(input.Text) };
            var swatch = Button("", delegate { });
            swatch.Content = chip;
            swatch.Width = 36;
            swatch.Padding = new Thickness(4);
            swatch.Margin = new Thickness(0);
            swatch.BorderThickness = new Thickness(0);
            swatch.Background = Brushes.Transparent;
            swatch.ToolTip = "选择" + label + "颜色";
            AutomationProperties.SetName(swatch, label + "颜色预设");
            var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = Ink("#A6423D"), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            Action commit = delegate { ApplyTextColor(input, currentLine, chip, error); };
            input.LostKeyboardFocus += delegate { commit(); };
            input.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Enter) { commit(); args.Handled = true; }
            };
            var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            popup.Child = new Border { Background = Brushes.White, BorderBrush = Ink("#E3DEE9"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Child = ColorPalette(delegate(string color)
                {
                    input.Text = color;
                    commit();
                    popup.IsOpen = false;
                    swatch.Focus();
                }) };
            popup.Opened += delegate { popup.Child.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)); };
            Closing += delegate { popup.IsOpen = false; };
            IsVisibleChanged += delegate { if (!IsVisible) popup.IsOpen = false; };
            popup.Child.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Escape) { popup.IsOpen = false; swatch.Focus(); args.Handled = true; }
            };
            swatch.Click += delegate { popup.IsOpen = !popup.IsOpen; };
            var controls = new Grid();
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.Children.Add(swatch);
            input.BorderThickness = new Thickness(0);
            input.Background = Brushes.Transparent;
            input.Padding = new Thickness(4, 5, 4, 5);
            Grid.SetColumn(input, 1);
            controls.Children.Add(input);
            var openPalette = Button("", delegate { popup.IsOpen = !popup.IsOpen; });
            openPalette.Content = null;
            openPalette.Tag = ControlTheme.Glyph("palette");
            openPalette.Width = 34;
            openPalette.Padding = new Thickness(8);
            openPalette.BorderThickness = new Thickness(0);
            openPalette.Background = Brushes.Transparent;
            openPalette.ToolTip = "选择" + label + "颜色";
            AutomationProperties.SetName(openPalette, label + "色板");
            Grid.SetColumn(openPalette, 2);
            controls.Children.Add(openPalette);
            var colorSurface = new Border { CornerRadius = new CornerRadius(3), Background = Ink("#FFFEFF"),
                BorderBrush = Ink("#CFC3DB"), BorderThickness = new Thickness(1), Child = controls };
            var content = new StackPanel { MaxWidth = 260 };
            content.Children.Add(colorSurface);
            content.Children.Add(error);
            return SettingRow(label, content);
        }

        private static void StyleField(Control control)
        {
            control.MinHeight = 32;
            control.FontSize = 13;
            control.Padding = new Thickness(8, 4, 8, 4);
            control.Background = Brushes.White;
            control.Foreground = Ink("#272235");
            control.BorderBrush = Ink("#CFC3DB");
            control.BorderThickness = new Thickness(1);
            if (control is TextBox)
            {
                control.Style = ControlTheme.Input;
                ((TextBox)control).VerticalContentAlignment = VerticalAlignment.Center;
            }
            else if (control is ComboBox) control.Style = ControlTheme.Select;
        }

        private static System.Windows.Shapes.Path Glyph(string name, Brush color, double size)
        {
            return new System.Windows.Shapes.Path { Data = ControlTheme.Glyph(name), Stroke = color, StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        }

        private static Button CaptionButton(string name, string glyph, RoutedEventHandler action)
        {
            var button = Button(name, action);
            button.Content = null;
            button.Tag = ControlTheme.Glyph(glyph);
            button.Template = ButtonTemplate(6, 12);
            button.Width = 32;
            button.MinHeight = button.Height = 28;
            button.Padding = new Thickness(7);
            button.BorderThickness = new Thickness(0);
            button.ToolTip = name;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private static StackPanel HeadingLabel(string title)
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5),
                Background = Ink("#77528F"), Margin = new Thickness(0, 0, 11, 0) });
            heading.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
            return heading;
        }

        private static ControlTemplate ButtonTemplate(double radius, double iconSize)
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
          BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' CornerRadius='RADIUS'>
    <Grid HorizontalAlignment='Center' VerticalAlignment='Center'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <Path x:Name='icon' Data='{TemplateBinding Tag}' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.4'
            StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Stretch='Uniform'
            Width='ICON_SIZE' Height='ICON_SIZE' Margin='0,0,8,0' VerticalAlignment='Center'/>
      <ContentPresenter x:Name='label' Grid.Column='1' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Grid>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='Tag' Value='{x:Null}'><Setter TargetName='icon' Property='Visibility' Value='Collapsed'/></Trigger>
    <Trigger Property='Content' Value='{x:Null}'>
      <Setter TargetName='icon' Property='Margin' Value='0'/>
      <Setter TargetName='label' Property='Visibility' Value='Collapsed'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.84'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.65'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#77528F'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.45'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>".Replace("RADIUS", radius.ToString(CultureInfo.InvariantCulture))
    .Replace("ICON_SIZE", iconSize.ToString(CultureInfo.InvariantCulture)));
        }

        private static Button Button(string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), MinHeight = 32, FontSize = 13,
                Foreground = Ink("#272235"), Background = Ink("#FCFAFF"), BorderBrush = Ink("#CFC3DB"),
                BorderThickness = new Thickness(1), Template = ButtonTemplate(3, 15) };
            AutomationProperties.SetName(button, text);
            button.Click += action;
            return button;
        }

        private static Button PrimaryButton(string text, RoutedEventHandler action)
        {
            var button = Button(text, action);
            button.Foreground = Brushes.White;
            button.Background = button.BorderBrush = Ink("#75558E");
            button.FontWeight = FontWeights.SemiBold;
            return button;
        }

        private static WrapPanel ColorPalette(Action<string> select)
        {
            var choices = new[,] {
                { "暖白", "#FFF1D6" }, { "白色", "#FFFFFF" }, { "金黄", "#FFD166" }, { "珊瑚", "#FF8A65" },
                { "粉色", "#F4D7E7" }, { "玫红", "#F06292" }, { "薄荷", "#7DE2D1" }, { "青色", "#72DDF7" },
                { "天蓝", "#8AB4F8" }, { "紫色", "#C4A1FF" }
            };
            var palette = new WrapPanel { Width = 160 };
            for (var index = 0; index < choices.GetLength(0); index++)
            {
                var color = choices[index, 1];
                var label = choices[index, 0] + " " + color;
                var swatch = Button("", delegate { select(color); });
                swatch.Width = 28;
                swatch.Height = swatch.MinHeight = 28;
                swatch.Padding = new Thickness(0);
                swatch.Margin = new Thickness(2);
                swatch.Background = Ink(color);
                swatch.ToolTip = label;
                AutomationProperties.SetName(swatch, label);
                palette.Children.Add(swatch);
            }
            return palette;
        }

        private DataTemplate CandidateTemplate()
        {
            var row = new FrameworkElementFactory(typeof(CandidateRowView));
            row.SetBinding(FrameworkElement.ToolTipProperty, new Binding());
            row.SetBinding(FrameworkElement.TagProperty, new Binding("Tag") { Source = candidates });
            return new DataTemplate(typeof(LyricMatch)) { VisualTree = row };
        }

        private static ControlTemplate CandidateItemTemplate()
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBoxItem}'>
  <Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='0' Padding='{TemplateBinding Padding}'
          BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width='24'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
      <Border x:Name='selector' Width='15' Height='15' CornerRadius='7.5' BorderBrush='#B3A5C5' BorderThickness='1'
              HorizontalAlignment='Left' VerticalAlignment='Center' UseLayoutRounding='False'>
        <Ellipse x:Name='dot' Width='5' Height='5' Fill='White' Visibility='Hidden'
                 HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ContentPresenter Grid.Column='1' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
    </Grid>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsSelected' Value='True'>
      <Setter TargetName='surface' Property='Background' Value='#EEE5F9'/>
      <Setter TargetName='selector' Property='Background' Value='#795398'/>
      <Setter TargetName='selector' Property='BorderBrush' Value='#795398'/>
      <Setter TargetName='dot' Property='Visibility' Value='Visible'/>
    </Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'>
      <Setter TargetName='surface' Property='BorderBrush' Value='#9474AE'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
        }

        private void ApplyAppearance()
        {
            overlay.Apply(settings);
            preview.Apply(settings);
            previewBackground.Background = new SolidColorBrush(Color.FromArgb((byte)(settings.BackgroundOpacity * 255), 27, 23, 41));
        }

        private IntPtr OnNativeMessage(IntPtr window, int message, IntPtr first, IntPtr second, ref bool handled)
        {
            if (message == 0x312 && first.ToInt32() == 1)
            {
                settings.ClickThrough = !settings.ClickThrough;
                clickThrough.IsChecked = settings.ClickThrough;
                ApplyAppearance();
                handled = true;
            }
            return IntPtr.Zero;
        }
        private void UpdateOffset() { offsetLabel.Text = settings.Offset.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " s"; }
        private void UpdateCandidatePlaceholder()
        {
            var empty = candidates.Items.Count == 0;
            candidates.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            candidateEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            UpdateLyricActions();
        }
        private void UpdateOverlayVisibility()
        {
            var visible = settings.ShowLyrics != false;
            if (visible)
            {
                if (!overlay.IsVisible) overlay.Show();
            }
            else overlay.Hide();
            overlayVisibility.IsChecked = visible;
            overlayVisibility.ToolTip = visible ? "隐藏整个悬浮窗口" : "重新显示整个悬浮窗口";
            AutomationProperties.SetName(overlayVisibility, visible ? "隐藏悬浮窗口" : "显示悬浮窗口");
        }

        private static string FormatColor(Color color)
        {
            return color.A == 255 ? "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2") : color.ToString();
        }

        private void ApplyTextColor(TextBox input, bool currentLine, Border chip, TextBlock error)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(input.Text)) throw new FormatException();
                var color = (Color)ColorConverter.ConvertFromString(input.Text.Trim());
                if (currentLine) settings.CurrentLineColor = color.ToString();
                else settings.NextLineColor = color.ToString();
                input.Text = FormatColor(color);
                chip.Background = new SolidColorBrush(color);
                error.Visibility = Visibility.Collapsed;
                ApplyAppearance();
            }
            catch (FormatException)
            {
                error.Text = "请输入有效颜色，例如 #F4D7E7 或 #FFF4D7E7。";
                error.Visibility = Visibility.Visible;
            }
        }

        private void UpdateConnectionControls()
        {
            devices.IsEnabled = !refreshingDevices && !connectionBusy && activeSource == null;
            refreshDevices.IsEnabled = !refreshingDevices && !connectionBusy;
            refreshDevices.Content = refreshingDevices ? "刷新中…" : "刷新";
            connect.Tag = ControlTheme.Glyph(activeSource != null ? "disconnect" : "connect");
            if (connectionBusy)
                connect.Content = activeSource != null ? "取消连接" : manuallyDisconnected ? "断开中…" : "连接中…";
            else connect.Content = activeSource != null ? "断开" : "连接";
            connect.IsEnabled = !refreshingDevices && (activeSource != null || (!connectionBusy && devices.SelectedItem != null));
            var connected = activeSource != null && !connectionBusy;
            playbackConnectionStatus.Text = connected ? "已连接" : connectionBusy ? "处理中" : reconnectTimer.IsEnabled ? "等待重连" : "未连接";
            playbackConnectionStatus.Foreground = connected ? Ink("#DDF4E3") : Ink("#D1C5DD");
            playbackConnectionBadge.Background = connected
                ? new LinearGradientBrush(Color.FromRgb(35, 106, 61), Color.FromRgb(35, 82, 53), 0)
                : (Brush)Ink("#4D425D");
            playbackConnectionBadge.BorderBrush = connected ? Ink("#438954") : Ink("#6A5B7C");
            playbackConnectionBadge.ToolTip = "手机蓝牙连接：" + playbackConnectionStatus.Text;
            AutomationProperties.SetName(playbackConnectionBadge, playbackConnectionBadge.ToolTip.ToString());
            connectionState.Text = connected ? "● 已连接" + (model.Player.Length > 0 ? " · " + DisplayPlayerName(model.Player) : "")
                : connectionBusy ? "处理中" : reconnectTimer.IsEnabled ? "等待重连" : "未连接";
            connectionDetails.Visibility = settings.HasConnectedToPhone && !connectionDetailsExpanded
                ? Visibility.Collapsed : Visibility.Visible;
            manageConnection.Visibility = settings.HasConnectedToPhone ? Visibility.Visible : Visibility.Collapsed;
            manageConnection.Content = connectionDetailsExpanded ? "收起" : "管理设备";
            AutomationProperties.SetName(manageConnection, manageConnection.Content.ToString());
            sourceStatus.Visibility = model.Ready ? Visibility.Collapsed : Visibility.Visible;
            connectionState.Foreground = activeSource != null && !connectionBusy ? Ink("#287342") : Ink("#71697D");
            AutomationProperties.SetName(connect, connect.Content.ToString());
        }

        private void UpdateLyricActions()
        {
            var match = candidates.SelectedItem as LyricMatch;
            choose.Visibility = candidates.Items.Count > 0 && (match == null || !Object.ReferenceEquals(match.Record, selected))
                ? Visibility.Visible : Visibility.Collapsed;
            viewLyrics.Visibility = lyrics != null ? Visibility.Visible : Visibility.Collapsed;
            choose.IsEnabled = model.Ready && candidateRevision == model.Revision && match != null
                && !Object.ReferenceEquals(match.Record, selected) && !searchingLyrics;
            choose.Content = "使用所选歌词";
            AutomationProperties.SetName(choose, choose.Content.ToString());
            viewLyrics.IsEnabled = lyrics != null;
            search.IsEnabled = model.Ready && !searchingLyrics;
            search.Content = searchingLyrics ? "搜索中…" : "搜索歌词";
            importLyrics.IsEnabled = model.Ready;
        }

        private async Task RefreshDevicesAsync(bool autoConnect)
        {
            if (refreshingDevices) return;
            refreshingDevices = true;
            var generation = connectionGeneration;
            UpdateConnectionControls();
            sourceStatus.Text = "正在读取已配对设备…";
            try
            {
                var found = await AmsSource.DevicesAsync(CancellationToken.None);
                if (closing || generation != connectionGeneration) return;
                devices.ItemsSource = found;
                var target = found.FirstOrDefault(item => item.Id == settings.DeviceId);
                if (target == null && requestedDevice.Length > 0) target = found.SingleOrDefault(item => item.Name == requestedDevice);
                devices.SelectedItem = target;
                sourceStatus.Text = activeSource != null ? model.Status : found.Count == 0
                    ? "未发现已配对设备，请先在 Windows 蓝牙设置中配对 iPhone。" : "选择 iPhone 后连接。";
                if (autoConnect)
                {
                    if (target != null) await ConnectAsync();
                    else StartAutoReconnect();
                }
            }
            catch (Exception error)
            {
                if (closing || generation != connectionGeneration) return;
                sourceStatus.Text = "设备读取失败：" + error.Message;
                if (autoConnect) StartAutoReconnect();
            }
            finally { refreshingDevices = false; UpdateConnectionControls(); }
        }

        private void StartAutoReconnect()
        {
            if (closing || manuallyDisconnected || (String.IsNullOrEmpty(settings.DeviceId) && requestedDevice.Length == 0)) return;
            if (!reconnectTimer.IsEnabled) reconnectTimer.Start();
            UpdateConnectionControls();
        }

        private async Task TryAutoReconnectAsync()
        {
            if (closing || manuallyDisconnected || activeSource != null || connectionBusy || refreshingDevices || reconnectAttemptInProgress) return;
            reconnectAttemptInProgress = true;
            var generation = connectionGeneration;
            try
            {
                var found = await AmsSource.DevicesAsync(CancellationToken.None);
                if (closing || manuallyDisconnected || activeSource != null || generation != connectionGeneration) return;
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
                if (!closing && !manuallyDisconnected && generation == connectionGeneration)
                {
                    Console.WriteLine("AMS_RECONNECT_ERROR: " + error.Message);
                    sourceStatus.Text = "iPhone 自动重连暂时失败，正在重试。";
                }
            }
            finally { reconnectAttemptInProgress = false; UpdateConnectionControls(); }
        }

        private async Task ConnectAsync()
        {
            if (connectionBusy || activeSource != null) return;
            var device = devices.SelectedItem as DeviceChoice;
            if (device == null) { sourceStatus.Text = "请先选择你的 iPhone。"; return; }
            manuallyDisconnected = false;
            connectionBusy = true;
            reconnectTimer.Stop();
            var generation = ++connectionGeneration;
            UpdateConnectionControls();
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
            sourceTask = source.RunAsync(sourceCancellation.Token);
            UpdateConnectionControls();
        }

        private async Task DisconnectAsync()
        {
            manuallyDisconnected = true;
            reconnectTimer.Stop();
            ++connectionGeneration;
            activeSource = null;
            if (sourceCancellation != null) sourceCancellation.Cancel();
            Accept(new SourceUpdate { Kind = UpdateKind.Disconnected, Revision = model.Revision + 1, Message = "已主动断开，点击连接可重新连接。" });
            connectionBusy = true;
            UpdateConnectionControls();
            try { await sourceTask; }
            finally { connectionBusy = false; UpdateConnectionControls(); }
        }

        private void Accept(SourceUpdate update)
        {
            if (update.Revision < model.Revision) return;
            if (update.Kind == UpdateKind.Disconnected)
            {
                activeSource = null;
                connectionBusy = false;
                if (!manuallyDisconnected)
                {
                    connectionDetailsExpanded = true;
                    connectionGuidePanel.Visibility = Visibility.Visible;
                }
            }
            var changed = model.Accept(update);
            if (update.Kind == UpdateKind.Connected)
            {
                connectionBusy = false;
                connectionDetailsExpanded = false;
                connectionGuidePanel.Visibility = Visibility.Collapsed;
                if (!settings.HasConnectedToPhone)
                {
                    settings.HasConnectedToPhone = true;
                    SaveSettings();
                }
            }
            sourceStatus.Text = model.Status + (model.Player.Length > 0 ? " · " + DisplayPlayerName(model.Player) : "");
            if (update.Kind == UpdateKind.Disconnected) StartAutoReconnect();
            else if (update.Kind == UpdateKind.Snapshot) reconnectTimer.Stop();
            Console.WriteLine("AMS: " + update.Kind + " r" + update.Revision + " " + model.Status);
            if (changed || update.Kind == UpdateKind.Transition || update.Kind == UpdateKind.Disconnected)
            {
                if (queryCancellation != null) queryCancellation.Cancel();
                lyricsRetryTimer.Stop();
                lyricsRetryCount = 0;
                lyricsRetryDelaySeconds = 0;
                searchingLyrics = false;
                lyrics = null; selected = null; candidates.Tag = null; candidates.ItemsSource = null; candidateRevision = -1;
                lyricSource.Text = "来源：LRCLIB";
                UpdateCandidatePlaceholder();
                candidateEmpty.Text = "播放歌曲后自动匹配，也可搜索或导入歌词。";
                lyricStatus.Text = "";
                RefreshLyricsViewer();
            }
            if (model.Ready)
            {
                var query = SongQuery.Infer(model.Track);
                trackTitle.Text = query.Title;
                trackTitle.ToolTip = "手机原始标题：" + model.Track.Title;
                artist.Text = String.Join(" · ", new[] { query.Artist, DisplayPlayerName(model.Player) }.Where(value => value.Length > 0));
                artist.ToolTip = "手机艺人字段：" + model.Track.Artist + (query.Inferred ? "；从视频标题提取，请核对版本" : "");
                HadSnapshot = true;
                if (changed)
                {
                    searchTitle.Text = query.Title;
                    searchArtist.Text = query.Artist;
                    var pending = FindLyricsAsync(false, "", "", false);
                }
            }
            else { trackTitle.Text = "等待歌曲信息"; trackTitle.ToolTip = null; artist.Text = "连接 iPhone 后播放音乐"; artist.ToolTip = null; progress.Text = ""; }
            UpdateConnectionControls();
            UpdateLyricActions();
            Render();
        }

        private static bool IsAppleMusicPlayer(string player)
        {
            return String.Equals(player, "Apple Music", StringComparison.OrdinalIgnoreCase)
                || String.Equals(player, "Music", StringComparison.OrdinalIgnoreCase);
        }

        private static string DisplayPlayerName(string player)
        {
            return IsAppleMusicPlayer(player) ? "Apple Music" : player;
        }

        private static bool SupportsOnlineLyrics(string player)
        {
            return String.Equals(player, "YouTube Music", StringComparison.OrdinalIgnoreCase)
                || IsAppleMusicPlayer(player)
                || String.Equals(player, "Spotify", StringComparison.OrdinalIgnoreCase)
                || String.Equals(player, "NetEase Music", StringComparison.OrdinalIgnoreCase)
                || player == "网易云音乐"
                || String.Equals(player, "QQMusic", StringComparison.OrdinalIgnoreCase)
                || String.Equals(player, "QQ Music", StringComparison.OrdinalIgnoreCase)
                || player == "QQ音乐"
                || String.Equals(player, "Kugou Music", StringComparison.OrdinalIgnoreCase)
                || player == "酷狗音乐";
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
            var track = model.Track;
            if (!SupportsOnlineLyrics(model.Player))
            {
                if (queryCancellation != null) queryCancellation.Cancel();
                candidateRevision = -1;
                candidates.ItemsSource = null;
                UpdateCandidatePlaceholder();
                var cached = store.LoadCached(track);
                if (cached != null) SetLyrics(cached);
                lyricStatus.Text = cached != null
                    ? "当前播放器不支持在线歌词搜索，已加载本地缓存歌词。"
                    : "当前播放器不支持在线歌词搜索，可导入本地 LRC。";
                return;
            }
            if (queryCancellation != null) queryCancellation.Cancel();
            var cancellation = new CancellationTokenSource();
            queryCancellation = cancellation;
            var token = cancellation.Token;
            searchingLyrics = true;
            var revision = model.Revision;
            candidateRevision = -1;
            candidates.ItemsSource = null;
            UpdateCandidatePlaceholder();
            candidateEmpty.Text = "正在搜索歌词…";
            lyricStatus.Text = "正在查询歌词…";
            UpdateLyricActions();
            try
            {
                var result = manual
                    ? await store.SearchAsync(track, title, artistName, token)
                    : await store.FindAsync(track, token);
                if (token.IsCancellationRequested || closing || revision != model.Revision) return;
                lyricsRetryCount = 0;
                lyricsRetryDelaySeconds = 0;
                if (result.Selected != null) SetLyrics(result.Selected);
                else
                {
                    candidateRevision = revision;
                    candidates.ItemsSource = result.Candidates;
                    UpdateCandidatePlaceholder();
                    var query = SongQuery.Infer(track);
                    var inference = !manual && query.Inferred ? "已提取歌名《" + query.Title + "》。" : "";
                    var bestIndex = result.Candidates.FindIndex(match => match.Record.instrumental || match.Record.Document().PlainText.Length > 0);
                    if (bestIndex >= 0)
                    {
                        var best = result.Candidates[bestIndex];
                        candidates.SelectedItem = best;
                        SetLyrics(best.Record);
                        var closeAlternative = result.Candidates.Where((match, index) => index != bestIndex)
                            .Any(match => match.Record.Document().Synced == lyrics.Synced
                                && Math.Abs(best.ConfidenceScore - match.ConfidenceScore) < 8);
                        var lyricType = best.Record.instrumental ? "纯音乐" : lyrics.Synced ? "同步歌词" : "纯文本歌词";
                        lyricStatus.Text = inference + "已自动选用《" + best.Record.trackName + "》的" + lyricType
                            + (closeAlternative ? "；有相近版本，可在列表中切换。" : "。");
                    }
                    else lyricStatus.Text = inference + (result.Candidates.Count == 0
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
            finally
            {
                if (Object.ReferenceEquals(queryCancellation, cancellation))
                {
                    searchingLyrics = false;
                    if (candidates.Items.Count == 0)
                        candidateEmpty.Text = lyrics != null ? "已载入当前歌词，可搜索其他版本。"
                            : lyricsRetryTimer.IsEnabled ? "等待自动重试，也可导入本地 LRC。" : "没有可用候选，可修改歌曲信息重试或导入 LRC。";
                    UpdateLyricActions();
                }
            }
        }

        private void HandleTransientLyricsFailure(bool manual, string title, string artistName, bool retryAttempt, long revision, string reason)
        {
            if (retryAttempt) lyricsRetryCount++;
            if (lyricsRetryCount >= MaxLyricsRetries)
            {
                lyricStatus.Text = reason + "，已自动重试 " + MaxLyricsRetries + " 次；请手动搜索歌词或导入本地 LRC。";
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
            candidates.Tag = record;
            lyricSource.Text = "来源：" + record.Source;
            lyrics = record.Document();
            UpdateLyricActions();
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
                return "已重试 " + MaxLyricsRetries + " 次 · 请在桌面手动搜索歌词";
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
            if (status.IndexOf("没有可显示的歌词内容", StringComparison.Ordinal) >= 0) return "候选歌词无可显示内容 · 可在桌面修正检索信息或导入 LRC";
            if (candidates.Items.Count > 0) return "有歌词候选 · 请在桌面核对选择";
            if (status.IndexOf("未找到歌词", StringComparison.Ordinal) >= 0) return "未找到歌词 · 可在桌面修正信息或导入 LRC";
            if (status.StartsWith("歌词查询暂时失败", StringComparison.Ordinal)) return "歌词查询失败 · 可在桌面重试或导入 LRC";
            if (status.StartsWith("歌词导入失败", StringComparison.Ordinal)) return "歌词导入失败 · 可在桌面查看原因";
            if (status.StartsWith("保存歌词失败", StringComparison.Ordinal)) return "歌词保存失败 · 可在桌面查看原因";
            if (status.StartsWith("请输入歌曲名称", StringComparison.Ordinal)) return "请在桌面输入歌曲名称后搜索";
            return "等待歌词 · 可在桌面搜索或导入 LRC";
        }

        private void SetLyricText(string before, string active, string after)
        {
            overlay.SetText(before, active, after);
            var live = model.Ready && lyrics != null && lyrics.Synced && !selected.instrumental;
            previewLabel.Text = live ? "实时预览" : "样式示例";
            if (live) preview.SetText(before, active, after);
            else preview.SetText("", "音乐在手机，歌词在桌面", "下一句歌词将在这里显示");
        }

        private void SetLyricProgress(double value)
        {
            overlay.SetKaraokeProgress(value);
            preview.SetKaraokeProgress(model.Ready && lyrics != null && lyrics.Synced ? value : 1);
        }

        private void Render()
        {
            UpdatePlaybackControls();
            if (!model.Ready)
            {
                UpdateLyricsViewer(-1);
                overlay.SetProgress(0, 0);
                playbackProgress.Value = 0;
                playbackProgress.Visibility = Visibility.Hidden;
                SetLyricProgress(1);
                SetLyricText("", model.Status, OverlayConnectionHint());
                return;
            }
            var position = model.Position(Stopwatch.GetTimestamp());
            overlay.SetProgress(position, model.Track.Duration);
            playbackProgress.Visibility = model.Track.Duration > 0 ? Visibility.Visible : Visibility.Hidden;
            playbackProgress.Value = model.Track.Duration > 0 ? Math.Max(0, Math.Min(1, position / model.Track.Duration)) : 0;
            var title = trackTitle.Text;
            progress.Text = model.Track.Duration > 0
                ? String.Format(CultureInfo.InvariantCulture, "{0:mm\\:ss} / {1:mm\\:ss}", TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(model.Track.Duration))
                : String.Format(CultureInfo.InvariantCulture, "已播放 {0:mm\\:ss}", TimeSpan.FromSeconds(position));
            if (selected != null && selected.instrumental) { UpdateLyricsViewer(-1); SetLyricProgress(1); SetLyricText("", title, "纯音乐"); return; }
            if (lyrics == null) { UpdateLyricsViewer(-1); SetLyricProgress(1); SetLyricText("", title, OverlayLyricsHint()); return; }
            if (!lyrics.Synced) { UpdateLyricsViewer(-1); SetLyricProgress(1); SetLyricText("", title, "此歌词为纯文本 · 可在桌面查看全文"); return; }
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
                SetLyricProgress(1);
            }
            else
            {
                UpdateLyricsViewer(index);
                SetLyricProgress(index < 0 ? 1 : lyrics.ProgressAt(index, position, model.Track.Duration, settings.Offset));
            }
            SetLyricText(previousLine, currentLine, nextLine);
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

        internal static void CaptureWindow(Window window, string path)
        {
            window.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(window);
            var content = (FrameworkElement)window.Content;
            var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
            var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX),
                (int)Math.Ceiling(height * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(path)) encoder.Save(output);
        }

        public void Capture(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var wasDisplay = displayPage.Visibility == Visibility.Visible;
            SelectSettingsPage(false);
            musicPage.ScrollToTop();
            CaptureWindow(this, path);
            if (overlay.IsVisible) CaptureWindow(overlay, Path.Combine(directory, "desktop-overlay.png"));
            SelectSettingsPage(true);
            displayPage.ScrollToTop();
            CaptureWindow(this, Path.Combine(directory, "desktop-appearance-settings.png"));
            displayPage.ScrollToBottom();
            CaptureWindow(this, Path.Combine(directory, "desktop-appearance-settings-lower.png"));
            var originalWidth = Width;
            var originalHeight = Height;
            Width = MinWidth;
            Height = MinHeight;
            displayPage.ScrollToTop();
            CaptureWindow(this, Path.Combine(directory, "desktop-appearance-minimum.png"));
            SelectSettingsPage(false);
            musicPage.ScrollToTop();
            CaptureWindow(this, Path.Combine(directory, "desktop-control-minimum.png"));
            Width = originalWidth;
            Height = originalHeight;
            SelectSettingsPage(wasDisplay);
            displayPage.ScrollToTop();
            var dpi = VisualTreeHelper.GetDpi(this);
            Console.WriteLine("SMOKE: snapshot=" + HadSnapshot + " timedLyrics=" + HasTimedLyrics + " dpi=" + (96 * dpi.DpiScaleX));
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
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricConnector");
            var window = new ControlWindow(deviceName, iconPath, directory);
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
