using System;
using System.IO;

namespace LyricConnector
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var deviceName = "iPhone";
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (String.Equals(args[index], "--device-name", StringComparison.OrdinalIgnoreCase))
                {
                    deviceName = args[index + 1];
                    break;
                }
            }

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "lyric-connector.ico");
            Entry.Run(deviceName, 0, String.Empty, iconPath);
        }
    }
}
