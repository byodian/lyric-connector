using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace LyricConnector
{
    public static class Tests
    {
        private static int checks;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
        private static SourceUpdate Snapshot(long revision, Track track, string value, long tick)
        {
            return new SourceUpdate { Kind = UpdateKind.Snapshot, Revision = revision, Track = track, Player = "YouTube Music", Playback = Playback.Parse(value, tick) };
        }
        public static void CheckProvider()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LyricConnector-Network-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new LyricsStore(directory))
                {
                    var records = store.SearchAsync("夜的第七章", "Jay Chou", CancellationToken.None).GetAwaiter().GetResult();
                    var timed = records.Where(record => record.Document().Synced).ToList();
                    Check(records.Count > 0 && timed.Count > 0, "native Windows HTTP receives timed lyric candidates");
                    Console.WriteLine("PROVIDER: candidates=" + records.Count + " timed=" + timed.Count + " firstLines=" + timed[0].Document().Lines.Count);
                    var video = new Track { Title = "止戰之殤 | KTV 動態字幕 | 全新 輕搖滾 Cover | AI Voice Cover by Capt. Lilia Iwasko | 原唱：周杰倫",
                        Artist = "Capt. Lilia Iwasko", Album = "", Duration = 219 };
                    var result = store.FindAsync(video, CancellationToken.None).GetAwaiter().GetResult();
                    Check(result.Candidates.Count > 0 && result.Selected == null, "mixed video title finds ranked candidates without treating an unverified cover as exact");
                    Console.WriteLine("INFERRED_PROVIDER: title=" + SongQuery.Infer(video).Title + " candidates=" + result.Candidates.Count);
                    var translatedTitle = new Track { Title = "Definitely Not a LRCLIB Title", Artist = "Jay Chou", Album = "十一月的萧邦", Duration = 0 };
                    var expanded = store.FindAsync(translatedTitle, CancellationToken.None).GetAwaiter().GetResult();
                    Check(expanded.ExpandedSearch && expanded.Candidates.Any(match => match.Record.Document().Synced),
                        "unmatched title expands to album or artist candidates without auto-selection");
                    Console.WriteLine("EXPANDED_PROVIDER: candidates=" + expanded.Candidates.Count);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        public static void Run(string fixture)
        {
            checks = 0;
            var track = new Track { Title = "A", Artist = "Artist", Album = "", Duration = 300 };
            var model = new PlaybackModel(1000);
            model.Accept(Snapshot(1, track, "1,1.0,20.0", 1000));
            Check(Math.Abs(model.Position(3500) - 22.5) < 0.00001, "monotonic playback interpolation");
            model.Accept(new SourceUpdate { Kind = UpdateKind.Playback, Revision = 1, Playback = Playback.Parse("0,1.0,22.5", 3500) });
            Check(model.Paused && model.Position(90000) == 22.5, "pause freezes position");
            model.Accept(new SourceUpdate { Kind = UpdateKind.Playback, Revision = 1, Playback = Playback.Parse("1,1.0,90", 4000) });
            Check(model.Position(5000) == 91, "seek replaces anchor immediately");
            model.Accept(new SourceUpdate { Kind = UpdateKind.Playback, Revision = 1, Playback = Playback.Parse("1,1.0,10", 3000) });
            Check(model.Position(5000) == 91, "older playback packet ignored");
            model.Accept(new SourceUpdate { Kind = UpdateKind.Transition, Revision = 2, Message = "changing" });
            Check(!model.Ready && model.Track == null, "metadata change clears old song and position");
            model.Accept(Snapshot(1, track, "1,1.0,299", 5000));
            Check(!model.Ready, "previous revision snapshot cannot revive old song");
            var second = new Track { Title = "B", Artist = "Artist", Album = "", Duration = 120 };
            model.Accept(Snapshot(2, second, "1,1.0,0", 6000));
            Check(model.Ready && model.Position(7000) == 1 && model.Track.Title == "B", "new song has independent anchor");
            var durationless = new Track { Title = "C", Artist = "Artist", Album = "", Duration = 0 };
            model.Accept(Snapshot(3, durationless, "1,1.0,20", 8000));
            Check(model.Ready && model.Position(9500) == 21.5, "playback advances when total duration is unavailable");
            Check(AmsSource.ParseDuration("") == 0 && AmsSource.ParseDuration("180.5") == 180.5,
                "missing track duration is optional while valid duration is retained");
            Check(AmsSource.IsPositionWithinDuration(0, 20) && !AmsSource.IsPositionWithinDuration(30, 31),
                "position validation does not reject a track with unknown duration");
            var invalidDurationRejected = false;
            try { AmsSource.ParseDuration("Infinity"); }
            catch (FormatException) { invalidDurationRejected = true; }
            Check(invalidDurationRejected, "invalid track duration is rejected");
            var missingLyricsDuration = new JavaScriptSerializer().Deserialize<LyricRecord>(
                "{\"id\":42,\"trackName\":\"中文曲名\",\"artistName\":\"歌手\",\"duration\":null,\"syncedLyrics\":\"[00:01.00]歌词\"}");
            Check(!missingLyricsDuration.duration.HasValue && missingLyricsDuration.ToString().Contains("中文曲名"),
                "LRCLIB candidate with an empty duration remains readable");
            model.Accept(new SourceUpdate { Kind = UpdateKind.Disconnected, Revision = 3, Message = "disconnected" });
            Check(!model.Ready, "disconnect clears timeline");
            var playerUpdate = new Packet { Entity = 0, Attribute = 0, Value = "Apple Music" };
            Check(AmsSource.ChangesPlayer(playerUpdate, "YouTube Music"), "player change triggers a fresh media snapshot");
            Check(!AmsSource.ChangesPlayer(playerUpdate, "Apple Music"), "same player name does not restart the media snapshot");
            var lines = Lyrics.Parse("[ar:A]\n[offset:250]\n[00:02.50][00:04.5]same\n[00:04.500]translation\n[00:08]last");
            Check(lines.Lines.Count == 3 && lines.Lines[1].Text == "same / translation", "multiple stamps and duplicate-time translation");
            Check(lines.IndexAt(2, 0) == -1 && lines.IndexAt(2, 0.25) == 0, "file and user offsets advance lyrics");
            Check(Math.Abs(lines.ProgressAt(0, 3.25, 10, 0) - 0.5) < 0.00001,
                "karaoke fill advances across a lyric line between timestamps");
            Check(lines.ProgressAt(1, 7.75, 10, 0) == 1 && lines.ProgressAt(2, 9, 0, 0) == 1,
                "karaoke fill completes at the next line or holds the final line without duration");
            Check(lines.IndexAt(99, 0) == 2 && lines.IndexAt(0, 0) == -1, "lyric boundary lookup");
            Check(!Lyrics.Parse("plain words").Synced, "plain lyrics have no fabricated timeline");
            Check(new LyricRecord { syncedLyrics = "", plainLyrics = "plain words" }.Document().PlainText == "plain words", "empty timed lyrics retain provider plain text");
            var matchTrack = new Track { Title = "夜的第七章", Artist = "Jay Chou", Album = "十一月的萧邦", Duration = 218 };
            var correctPlain = new LyricRecord { trackName = "夜的第七章", artistName = "Jay Chou", albumName = "十一月的萧邦", duration = 218, plainLyrics = "正确歌词" };
            var wrongSynced = new LyricRecord { trackName = "夜的第七章", artistName = "Other Artist", albumName = "Other Album", duration = 250, syncedLyrics = "[00:01]错版歌词" };
            var ranked = LyricMatcher.Rank(matchTrack, matchTrack.Title, matchTrack.Artist, new[] { wrongSynced, correctPlain });
            Check(ranked[0].Record == correctPlain && ranked[0].Score > ranked[1].Score,
                "candidate ranking prefers matching song identity over an unrelated synced version");
            var titleVariant = new LyricRecord { trackName = "Love Story", artistName = "Taylor Swift", albumName = "Fearless", duration = 235, syncedLyrics = "[00:01]lyrics" };
            var variantTrack = new Track { Title = "Love Story (Lyric Video)", Artist = "Taylor Swift", Album = "Fearless", Duration = 235 };
            Check(LyricMatcher.Rank(variantTrack, variantTrack.Title, variantTrack.Artist, new[] { titleVariant })[0].Score == 100,
                "candidate score ignores punctuation and generic video labels");
            var durationlessTrack = new Track { Title = "夜的第七章", Artist = "Jay Chou", Album = "", Duration = 0 };
            var durationlessMatch = new LyricRecord { trackName = "夜的第七章", artistName = "Jay Chou", duration = null, syncedLyrics = "[00:01]lyrics" };
            Check(LyricMatcher.Rank(durationlessTrack, durationlessTrack.Title, durationlessTrack.Artist, new[] { durationlessMatch })[0].Score == 100,
                "missing playback and candidate duration do not lower a metadata match");
            var sparseTrack = new Track { Title = "夜的第七章", Artist = "Jay Chou", Album = "十一月的萧邦", Duration = 218 };
            var sparseCandidate = new LyricRecord { trackName = "夜的第七章", syncedLyrics = "[00:01]歌词" };
            var sparseMatch = LyricMatcher.Rank(sparseTrack, sparseTrack.Title, sparseTrack.Artist, new[] { sparseCandidate })[0];
            Check(sparseMatch.Score == 100 && sparseMatch.ConfidenceScore < 80,
                "missing candidate metadata keeps title similarity but lowers confidence");
            var defaults = DesktopSettings.Defaults();
            var currentColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(defaults.CurrentLineColor);
            var nextColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(defaults.NextLineColor);
            Check(currentColor != nextColor, "current and next lyric lines support distinct configurable colors");
            Check(!Lyrics.Parse("[00:99]invalid").Synced, "invalid seconds rejected");
            foreach (var invalid in new[] { "0,,", "1,1.0,", "1,1.0,nan", "1,NaN,10", "1,1.0,Infinity", "1,1,-1" })
            {
                var rejected = false;
                try { Playback.Parse(invalid, 0); }
                catch (FormatException) { rejected = true; }
                Check(rejected, "missing or invalid progress rejected: " + invalid);
            }
            Cache();
            Names();
            Replay(fixture);
            Console.WriteLine("RESULT: " + checks + " checks passed.");
        }

        private static void Names()
        {
            var examples = new[]
            {
                new[] { "止戰之殤 | KTV 動態字幕 | 全新 輕搖滾 Cover | AI Voice Cover by Capt. Lilia Iwasko | 原唱：周杰倫", "Capt. Lilia Iwasko", "止戰之殤", "周杰倫" },
                new[] { "周二珂 - 《走在冷风中》", "SolarMotion20", "走在冷风中", "周二珂" },
                new[] { "[Meteor Garden OST] Ni Yao De Ai (你要的爱) - Penny Tai Cover in Bohol Island", "DARA TV", "你要的爱", "Penny Tai" },
                new[] { "周杰伦 - 夜的第七章（歌词版） | Official MV", "字幕频道", "夜的第七章", "周杰伦" },
                new[] { "【Official MV】周二珂 - 走在冷风中", "音乐频道", "走在冷风中", "周二珂" },
                new[] { "夜的第七章", "Jay Chou", "夜的第七章", "Jay Chou" },
                new[] { "Love Story", "Taylor Swift", "Love Story", "Taylor Swift" },
                new[] { "Love - Hate", "Some Band", "Love - Hate", "Some Band" },
                new[] { "DARA TV | 你要的爱 | Lyric Video", "DARA TV", "你要的爱", "DARA TV" },
                new[] { "Taylor Swift - Love Story", "Taylor Swift", "Love Story", "Taylor Swift" }
            };
            foreach (var row in examples)
            {
                var query = SongQuery.Infer(new Track { Title = row[0], Artist = row[1], Album = "", Duration = 200 });
                Check(query.Title == row[2] && query.Artist == row[3], "song query inference: " + row[2]);
            }
        }

        private static void Cache()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LyricConnector-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var track = new Track { Title = "中文歌曲", Artist = "歌手", Album = "版本 A", Duration = 211.081 };
                var file = Path.Combine(directory, "local.lrc");
                File.WriteAllText(file, "[00:01.50]中文歌词\n[00:03]下一行", Encoding.UTF8);
                using (var store = new LyricsStore(Path.Combine(directory, "cache")))
                {
                    store.Save(track, store.Import(track, file));
                    var restored = store.FindAsync(track, CancellationToken.None).GetAwaiter().GetResult();
                    Check(restored.Selected.Source == "本地歌词" && restored.Selected.Document().Lines[0].Text == "中文歌词", "local UTF-8 LRC import and cache round trip");
                    track.Duration = 211;
                    Check(store.FindAsync(track, CancellationToken.None).GetAwaiter().GetResult().Selected != null, "fractional duration notification retains mapping");
                    Check(Directory.GetFiles(Path.Combine(directory, "cache")).Length == 1, "cache signature stable for one recording");
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void Replay(string fixture)
        {
            var capture = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(fixture);
            var events = (System.Collections.ArrayList)capture["events"];
            Check(events.Count == 42, "real phone capture fixture has 42 events");
            var model = new PlaybackModel(1000);
            var track = new Track { Title = "initial", Artist = "", Album = "", Duration = 600 };
            long tick = 0;
            long revision = 1;
            int cutovers = 0;
            int pauses = 0;
            model.Accept(Snapshot(revision, track, "1,1,0", tick));
            foreach (Dictionary<string, object> packet in events)
            {
                tick += 1000;
                var entity = Convert.ToInt32(packet["entityId"]);
                var attribute = Convert.ToInt32(packet["attributeId"]);
                var value = (string)packet["value"];
                if (entity == 2 && attribute == 2 && track.Title != value)
                {
                    revision++;
                    model.Accept(new SourceUpdate { Kind = UpdateKind.Transition, Revision = revision, Message = "changing" });
                    cutovers++;
                    Check(!model.Ready, "capture title change invalidates position #" + cutovers);
                    track.Title = value;
                }
                if (entity == 0 && attribute == 1)
                {
                    var playback = Playback.Parse(value, tick);
                    if (playback.State == 0) pauses++;
                    model.Accept(new SourceUpdate { Kind = UpdateKind.Playback, Revision = revision, Playback = playback });
                    if (!model.Ready) Check(model.Track == null, "transition playback cannot resurrect stale track");
                }
            }
            Check(cutovers >= 6 && pauses > 0, "real capture exercises title cutovers and pauses");
        }
    }
}
