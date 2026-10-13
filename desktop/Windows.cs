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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LyricConnector
{
    public sealed class LyricWindow : Window
    {
        private readonly Border surface;
        private readonly Border panelBackground;
        private readonly TextBlock current;
        private readonly TextBlock next;
        private readonly Button previousTrack;
        private readonly Button playPause;
        private readonly Button nextTrack;
        private readonly StackPanel controls;
        private readonly Border progressTrack;
        private readonly Border progressFill;
        private readonly DispatcherTimer controlsHideTimer;
        private double progressRatio;
        private bool through;
        private bool previousAvailable;
        private bool playPauseAvailable;
        private bool nextAvailable;
        private bool playPauseShowsPlay = true;
        private byte playPauseCommand;
        public event Action<byte> RemoteCommandRequested;
        public event Action SettingsRequested;
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
            Height = 164;
            MinWidth = 360;
            MinHeight = 128;
            var area = SystemParameters.WorkArea;
            Left = settings.Left < 0 ? area.Left + (area.Width - Width) / 2 : Math.Max(area.Left, Math.Min(settings.Left, area.Right - 120));
            Top = settings.Top < 0 ? area.Bottom - Height - 44 : Math.Max(area.Top, Math.Min(settings.Top, area.Bottom - 60));
            surface = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            panelBackground = new Border { CornerRadius = new CornerRadius(12), IsHitTestVisible = false };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 8, 20, 8) };
            current = new TextBlock { TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.Medium };
            next = new TextBlock { TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };
            previousTrack = PlaybackButton("播放上一首", PlaybackGlyph(RemoteCommand.PreviousTrack));
            playPause = PlaybackButton("播放或暂停", PlaybackGlyph(RemoteCommand.Play));
            nextTrack = PlaybackButton("播放下一首", PlaybackGlyph(RemoteCommand.NextTrack));
            previousTrack.Click += delegate { RequestRemoteCommand(RemoteCommand.PreviousTrack); };
            playPause.Click += delegate { RequestRemoteCommand(playPauseCommand); };
            nextTrack.Click += delegate { RequestRemoteCommand(RemoteCommand.NextTrack); };
            controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0), Opacity = 0, IsHitTestVisible = false };
            controls.Children.Add(previousTrack);
            controls.Children.Add(playPause);
            controls.Children.Add(nextTrack);
            progressFill = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(132, 86, 186)), CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
            progressTrack = new Border { Width = 220, Height = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Color.FromArgb(128, 150, 146, 160)), CornerRadius = new CornerRadius(1),
                Child = progressFill, IsHitTestVisible = false };
            progressTrack.SizeChanged += delegate { UpdateProgressFill(); };
            controlsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            controlsHideTimer.Tick += delegate
            {
                controlsHideTimer.Stop();
                if (!surface.IsMouseOver) SetControlsVisible(false);
            };
            var content = new Grid();
            content.Children.Add(panelBackground);
            text.Children.Add(current);
            text.Children.Add(next);
            text.Children.Add(progressTrack);
            text.Children.Add(controls);
            content.Children.Add(text);
            surface.Child = content;
            Content = surface;
            surface.MouseEnter += delegate { controlsHideTimer.Stop(); SetControlsVisible(true); };
            surface.MouseLeave += delegate { controlsHideTimer.Stop(); controlsHideTimer.Start(); };
            surface.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
            {
                if (args.ClickCount != 2) return;
                var handler = SettingsRequested;
                if (handler != null) handler();
                args.Handled = true;
            };
            Closed += delegate { controlsHideTimer.Stop(); };
            current.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
            { if (!through && args.ClickCount == 1) DragMove(); };
            next.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
            { if (!through && args.ClickCount == 1) DragMove(); };
            SourceInitialized += delegate { Apply(settings); };
            SetPlaybackControls(false, false, RemoteCommand.TogglePlayPause, false, false);
            SetText("等待 iPhone 的播放状态", "桌面控制窗口负责歌词与显示设置");
        }

        public void SetText(string first, string second) { current.Text = first; next.Text = second; }
        public void SetLyricsVisible(bool visible)
        {
            current.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            next.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
            var button = new Button { Content = icon, Width = 38, Height = 32, Padding = new Thickness(3), Margin = new Thickness(4, 0, 4, 0),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), FocusVisualStyle = null,
                ToolTip = name };
            AutomationProperties.SetName(button, name);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "buttonSurface";
            var templatedParent = new RelativeSource(RelativeSourceMode.TemplatedParent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
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
            var icon = new Canvas { Width = 28, Height = 24, IsHitTestVisible = false };
            var ink = new SolidColorBrush(Color.FromRgb(248, 247, 250));
            if (command == RemoteCommand.PreviousTrack)
            {
                var bar = new System.Windows.Shapes.Rectangle { Width = 2, Height = 17, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, 3); Canvas.SetTop(bar, 3); icon.Children.Add(bar);
                icon.Children.Add(new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(22, 3), new Point(8, 12), new Point(22, 21) }, Fill = ink });
            }
            else if (command == RemoteCommand.NextTrack)
            {
                icon.Children.Add(new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(6, 3), new Point(20, 12), new Point(6, 21) }, Fill = ink });
                var bar = new System.Windows.Shapes.Rectangle { Width = 2, Height = 17, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, 23); Canvas.SetTop(bar, 3); icon.Children.Add(bar);
            }
            else if (command == RemoteCommand.Pause)
            {
                var first = new System.Windows.Shapes.Rectangle { Width = 3, Height = 17, Fill = ink, RadiusX = 1, RadiusY = 1 };
                var second = new System.Windows.Shapes.Rectangle { Width = 3, Height = 17, Fill = ink, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(first, 8); Canvas.SetTop(first, 3);
                Canvas.SetLeft(second, 17); Canvas.SetTop(second, 3);
                icon.Children.Add(first); icon.Children.Add(second);
            }
            else icon.Children.Add(new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(8, 3), new Point(22, 12), new Point(8, 21) }, Fill = ink });
            return icon;
        }

        private void SetControlsVisible(bool visible)
        {
            controls.IsHitTestVisible = visible;
            controls.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(140)));
        }

        private void RequestRemoteCommand(byte command)
        {
            var handler = RemoteCommandRequested;
            if (handler != null) handler(command);
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
            current.FontFamily = next.FontFamily = new FontFamily(settings.Font);
            current.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(settings.CurrentLineColor);
            next.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(settings.NextLineColor);
            current.FontSize = settings.FontSize;
            next.FontSize = Math.Max(12, settings.FontSize * 0.72);
            panelBackground.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * settings.BackgroundOpacity), 27, 23, 41));
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
        private readonly PlaybackModel model = new PlaybackModel(Stopwatch.Frequency);
        private readonly DesktopSettings settings;
        private readonly string settingsPath;
        private readonly LyricsStore store;
        private readonly LyricWindow overlay;
        private readonly DispatcherTimer ticker;
        private readonly ComboBox devices = new ComboBox { MinWidth = 210 };
        private readonly TextBlock sourceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock trackTitle = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock artist = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock progress = new TextBlock();
        private readonly TextBlock lyricStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox searchTitle = new TextBox();
        private readonly TextBox searchArtist = new TextBox();
        private readonly ListBox candidates = new ListBox { Height = 112 };
        private readonly Button lyricsVisibility;
        private readonly CheckBox clickThrough = new CheckBox { Content = "鼠标穿透（Ctrl + Alt + L 切换）" };
        private readonly TextBlock offsetLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 76, TextAlignment = TextAlignment.Center };
        private CancellationTokenSource sourceCancellation;
        private Task sourceTask = Task.FromResult(0);
        private AmsSource activeSource;
        private CancellationTokenSource queryCancellation;
        private long connectionGeneration;
        private long candidateRevision = -1;
        private Lyrics lyrics;
        private LyricRecord selected;
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
            overlay.SetLyricsVisible(settings.ShowLyrics != false);
            overlay.RemoteCommandRequested += SendRemoteCommand;
            overlay.SettingsRequested += ShowControlWindow;
            lyricsVisibility = Button(settings.ShowLyrics == false ? "显示歌词" : "隐藏歌词", delegate
            {
                settings.ShowLyrics = settings.ShowLyrics == false;
                UpdateLyricsVisibility();
            });
            lyricsVisibility.ToolTip = "切换悬浮歌词文字显示";
            Title = "Lyric Connector · 桌面控制";
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
            trayImage = new System.Drawing.Icon(iconPath);
            trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("打开桌面控制").Click += delegate { ShowControlWindow(); };
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
            root.Children.Add(Row(Button("搜索候选", async delegate { await FindLyricsAsync(true); }), Button("导入 LRC", delegate { Import(); })));
            root.Children.Add(candidates);
            root.Children.Add(Row(Button("使用所选歌词", delegate { Choose(); }), Button("查看歌词文本", delegate { ShowText(); })));
            root.Children.Add(Label("悬浮窗口"));
            root.Children.Add(Row(new TextBlock { Text = "歌词文字", VerticalAlignment = VerticalAlignment.Center }, lyricsVisibility));
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
                overlay.Show();
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
        private void UpdateLyricsVisibility()
        {
            var visible = settings.ShowLyrics != false;
            overlay.SetLyricsVisible(visible);
            lyricsVisibility.Content = visible ? "隐藏歌词" : "显示歌词";
            lyricsVisibility.ToolTip = visible ? "隐藏悬浮歌词文字" : "显示悬浮歌词文字";
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
                if (autoConnect && target != null) await ConnectAsync();
            }
            catch (Exception error) { sourceStatus.Text = "设备读取失败：" + error.Message; }
        }

        private async Task ConnectAsync()
        {
            var device = devices.SelectedItem as DeviceChoice;
            if (device == null) { sourceStatus.Text = "请先选择你的 iPhone。"; return; }
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
            Console.WriteLine("AMS: " + update.Kind + " r" + update.Revision + " " + model.Status);
            if (changed || update.Kind == UpdateKind.Transition || update.Kind == UpdateKind.Disconnected)
            {
                if (queryCancellation != null) queryCancellation.Cancel();
                lyrics = null; selected = null; candidates.ItemsSource = null; candidateRevision = -1;
                lyricStatus.Text = "";
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
                    var pending = FindLyricsAsync(false);
                }
            }
            else { trackTitle.Text = "等待歌曲信息"; artist.Text = ""; progress.Text = ""; }
            Render();
        }

        private async Task FindLyricsAsync(bool manual)
        {
            if (!model.Ready) { lyricStatus.Text = "歌曲就绪后可检索歌词。"; return; }
            if (manual && searchTitle.Text.Trim().Length == 0) { lyricStatus.Text = "请输入歌曲名称。"; return; }
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
                    ? new LyricResult { Candidates = await store.SearchAsync(searchTitle.Text.Trim(), searchArtist.Text.Trim(), token) }
                    : await store.FindAsync(track, token);
                if (token.IsCancellationRequested || closing || revision != model.Revision) return;
                if (result.Selected != null) SetLyrics(result.Selected);
                else
                {
                    candidateRevision = revision;
                    candidates.ItemsSource = result.Candidates;
                    var query = SongQuery.Infer(track);
                    var inference = !manual && query.Inferred ? "已提取歌名《" + query.Title + "》。" : "";
                    lyricStatus.Text = inference + (result.Candidates.Count == 0 ? "未找到歌词。可修正标题 / 歌手，或导入本地 LRC。" : "找到 " + result.Candidates.Count + " 个候选，请核对版本后选择。");
                }
            }
            catch (OperationCanceledException) { if (!token.IsCancellationRequested && revision == model.Revision) lyricStatus.Text = "歌词请求超时，可重新搜索或导入 LRC。"; }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && !closing && revision == model.Revision)
                {
                    Console.WriteLine("LYRICS_ERROR: " + error.Message);
                    var serviceError = error as System.Net.Http.HttpRequestException;
                    lyricStatus.Text = serviceError != null && serviceError.Message.StartsWith("歌词服务", StringComparison.Ordinal)
                        ? serviceError.Message : "歌词查询暂时失败，请检查网络后重试，或导入本地 LRC。";
                }
            }
        }

        private void SetLyrics(LyricRecord record)
        {
            store.Save(model.Track, record);
            selected = record;
            lyrics = record.Document();
            lyricStatus.Text = record.Source + " · " + (record.instrumental ? "纯音乐" : lyrics.Synced ? lyrics.Lines.Count + " 行同步歌词" : "纯文本歌词，无时间轴");
            Console.WriteLine("LYRICS: " + lyricStatus.Text);
            Render();
        }
        private void Choose()
        {
            if (!model.Ready || candidateRevision != model.Revision) return;
            var record = candidates.SelectedItem as LyricRecord;
            if (record == null) { lyricStatus.Text = "先选择一个候选。"; return; }
            try { if (queryCancellation != null) queryCancellation.Cancel(); SetLyrics(record); }
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
            var viewer = new Window { Title = "歌词文本", Width = 620, Height = 640, Owner = this, Icon = this.Icon, Content = new TextBox
                { Text = lyrics.PlainText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) } };
            viewer.Show();
        }
        private void Render()
        {
            UpdatePlaybackControls();
            if (!model.Ready) { overlay.SetProgress(0, 0); overlay.SetText(model.Status, "在桌面控制窗口中连接 iPhone"); return; }
            var position = model.Position(Stopwatch.GetTimestamp());
            overlay.SetProgress(position, model.Track.Duration);
            var title = trackTitle.Text;
            progress.Text = String.Format(CultureInfo.InvariantCulture, "{0:mm\\:ss} / {1:mm\\:ss}", TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(model.Track.Duration));
            if (selected != null && selected.instrumental) { overlay.SetText(title, "纯音乐"); return; }
            if (lyrics == null) { overlay.SetText(title, "等待歌词 · 请在桌面选择候选或导入 LRC"); return; }
            if (!lyrics.Synced) { overlay.SetText(title, "此歌词为纯文本 · 可在桌面查看全文"); return; }
            var index = lyrics.IndexAt(position, settings.Offset);
            var currentLine = index < 0 ? title : lyrics.Lines[index].Text;
            var nextLine = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Text : "";
            if ((model.Track.Duration > 0 && position >= model.Track.Duration) || String.IsNullOrWhiteSpace(currentLine))
                currentLine = title;
            overlay.SetText(currentLine, nextLine);
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
