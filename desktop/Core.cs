using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LyricConnector
{
    public sealed class Track
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public double Duration { get; set; }
        public string Key
        {
            get { return String.Join("\n", Title, Artist, Album, Math.Round(Duration).ToString(CultureInfo.InvariantCulture)); }
        }
    }

    public sealed class Playback
    {
        public int State { get; private set; }
        public double Rate { get; private set; }
        public double Seconds { get; private set; }
        public long Timestamp { get; private set; }

        public static Playback Parse(string value, long timestamp)
        {
            var parts = value.Split(',');
            int state;
            double rate;
            double seconds;
            if (parts.Length != 3 || !Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out state)
                || !Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                || !Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                throw new FormatException("手机未提供有效的播放状态、速率或进度：" + value);
            var result = new Playback
            {
                State = state,
                Rate = rate,
                Seconds = seconds,
                Timestamp = timestamp
            };
            if (result.State < 0 || result.State > 3 || Double.IsNaN(result.Rate) || Double.IsInfinity(result.Rate)
                || Double.IsNaN(result.Seconds) || Double.IsInfinity(result.Seconds) || result.Seconds < 0)
                throw new FormatException("手机返回了无效的播放状态。");
            return result;
        }
    }

    public enum UpdateKind { Transition, Snapshot, Playback, Disconnected }

    public sealed class SourceUpdate
    {
        public UpdateKind Kind { get; set; }
        public long Revision { get; set; }
        public Track Track { get; set; }
        public Playback Playback { get; set; }
        public string Player { get; set; }
        public string Message { get; set; }
    }

    public sealed class PlaybackModel
    {
        private readonly double frequency;
        private Playback anchor;
        public long Revision { get; private set; }
        public Track Track { get; private set; }
        public string Player { get; private set; }
        public string Status { get; private set; }
        public bool Ready { get { return Track != null && anchor != null; } }
        public bool Paused { get { return Ready && anchor.State == 0; } }

        public PlaybackModel(double frequency)
        {
            this.frequency = frequency;
            Status = "等待连接 iPhone";
            Player = "";
        }

        public bool Accept(SourceUpdate update)
        {
            if (update.Revision < Revision) return false;
            var previousKey = Track == null ? "" : Track.Key;
            Revision = update.Revision;
            if (update.Kind == UpdateKind.Transition || update.Kind == UpdateKind.Disconnected)
            {
                Track = null;
                anchor = null;
                Status = update.Message;
                return previousKey.Length > 0;
            }
            if (update.Kind == UpdateKind.Snapshot)
            {
                Track = update.Track;
                Player = update.Player;
                anchor = update.Playback;
            }
            else if (update.Kind == UpdateKind.Playback && Ready)
            {
                if (update.Playback.Timestamp < anchor.Timestamp) return false;
                anchor = update.Playback;
            }
            Status = Ready ? (Paused ? "已暂停" : "正在播放") : "正在切换歌曲";
            return Track != null && previousKey != Track.Key;
        }

        public double Position(long timestamp)
        {
            if (!Ready) throw new InvalidOperationException("歌曲位置尚未就绪。");
            var elapsed = anchor.State == 0 ? 0 : Math.Max(0, timestamp - anchor.Timestamp) / frequency * anchor.Rate;
            return Math.Max(0, Math.Min(Track.Duration, anchor.Seconds + elapsed));
        }
    }

    public sealed class LyricLine
    {
        public double Seconds { get; set; }
        public string Text { get; set; }
    }

    public sealed class Lyrics
    {
        private static readonly Regex timestamp = new Regex(@"\[(\d+):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
        private static readonly Regex offset = new Regex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public List<LyricLine> Lines { get; private set; }
        public string PlainText { get; private set; }
        public double FileOffset { get; private set; }
        public bool Synced { get { return Lines.Count > 0; } }

        public static Lyrics Parse(string text)
        {
            var rows = new List<LyricLine>();
            var fileOffset = 0.0;
            foreach (var row in text.Replace("\r", "").Split('\n'))
            {
                var adjustment = offset.Match(row);
                if (adjustment.Success) fileOffset = Double.Parse(adjustment.Groups[1].Value, CultureInfo.InvariantCulture) / 1000;
                var stamps = timestamp.Matches(row);
                if (stamps.Count == 0) continue;
                var body = timestamp.Replace(row, "").Trim();
                foreach (Match stamp in stamps)
                {
                    var seconds = Int32.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (seconds >= 60) continue;
                    var fraction = stamp.Groups[3].Success
                        ? Double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                    rows.Add(new LyricLine { Seconds = Int32.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + seconds + fraction, Text = body });
                }
            }
            return new Lyrics
            {
                Lines = rows.GroupBy(line => line.Seconds).OrderBy(group => group.Key)
                    .Select(group => new LyricLine { Seconds = group.Key, Text = String.Join(" / ", group.Select(line => line.Text).Distinct()) }).ToList(),
                PlainText = text.Trim(),
                FileOffset = fileOffset
            };
        }

        public int IndexAt(double position, double userOffset)
        {
            var target = position + userOffset + FileOffset;
            var low = 0;
            var high = Lines.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (Lines[middle].Seconds <= target) low = middle + 1;
                else high = middle;
            }
            return low - 1;
        }
    }
}
