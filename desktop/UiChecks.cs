using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LyricConnector
{
    public static class UiChecks
    {
        private static int checks;

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(target, arguments);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL UI: " + name);
            checks++;
            Console.WriteLine("PASS UI: " + name);
        }

        private static int VisibleSpotlightButtons(ControlWindow window)
        {
            return Descendants(Field<Border>(window, "spotlightCallout"))
                .OfType<Button>().Count(button => button.IsVisible);
        }

        private static void CheckSpotlightLayout(ControlWindow window, string step)
        {
            window.UpdateLayout();
            Invoke(window, "UpdateSpotlight");
            window.UpdateLayout();
            var host = Field<Grid>(window, "spotlightHost");
            var callout = Field<Border>(window, "spotlightCallout");
            var target = (FrameworkElement)Invoke(window, "ConnectionTourTarget");
            var calloutBounds = callout.TransformToAncestor(host).TransformBounds(new Rect(callout.RenderSize));
            var targetBounds = target.TransformToAncestor(host).TransformBounds(new Rect(target.RenderSize));
            targetBounds.Inflate(6, 6);
            Check(callout.MaxWidth <= 320 && calloutBounds.Width <= 320.5
                && calloutBounds.Left >= -0.5 && calloutBounds.Top >= -0.5
                && calloutBounds.Right <= host.ActualWidth + 0.5 && calloutBounds.Bottom <= host.ActualHeight + 0.5,
                step + " callout stays within the window at 320px maximum width");
            Check(!calloutBounds.IntersectsWith(targetBounds), step + " callout stays clear of its highlighted target");
        }

        private static void CheckSpotlightTour(ControlWindow window, DeviceChoice fixtureDevice, string screenshots)
        {
            var settings = Field<DesktopSettings>(window, "settings");
            var devices = Field<ComboBox>(window, "devices");
            var guide = Field<Border>(window, "connectionGuidePanel");
            var guideDescription = Field<TextBlock>(window, "connectionGuideDescription");
            var bluetoothSettings = Field<Button>(window, "openBluetoothSettingsButton");
            var help = Field<Button>(window, "connectionHelpButton");
            var skip = Field<Button>(window, "spotlightSkip");
            var previous = Field<Button>(window, "spotlightPrevious");
            var action = Field<Button>(window, "spotlightAction");
            var connect = Field<Button>(window, "connect");
            if (Field<bool>(window, "connectionTourActive")) Invoke(window, "DismissConnectionTour");

            settings.ConnectionTourCompleted = true;
            devices.ItemsSource = new DeviceChoice[0];
            devices.SelectedIndex = -1;
            guide.Visibility = Visibility.Collapsed;
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(guide.Visibility == Visibility.Visible && !Field<bool>(window, "connectionTourActive"),
                "connection help opens its normal inline instructions outside the tour");
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field<Button>(window, "connectionTourRestartButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<bool>(window, "connectionTourActive") && Field<int>(window, "connectionTourStep") == 1,
                "the inline help can manually restart the tour when startup display is disabled");
            Check(Object.ReferenceEquals(Invoke(window, "ConnectionTourTarget"), bluetoothSettings)
                && guide.Visibility == Visibility.Visible && guideDescription.Visibility == Visibility.Collapsed
                && Field<Button>(window, "connectionTourRestartButton").Visibility == Visibility.Collapsed
                && !Object.ReferenceEquals(bluetoothSettings.Parent, Field<Grid>(window, "connectionDeviceRow")),
                "step 1 highlights the Bluetooth settings button inside connection help and hides duplicate instructions");
            Check(skip.IsVisible && !previous.IsVisible && !action.IsVisible && VisibleSpotlightButtons(window) == 1,
                "step 1 shows only the weak skip entry until pairing returns");
            CheckSpotlightLayout(window, "step 1");
            ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "spotlight-tour-step-1.png"));

            skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!Field<bool>(window, "connectionTourActive")
                && Field<Canvas>(window, "spotlightLayer").Visibility == Visibility.Collapsed
                && guide.Visibility == Visibility.Visible && guideDescription.Visibility == Visibility.Visible
                && Field<Button>(window, "refreshDevices").IsEnabled,
                "skipping closes the overlay, restores normal help and leaves page controls usable");
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field<Button>(window, "connectionTourRestartButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            typeof(ControlWindow).GetField("bluetoothSettingsReturned", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(window, true);
            Invoke(window, "UpdateConnectionControls");
            Check(action.IsVisible && (string)action.Content == "已完成" && VisibleSpotlightButtons(window) == 2,
                "step 1 offers one completion confirmation only after returning without a discovered device");
            CheckSpotlightLayout(window, "step 1 confirmation");
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Check(Field<int>(window, "connectionTourStep") == 2
                && Object.ReferenceEquals(Invoke(window, "ConnectionTourTarget"), Field<Grid>(window, "connectionDeviceRow"))
                && !action.IsVisible && previous.IsVisible && VisibleSpotlightButtons(window) == 2,
                "step 2 highlights the real refresh and selection row without a next button");
            CheckSpotlightLayout(window, "step 2");
            ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "spotlight-tour-step-2.png"));
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            devices.ItemsSource = new[] { fixtureDevice };
            devices.SelectedIndex = 0;
            Invoke(window, "AdvanceConnectionTourAfterDeviceRefresh", devices.Items.Count);
            Check(Field<int>(window, "connectionTourStep") == 3
                && Object.ReferenceEquals(Invoke(window, "ConnectionTourTarget"), connect)
                && !action.IsVisible && !previous.IsVisible && connect.IsEnabled && VisibleSpotlightButtons(window) == 1,
                "device discovery and selection advance to the real connect button");
            CheckSpotlightLayout(window, "step 3");
            ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "spotlight-tour-step-3.png"));

            typeof(ControlWindow).GetField("connectionBusy", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(window, true);
            Invoke(window, "UpdateConnectionControls");
            Check(Field<int>(window, "connectionTourStep") == 3 && !connect.IsEnabled
                && VisibleSpotlightButtons(window) == 1,
                "connecting keeps the tour on step 3 and disables the real button while waiting");
            typeof(ControlWindow).GetField("connectionBusy", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(window, false);
            var model = Field<PlaybackModel>(window, "model");
            Invoke(window, "Accept", new SourceUpdate { Kind = UpdateKind.Disconnected,
                Revision = model.Revision + 1, Message = "测试连接失败" });
            Check(Field<bool>(window, "connectionTourActive") && Field<int>(window, "connectionTourStep") == 3
                && Field<bool>(window, "connectionTourConnectFailed") && connect.IsEnabled
                && Field<TextBlock>(window, "spotlightDescription").Text.Contains("测试连接失败")
                && previous.IsVisible && VisibleSpotlightButtons(window) == 2,
                "connection failure stays on step 3, shows the error, enables retry and allows changing device");
            CheckSpotlightLayout(window, "step 3 retry");
            ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "spotlight-tour-step-3-retry.png"));
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<int>(window, "connectionTourStep") == 2 && devices.SelectedItem == null
                && Object.ReferenceEquals(Invoke(window, "ConnectionTourTarget"), Field<Grid>(window, "connectionDeviceRow"))
                && !action.IsVisible && VisibleSpotlightButtons(window) == 1,
                "previous appears after failure and clears selection so another device can be chosen");
            devices.SelectedIndex = 0;
            var activeSourceField = typeof(ControlWindow).GetField("activeSource", BindingFlags.Instance | BindingFlags.NonPublic);
            activeSourceField.SetValue(window, new AmsSource(fixtureDevice, update => { }, model.Revision));
            Invoke(window, "Accept", new SourceUpdate { Kind = UpdateKind.Connected,
                Revision = model.Revision + 1, Message = "测试连接成功" });
            Check(!Field<bool>(window, "connectionTourActive")
                && Field<Canvas>(window, "spotlightLayer").Visibility == Visibility.Collapsed
                && guide.Visibility == Visibility.Visible,
                "successful connection closes the tour automatically and restores normal help");
            activeSourceField.SetValue(window, null);
            typeof(ControlWindow).GetField("connectionBusy", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(window, false);
            Invoke(window, "UpdateConnectionControls");
        }

        private static void Enter(ControlWindow window, TextBox input, string text)
        {
            input.Text = text;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
        }

        public static void Run(string iconPath, string screenshots)
        {
            checks = 0;
            var directory = Path.Combine(Path.GetTempPath(), "LyricConnector-UI-" + Guid.NewGuid().ToString("N"));
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new ControlWindow("", iconPath, directory);
            Exception failure = null;
            var started = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += async delegate
            {
                if (Field<bool>(window, "refreshingDevices") && started.Elapsed.TotalSeconds < 20) return;
                timer.Stop();
                try
                {
                    Check(!Field<bool>(window, "refreshingDevices"), "device discovery completes without connecting a fixture device");
                    var settings = Field<DesktopSettings>(window, "settings");
                    var model = Field<PlaybackModel>(window, "model");
                    var reconnect = Field<DispatcherTimer>(window, "reconnectTimer");
                    var fixtureDevice = new DeviceChoice { Id = "ui-fixture-device", Name = "iPhone 15 Pro" };
                    CheckSpotlightTour(window, fixtureDevice, screenshots);
                    settings.DeviceId = fixtureDevice.Id;
                    await (Task)Invoke(window, "DisconnectAsync");
                    Invoke(window, "StartAutoReconnect");
                    Check(!reconnect.IsEnabled && !model.Ready, "manual disconnect clears playback and suppresses automatic reconnect");
                    window.GetType().GetField("manuallyDisconnected", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, false);
                    Invoke(window, "Accept", new SourceUpdate { Kind = UpdateKind.Disconnected, Revision = model.Revision + 1, Message = "测试意外断开" });
                    Check(reconnect.IsEnabled, "unexpected disconnect still schedules automatic reconnect");
                    Check(Field<TextBlock>(window, "sourceStatus").Text == "测试意外断开", "automatic reconnect preserves the connection error");
                    reconnect.Stop();
                    var devices = Field<ComboBox>(window, "devices");
                    devices.ItemsSource = new[] { fixtureDevice };
                    devices.SelectedIndex = 0;
                    Check(devices.ToolTip as string == fixtureDevice.Id && ToolTipService.GetShowOnDisabled(devices),
                        "full device ID remains available when the connected selector is disabled");
                    window.GetType().GetField("activeSource", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(window, new AmsSource(fixtureDevice, update => { }, model.Revision));
                    window.GetType().GetField("connectionBusy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                    Invoke(window, "UpdateConnectionControls");
                    var connect = Field<Button>(window, "connect");
                    Check(connect.IsEnabled && (string)connect.Content == "取消连接", "in-progress connection can be cancelled");
                    Invoke(window, "Accept", new SourceUpdate { Kind = UpdateKind.Connected, Revision = model.Revision, Message = "测试连接" });
                    Check(Field<Border>(window, "connectionGuidePanel").Visibility == Visibility.Collapsed,
                        "successful connection collapses first-use connection help");
                    var connectionDetails = Field<StackPanel>(window, "connectionDetails");
                    Check(connectionDetails.Visibility == Visibility.Visible && devices.IsVisible,
                        "device selector remains visible after connection");
                    Check(Field<TextBlock>(window, "playbackConnectionStatus").Text == "已连接",
                        "playback panel shows the actual Bluetooth connection state");
                    Check(!devices.IsEnabled, "connected device selector stays visible but disabled");

                    var snapshot = new SourceUpdate { Kind = UpdateKind.Snapshot, Revision = model.Revision + 1,
                        Track = new Track { Title = "爱是怀疑", Artist = "Eason Chan", Album = "", Duration = 273 },
                        Player = "Apple Music", Playback = Playback.Parse("0,1.0,151", Stopwatch.GetTimestamp()) };
                    model.Accept(snapshot);
                    Invoke(window, "Accept", snapshot);
                    Field<TextBox>(window, "searchTitle").Text = snapshot.Track.Title;
                    Field<TextBox>(window, "searchArtist").Text = snapshot.Track.Artist;
                    var first = new LyricRecord { id = 1, trackName = "爱是怀疑", artistName = "Eason Chan", albumName = "反正是我", duration = 273,
                        syncedLyrics = "[00:00]爱是怀疑\n[02:20]忘记了自己也有这样的过去\n[02:50]下一句歌词" };
                    var second = new LyricRecord { id = 2, trackName = "爱是怀疑 (Live)", artistName = "Eason Chan", duration = 280,
                        syncedLyrics = "[00:00]另一版本\n[02:20]现场版本歌词" };
                    var list = Field<ListBox>(window, "candidates");
                    list.ItemsSource = new[] { new LyricMatch { Record = first, Score = 100, EvidenceScore = 100 },
                        new LyricMatch { Record = second, Score = 90, EvidenceScore = 90 } };
                    window.GetType().GetField("candidateRevision", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, model.Revision);
                    Invoke(window, "UpdateCandidatePlaceholder");
                    Invoke(window, "DisplayLyrics", first);
                    list.SelectedIndex = 0;
                    window.UpdateLayout();
                    var selectedRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var selector = (FrameworkElement)selectedRow.Template.FindName("selector", selectedRow);
                    var selectionDot = (FrameworkElement)selectedRow.Template.FindName("dot", selectedRow);
                    var dotCenter = selectionDot.TransformToAncestor(selector).Transform(
                        new Point(selectionDot.ActualWidth / 2, selectionDot.ActualHeight / 2));
                    Check(Math.Abs(dotCenter.X - selector.ActualWidth / 2) < 0.1
                        && Math.Abs(dotCenter.Y - selector.ActualHeight / 2) < 0.1,
                        "candidate selection dot stays concentric with its outer circle at actual display DPI");
                    var playbackProgress = Field<ProgressBar>(window, "playbackProgress");
                    var progressTrack = (FrameworkElement)playbackProgress.Template.FindName("PART_Track", playbackProgress);
                    var progressFill = (FrameworkElement)playbackProgress.Template.FindName("PART_Indicator", playbackProgress);
                    Check(playbackProgress.Value > 0 && playbackProgress.Value < 1
                        && Math.Abs(progressFill.ActualWidth - progressTrack.ActualWidth * playbackProgress.Value) < 1,
                        "rounded playback indicator renders the actual song position proportionally");
                    Check(Descendants(list).OfType<TextBlock>().Any(block => block.Inlines.OfType<System.Windows.Documents.Run>()
                        .Any(run => run.Text == first.albumName)), "candidate rows show available album information");
                    var choose = Field<Button>(window, "choose");
                    Check(!choose.IsEnabled && choose.Visibility == Visibility.Collapsed, "applied lyrics do not show a redundant apply button");
                    list.SelectedIndex = 1;
                    Check(choose.IsEnabled, "selecting an alternative enables explicit lyric selection");
                    var alternativeRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                    var alternativeDot = (FrameworkElement)alternativeRow.Template.FindName("dot", alternativeRow);
                    Check(selectionDot.Visibility == Visibility.Hidden && alternativeDot.Visibility == Visibility.Visible,
                        "switching candidates moves the single selection dot to the chosen row");
                    var revision = model.Revision;
                    var source = Field<object>(window, "activeSource");
                    Invoke(window, "SelectSettingsPage", true);
                    window.UpdateLayout();
                    Check(model.Revision == revision && Object.ReferenceEquals(source, Field<object>(window, "activeSource")),
                        "switching tabs preserves the playback and connection session");
                    window.Overlay.Hide();
                    Invoke(window, "Render");
                    var preview = Field<LyricTextView>(window, "preview");
                    var active = (TextBlock)preview.Children[1];
                    var overlayText = Field<LyricTextView>(window.Overlay, "lyricText");
                    Check(active.Text == "忘记了自己也有这样的过去" && active.Text == ((TextBlock)overlayText.Children[1]).Text,
                        "hidden desktop overlay and embedded preview share the real playback lyric");

                    var controls = Descendants(window).ToList();
                    var font = controls.OfType<ComboBox>().Single(input => AutomationProperties.GetName(input) == "字体");
                    var opacityInput = controls.OfType<TextBox>().SingleOrDefault(input => AutomationProperties.GetName(input) == "背景不透明度，%");
                    Check(opacityInput != null && !font.IsEditable,
                        "opacity percentage is editable while font selection is not a text editor");
                    var visibility = Field<System.Windows.Controls.Primitives.ToggleButton>(window, "overlayVisibility");
                    visibility.IsChecked = false;
                    visibility.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(settings.ShowLyrics == false && !window.Overlay.IsVisible, "display switch hides the real floating window");
                    window.Activate();
                    visibility.Focus();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    window.UpdateLayout();
                    var rail = (Border)visibility.Template.FindName("rail", visibility);
                    var knob = (System.Windows.Shapes.Ellipse)visibility.Template.FindName("knob", visibility);
                    var knobOrigin = knob.TransformToAncestor(rail).Transform(new Point());
                    Check(visibility.IsKeyboardFocused && knobOrigin.Y >= rail.BorderThickness.Top
                        && knobOrigin.Y + knob.ActualHeight <= rail.ActualHeight - rail.BorderThickness.Bottom
                        && knob.ActualWidth == knob.ActualHeight,
                        "focused off switch keeps the complete circular thumb inside the track");
                    Check(((SolidColorBrush)rail.BorderBrush).Color == (Color)ColorConverter.ConvertFromString("#493257"),
                        "focused switch shows a visible keyboard focus border");
                    var switchImage = new DrawingVisual();
                    using (var drawing = switchImage.RenderOpen())
                        drawing.DrawRectangle(new VisualBrush(rail), null, new Rect(0, 0, rail.ActualWidth, rail.ActualHeight));
                    var switchDpi = VisualTreeHelper.GetDpi(rail);
                    var switchBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(rail.ActualWidth * switchDpi.DpiScaleX),
                        (int)Math.Ceiling(rail.ActualHeight * switchDpi.DpiScaleY),
                        96 * switchDpi.DpiScaleX, 96 * switchDpi.DpiScaleY, PixelFormats.Pbgra32);
                    switchBitmap.Render(switchImage);
                    var switchEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    switchEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(switchBitmap));
                    using (var output = File.Create(Path.Combine(screenshots, "switch-off-focused.png"))) switchEncoder.Save(output);
                    visibility.IsChecked = true;
                    visibility.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(settings.ShowLyrics == true && window.Overlay.IsVisible, "display switch restores the real floating window");
                    window.Activate();
                    font.BringIntoView();
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    font.Focus();
                    var dropdownToggle = (System.Windows.Controls.Primitives.ToggleButton)font.Template.FindName("toggle", font);
                    var wholeSurfaceClickable = true;
                    foreach (var point in new[] { new Point(15, font.ActualHeight / 2), new Point(font.ActualWidth / 2, font.ActualHeight / 2),
                        new Point(font.ActualWidth - 15, font.ActualHeight / 2) })
                    {
                        var hit = font.InputHitTest(point) as DependencyObject;
                        while (hit != null && !(hit is System.Windows.Controls.Primitives.ToggleButton)) hit = VisualTreeHelper.GetParent(hit);
                        wholeSurfaceClickable &= Object.ReferenceEquals(hit, dropdownToggle);
                    }
                    Check(wholeSurfaceClickable && font.Template.FindName("PART_EditableTextBox", font) == null,
                        "font label, center and arrow all target the same dropdown button without a text editor");
                    typeof(System.Windows.Controls.Primitives.ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(dropdownToggle, new object[0]);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Check(((System.Windows.Controls.Primitives.Popup)font.Template.FindName("PART_Popup", font)).IsOpen,
                        "custom font selector opens its real dropdown");
                    font.IsDropDownOpen = false;
                    font.SelectedItem = "Segoe UI";
                    Check(settings.Font == "Segoe UI" && active.FontFamily.Source == "Segoe UI"
                        && ((TextBlock)overlayText.Children[1]).FontFamily.Source == "Segoe UI",
                        "selecting a font immediately updates settings and both lyric renderers");
                    font.SelectedItem = "Microsoft YaHei UI";
                    var colorInput = controls.OfType<TextBox>().Single(input => AutomationProperties.GetName(input) == "当前行颜色");
                    var originalColor = settings.CurrentLineColor;
                    foreach (var invalid in new[] { "", "#GGGGGG", "not-a-color" })
                    {
                        Enter(window, colorInput, invalid);
                        Check(settings.CurrentLineColor == originalColor, "invalid color retains the last valid appearance: " + invalid);
                    }
                    Enter(window, colorInput, "#80ABCDEF");
                    Check(settings.CurrentLineColor == "#80ABCDEF"
                        && ((LinearGradientBrush)active.Foreground).GradientStops[0].Color.A == 128,
                        "Enter commits valid HEX colors and preserves alpha in the preview");
                    var paletteButton = controls.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "当前行颜色预设");
                    window.Activate();
                    paletteButton.Focus();
                    paletteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    var preset = Keyboard.FocusedElement as Button;
                    Check(preset != null && AutomationProperties.GetName(preset).StartsWith("暖白"),
                        "opening the compact palette moves keyboard focus to a color preset");
                    preset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(colorInput.Text == "#FFF1D6" && settings.CurrentLineColor == "#FFFFF1D6",
                        "choosing a palette color immediately applies it without a separate button");
                    Enter(window, colorInput, originalColor);
                    var sizeInput = controls.OfType<TextBox>().Single(input => AutomationProperties.GetName(input) == "歌词字号，px");
                    Enter(window, sizeInput, "57");
                    Check(settings.FontSize == 16, "out-of-range font input retains the previous size");
                    Enter(window, sizeInput, "56");
                    Check(settings.FontSize == 56 && active.FontSize == 56
                        && ((TextBlock)overlayText.Children[1]).FontSize == 56, "exact font input updates both renderers");
                    Enter(window, sizeInput, "16");
                    var sizeSlider = controls.OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == "歌词字号");
                    sizeSlider.Focus();
                    window.UpdateLayout();
                    Check(((SolidColorBrush)((Grid)sizeSlider.Template.FindName("sliderSurface", sizeSlider)).Background).Color.A == 0,
                        "focused font slider does not paint a background below the track");
                    var opacity = controls.OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == "背景不透明度");
                    opacityInput.BringIntoView();
                    window.Activate();
                    opacityInput.Focus();
                    Check(opacityInput.IsKeyboardFocused && !opacityInput.IsReadOnly,
                        "opacity percentage accepts keyboard focus for direct input");
                    foreach (var percentage in new[] { 0, 100, 37 })
                    {
                        Enter(window, opacityInput, percentage.ToString());
                        Check(Math.Abs(settings.BackgroundOpacity - percentage / 100.0) < 0.001
                            && Math.Abs(opacity.Value - percentage / 100.0) < 0.001
                            && ((SolidColorBrush)Field<Border>(window, "previewBackground").Background).Color.A == (byte)(percentage / 100.0 * 255),
                            "opacity input applies percentage to slider and preview: " + percentage);
                    }
                    foreach (var invalid in new[] { "", "abc", "-1", "101", "25.5" })
                    {
                        Enter(window, opacityInput, invalid);
                        Check(Math.Abs(settings.BackgroundOpacity - 0.37) < 0.001,
                            "invalid opacity retains the last valid value: " + invalid);
                    }
                    opacityInput.Text = "42";
                    sizeSlider.Focus();
                    Check(Math.Abs(settings.BackgroundOpacity - 0.42) < 0.001,
                        "leaving the opacity input commits a valid percentage");
                    opacity.Value = 0.65;
                    Check(opacityInput.Text == "65", "dragging the opacity slider synchronizes its editable percentage");
                    opacity.Value = 0;
                    Check(((SolidColorBrush)Field<Border>(window, "previewBackground").Background).Color.A == 0,
                        "zero opacity removes the lyric panel while keeping the example desktop visible");
                    opacity.Value = 1;
                    Check(((SolidColorBrush)Field<Border>(window, "previewBackground").Background).Color.A == 255,
                        "full opacity renders an opaque lyric panel");
                    Enter(window, sizeInput, "56");
                    Enter(window, sizeInput, "57");
                    foreach (var appearance in new[] {
                        new { Name = "标准", Size = 24.0, Opacity = 0.45 },
                        new { Name = "大字体", Size = 32.0, Opacity = 0.64 },
                        new { Name = "简约", Size = 16.0, Opacity = 0.0 }
                    })
                    {
                        var appearanceButton = controls.OfType<Button>().Single(button => button.Content as string == appearance.Name);
                        appearanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var saved = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DesktopSettings>(
                            File.ReadAllText(Path.Combine(directory, "settings.json")));
                        Check(settings.FontSize == appearance.Size && active.FontSize == appearance.Size
                            && saved.FontSize == appearance.Size && Math.Abs(saved.BackgroundOpacity - appearance.Opacity) < 0.001
                            && sizeInput.Text == appearance.Size.ToString("0") && opacityInput.Text == (appearance.Opacity * 100).ToString("0")
                            && saved.CurrentLineColor == settings.CurrentLineColor
                            && AutomationProperties.GetItemStatus(appearanceButton) == "当前预设",
                            appearance.Name + " preset synchronizes controls, preview, selection and saved settings");
                    }
                    Enter(window, opacityInput, "invalid");
                    controls.OfType<Button>().Single(button => button.Content as string == "简约")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(opacityInput.Text == "0" && settings.BackgroundOpacity == 0,
                        "reapplying the same preset clears invalid percentage text");
                    var increaseOffset = controls.OfType<Button>().Single(button => button.Content as string == "+ 0.25 秒");
                    var decreaseOffset = controls.OfType<Button>().Single(button => button.Content as string == "− 0.25 秒");
                    var resetOffset = controls.OfType<Button>().Single(button => button.Content as string == "归零");
                    resetOffset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    increaseOffset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(settings.Offset == 0.25 && Field<TextBlock>(window, "offsetLabel").Text == "+0.25 s",
                        "time increase button applies a quarter second and updates its readout");
                    decreaseOffset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    decreaseOffset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(settings.Offset == -0.25, "time decrease button applies quarter second steps");
                    resetOffset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(settings.Offset == 0, "time reset button returns the offset to zero");
                    opacity.Value = 0.25;
                    Check(controls.OfType<Button>().All(button => AutomationProperties.GetItemStatus(button) != "当前预设"),
                        "custom appearance clears the preset highlight");
                    opacity.Value = 0.64;
                    list.SelectedIndex = 0;
                    window.Capture(Path.Combine(screenshots, "desktop-control.png"));
                    var originalCandidates = list.ItemsSource;
                    var manyCandidates = new List<LyricMatch>((IEnumerable<LyricMatch>)originalCandidates);
                    for (var version = 3; version <= 12; version++)
                        manyCandidates.Add(new LyricMatch { Record = new LyricRecord { id = version, trackName = "爱是怀疑 (版本 " + version + ")",
                            artistName = "Eason Chan", duration = 273, syncedLyrics = first.syncedLyrics }, Score = 80, EvidenceScore = 90 });
                    list.ItemsSource = manyCandidates;
                    Invoke(window, "SelectSettingsPage", false);
                    window.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    var candidateScroller = Descendants(list).OfType<ScrollViewer>().Single();
                    var candidateRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    Check(list.Items.Count == 12 && Math.Abs(candidateScroller.ViewportHeight - 5 * candidateRow.ActualHeight) < 1
                        && candidateScroller.ScrollableHeight > 0, "candidate list shows five rows without discarding additional versions");
                    list.ScrollIntoView(manyCandidates.Last());
                    list.SelectedItem = manyCandidates.Last();
                    window.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    Check(candidateScroller.VerticalOffset > 0 && choose.IsEnabled,
                        "candidate scrollbar reaches and selects versions beyond the first five");
                    Field<ScrollViewer>(window, "musicPage").ScrollToBottom();
                    ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "desktop-candidates-scroll.png"));
                    list.SelectedIndex = 0;
                    list.ScrollIntoView(manyCandidates[0]);
                    Field<ScrollViewer>(window, "musicPage").ScrollToTop();
                    Invoke(window, "SelectSettingsPage", true);
                    window.WindowState = WindowState.Maximized;
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    window.UpdateLayout();
                    var musicPage = Field<ScrollViewer>(window, "musicPage");
                    var displayPage = Field<ScrollViewer>(window, "displayPage");
                    var musicBounds = musicPage.TransformToAncestor(window).TransformBounds(new Rect(musicPage.RenderSize));
                    var displayBounds = displayPage.TransformToAncestor(window).TransformBounds(new Rect(displayPage.RenderSize));
                    Check(musicPage.IsVisible && displayPage.IsVisible && displayBounds.Left > musicBounds.Right
                        && Field<Border>(window, "settingsTabs").Visibility == Visibility.Collapsed,
                        "maximized window presents music and floating settings side by side");
                    var surface = (Border)Field<Grid>(window, "spotlightHost").Children[0];
                    var root = (Grid)surface.Child;
                    Check(Math.Abs(root.ActualWidth - (surface.ActualWidth - 50)) < 1
                        && root.Margin.Left == 24 && root.Margin.Right == 24,
                        "wide window fills the client area with fixed 24 DIP side padding and a one DIP frame");
                    var trackPanel = root.Children.OfType<Border>().Single(border => Grid.GetRow(border) == 1);
                    Check(Math.Abs(trackPanel.ActualWidth - root.ActualWidth) < 1,
                        "playback panel stretches across the complete content width");
                    Check(Math.Abs(musicPage.ActualWidth / (musicPage.ActualWidth + displayPage.ActualWidth) - 0.55) < 0.001,
                        "wide columns divide available width in a 55 to 45 ratio");
                    window.UpdateLayout();
                    Check(((FrameworkElement)devices.Parent).ActualWidth <= 640
                        && Field<StackPanel>(window, "connectionDetails").ActualWidth > ((FrameworkElement)devices.Parent).ActualWidth,
                        "device operations stay bounded inside a full width connection section");
                    Check(((FrameworkElement)((FrameworkElement)Field<TextBox>(window, "searchTitle").Parent).Parent).ActualWidth <= 720
                        && Field<TextBox>(window, "searchTitle").ActualWidth <= 360
                        && Field<TextBox>(window, "searchArtist").ActualWidth <= 360,
                        "maximized song and artist search inputs keep reasonable width limits");
                    Check(Math.Abs(((FrameworkElement)Field<Border>(window, "previewBackground").Parent).ActualWidth
                        - (displayPage.ViewportWidth - 8)) < 1
                        && font.ActualWidth <= 420 && ((FrameworkElement)sizeSlider.Parent).ActualWidth <= 520,
                        "lyric preview stretches while font and slider controls keep their width limits");
                    var title = Field<TextBlock>(window, "trackTitle");
                    var artist = Field<TextBlock>(window, "artist");
                    var titleBounds = title.TransformToAncestor(window).TransformBounds(new Rect(title.RenderSize));
                    var artistBounds = artist.TransformToAncestor(window).TransformBounds(new Rect(artist.RenderSize));
                    Check(artistBounds.Top >= titleBounds.Bottom && Math.Abs(artistBounds.Left - titleBounds.Left) < 20,
                        "playback panel places artist and source below the song title");
                    Check(model.Revision == revision && Object.ReferenceEquals(source, Field<object>(window, "activeSource")),
                        "maximizing preserves the active playback and connection session");
                    ControlWindow.CaptureWindow(window, Path.Combine(screenshots, "desktop-maximized.png"));
                    window.WindowState = WindowState.Normal;
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    Check(displayPage.IsVisible && !musicPage.IsVisible && Field<Border>(window, "settingsTabs").IsVisible,
                        "restoring the window returns to the previously selected tab");
                    list.ItemsSource = originalCandidates;
                    list.SelectedIndex = 0;
                    foreach (var dimensions in new[] { new Size(1800, 1000), new Size(1200, 790), new Size(700, 790), new Size(620, 620) })
                    {
                        window.Width = dimensions.Width;
                        window.Height = dimensions.Height;
                        foreach (var display in new[] { false, true })
                        {
                            Invoke(window, "SelectSettingsPage", display);
                            window.UpdateLayout();
                            Check(Math.Abs(root.ActualWidth - (surface.ActualWidth - 50)) < 1,
                                "content follows window width at " + dimensions);
                            var split = dimensions.Width >= 1100;
                            Check(musicPage.IsVisible == (split || !display) && displayPage.IsVisible == (split || display),
                                "column layout follows window width at " + dimensions + " display=" + display);
                            if (split)
                                Check(Math.Abs(musicPage.ActualWidth / (musicPage.ActualWidth + displayPage.ActualWidth) - 0.55) < 0.001,
                                    "resizing retains the 55 to 45 column ratio at " + dimensions);
                            var page = Field<ScrollViewer>(window, display ? "displayPage" : "musicPage");
                            Check(page.ScrollableWidth == 0, "single vertical scrolling at " + dimensions + " display=" + display);
                            foreach (var control in Descendants(page).OfType<Control>().Where(item => item.IsVisible
                                && (item is Button || item is TextBox || item is ComboBox || item is Slider)))
                            {
                                var origin = control.TransformToAncestor(page).Transform(new Point());
                                if (origin.X < -1 || origin.X + control.ActualWidth > page.ActualWidth + 1)
                                    throw new InvalidOperationException("Horizontal clipping: " + control.GetType().Name + " " + AutomationProperties.GetName(control));
                            }
                        }
                    }
                    Check(Directory.GetFiles(Path.Combine(directory, "lyrics")).Length == 0, "UI fixtures do not replace a persisted lyric selection");
                    await (Task)Invoke(window, "DisconnectAsync");
                    Check(Field<TextBlock>(window, "playbackConnectionStatus").Text == "未连接"
                        && Field<ProgressBar>(window, "playbackProgress").Visibility == Visibility.Hidden,
                        "disconnect updates the playback badge and hides stale progress");
                    Invoke(window, "SelectSettingsPage", true);
                    Invoke(window, "Render");
                    Check(Field<TextBlock>(window, "previewLabel").Text.StartsWith("样式示例")
                        && active.Text == "音乐在手机，歌词在桌面", "missing lyrics show explicitly labeled appearance examples");
                    Console.WriteLine("RESULT UI: " + checks + " checks passed; screenshots use deterministic lyric fixtures.");
                }
                catch (Exception error) { failure = error; Console.WriteLine(error.ToString()); }
                finally { window.ExitApplication(); }
            };
            window.Loaded += delegate { timer.Start(); };
            try { application.Run(window); }
            finally { Directory.Delete(directory, true); }
            if (failure != null) throw failure;
        }
    }
}
