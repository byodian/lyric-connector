using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LyricConnector
{
    public static class SongGlyph
    {
        private static readonly Geometry musicMark = Geometry.Parse("M 80,59 L 80,136 C 70,127 55,131 51,143 C 47,157 63,164 75,157 C 81,153 86,147 86,139 L 86,86 L 111,81 L 111,119 C 100,111 85,118 84,130 C 83,143 99,148 109,141 C 116,136 117,130 117,123 L 117,63 Z");

        private static void Draw(DrawingContext context)
        {
            var shade = new LinearGradientBrush(Color.FromRgb(45, 38, 85), Color.FromRgb(18, 21, 45), new Point(0, 0), new Point(1, 1));
            context.DrawRoundedRectangle(shade, null, new Rect(9, 9, 238, 238), 58, 58);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(130, 103, 183)), null, new Rect(9, 9, 238, 238), 58, 58);
            context.DrawRoundedRectangle(shade, null, new Rect(13, 13, 230, 230), 53, 53);

            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 190, 214)), null, musicMark);

            var line = new LinearGradientBrush(Color.FromRgb(255, 197, 219), Color.FromRgb(164, 221, 255), new Point(0, 0), new Point(1, 0));
            context.DrawRoundedRectangle(line, null, new Rect(132, 77, 78, 11), 5.5, 5.5);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 222, 209, 242)), null, new Rect(132, 113, 61, 8), 4, 4);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 190, 204, 234)), null, new Rect(132, 145, 70, 8), 4, 4);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(175, 169, 183, 218)), null, new Rect(132, 177, 47, 8), 4, 4);

            var connector = new Pen(new SolidColorBrush(Color.FromRgb(142, 210, 243)), 3);
            connector.StartLineCap = PenLineCap.Round;
            connector.EndLineCap = PenLineCap.Round;
            context.DrawLine(connector, new Point(211, 82), new Point(221, 82));
            context.DrawLine(connector, new Point(194, 117), new Point(222, 117));
            context.DrawLine(connector, new Point(203, 149), new Point(220, 149));
            foreach (var point in new[] { new Point(222, 82), new Point(224, 117), new Point(222, 149) })
            {
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(136, 221, 246)), null, point, 4.8, 4.8);
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(75, 143, 213, 246)), null, point, 8.5, 8.5);
            }
        }

        private static byte[] Png(int size)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(4, 4));
                Draw(context);
                context.Pop();
            }
            var full = new RenderTargetBitmap(1024, 1024, 96, 96, PixelFormats.Pbgra32);
            full.Render(visual);
            var resized = new TransformedBitmap(full, new ScaleTransform(size / 1024.0, size / 1024.0));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            using (var output = new MemoryStream())
            {
                encoder.Save(output);
                return output.ToArray();
            }
        }

        public static void Save(string iconPath, string previewPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (var size in sizes) images.Add(Png(size));
            var baseOffset = 6 + 16 * sizes.Length;
            using (var stream = File.Create(iconPath))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                for (var index = 0; index < sizes.Length; index++)
                {
                    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(images[index].Length); writer.Write(baseOffset);
                    baseOffset += images[index].Length;
                }
                foreach (var image in images) writer.Write(image);
            }
            using (var output = File.Create(previewPath)) output.Write(images[5], 0, images[5].Length);
        }
    }
}
