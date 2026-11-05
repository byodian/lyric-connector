using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricConnector
{
    internal sealed class LyricTextView : StackPanel
    {
        private readonly TextBlock previous;
        private readonly TextBlock current;
        private readonly TextBlock next;
        private readonly GradientStop fillStart;
        private readonly GradientStop fillEnd;
        private readonly GradientStop baseStart;
        private readonly GradientStop baseEnd;
        private Color fillColor;
        private Color baseColor;
        private double progress = 1;

        public LyricTextView()
        {
            VerticalAlignment = VerticalAlignment.Center;
            fillStart = new GradientStop();
            fillEnd = new GradientStop();
            baseStart = new GradientStop();
            baseEnd = new GradientStop();
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(fillStart);
            brush.GradientStops.Add(fillEnd);
            brush.GradientStops.Add(baseStart);
            brush.GradientStops.Add(baseEnd);
            previous = Line();
            previous.Opacity = 0.55;
            current = Line();
            current.FontWeight = FontWeights.Medium;
            current.Foreground = brush;
            current.Margin = new Thickness(0, 1, 0, 1);
            next = Line();
            next.Margin = new Thickness(0, 0, 0, 1);
            Children.Add(previous);
            Children.Add(current);
            Children.Add(next);
        }

        private static TextBlock Line()
        {
            return new TextBlock { TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        public void SetText(string before, string active, string after)
        {
            previous.Text = String.IsNullOrEmpty(before) ? "\u00A0" : before;
            current.Text = String.IsNullOrEmpty(active) ? "\u00A0" : active;
            next.Text = String.IsNullOrEmpty(after) ? "\u00A0" : after;
        }

        public void SetKaraokeProgress(double value)
        {
            progress = Math.Max(0, Math.Min(1, value));
            fillStart.Color = fillEnd.Color = progress <= 0 ? baseColor : fillColor;
            baseStart.Color = baseEnd.Color = progress >= 1 ? fillColor : baseColor;
            fillStart.Offset = 0;
            fillEnd.Offset = baseStart.Offset = progress;
            baseEnd.Offset = 1;
        }

        public void Apply(DesktopSettings settings)
        {
            previous.FontFamily = current.FontFamily = next.FontFamily = new FontFamily(settings.Font);
            current.FontSize = settings.FontSize;
            previous.FontSize = next.FontSize = Math.Max(12, settings.FontSize * 0.72);
            fillColor = (Color)ColorConverter.ConvertFromString(settings.CurrentLineColor);
            baseColor = (Color)ColorConverter.ConvertFromString(settings.NextLineColor);
            previous.Foreground = next.Foreground = new SolidColorBrush(baseColor);
            SetKaraokeProgress(progress);
        }
    }
}
