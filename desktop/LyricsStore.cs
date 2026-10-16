using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LyricConnector
{
    public sealed class LyricRecord
    {
        public long id { get; set; }
        public string trackName { get; set; }
        public string artistName { get; set; }
        public string albumName { get; set; }
        public double? duration { get; set; }
        public bool instrumental { get; set; }
        public string syncedLyrics { get; set; }
        public string plainLyrics { get; set; }
        public string localText { get; set; }
        public string Source { get { return localText != null ? "本地歌词" : "LRCLIB"; } }
        public Lyrics Document()
        {
            return Lyrics.Parse(localText != null ? localText : (!String.IsNullOrEmpty(syncedLyrics) ? syncedLyrics : (plainLyrics ?? "")));
        }
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}  /  {1}\n{2}  ·  {3:0.0} 秒  ·  {4}",
                trackName, artistName, albumName, duration, !String.IsNullOrEmpty(syncedLyrics) ? "同步歌词" : "纯文本");
        }
    }

    public sealed class LyricResult
    {
        public LyricRecord Selected { get; set; }
        public List<LyricMatch> Candidates { get; set; }
        public bool ExpandedSearch { get; set; }
    }

    public sealed class LyricMatch
    {
        public LyricRecord Record { get; set; }
        public int Score { get; set; }
        public int EvidenceScore { get; set; }
        public int ConfidenceScore { get { return (int)Math.Round(Score * EvidenceScore / 100.0); } }
        public string Confidence { get { return ConfidenceScore >= 80 ? "较高" : ConfidenceScore >= 60 ? "一般" : "较低"; } }
        public override string ToString()
        {
            return Record.ToString() + "\n相似度 " + Score + "% · 置信度 " + ConfidenceScore + "%（" + Confidence + "）";
        }
    }

    public static class LyricMatcher
    {
        private static readonly string[][] versions = {
            new[] { "live", "现场", "演唱会" }, new[] { "remix", "混音" },
            new[] { "acoustic", "原声" }, new[] { "instrumental", "伴奏", "纯音乐" },
            new[] { "demo", "小样" }
        };

        public static List<LyricMatch> Rank(Track track, string title, string artist, IEnumerable<LyricRecord> records)
        {
            return records.Select(record => Match(track, title, artist, record))
                .OrderByDescending(match => match.ConfidenceScore)
                .ThenByDescending(match => match.Score)
                .ThenBy(match => String.IsNullOrWhiteSpace(match.Record.syncedLyrics) ? 1 : 0)
                .ThenBy(match => Math.Abs(match.Record.duration.HasValue && track.Duration > 0
                    ? match.Record.duration.Value - track.Duration : Double.MaxValue))
                .ToList();
        }

        private static LyricMatch Match(Track track, string title, string artist, LyricRecord record)
        {
            var weighted = 0.0;
            var weights = 0.0;
            var expectedWeights = 0.45;
            AddScore(TitleSimilarity(title, record.trackName), 0.45, ref weighted, ref weights);
            if (!String.IsNullOrWhiteSpace(artist))
            {
                expectedWeights += 0.25;
                if (!String.IsNullOrWhiteSpace(record.artistName)) AddScore(Similarity(artist, record.artistName), 0.25, ref weighted, ref weights);
            }
            if (!String.IsNullOrWhiteSpace(track.Album))
            {
                expectedWeights += 0.10;
                if (!String.IsNullOrWhiteSpace(record.albumName)) AddScore(Similarity(track.Album, record.albumName), 0.10, ref weighted, ref weights);
            }
            if (track.Duration > 0)
            {
                expectedWeights += 0.20;
                if (record.duration.HasValue && record.duration.Value > 0)
                    AddScore(DurationSimilarity(track.Duration, record.duration.Value), 0.20, ref weighted, ref weights);
            }

            var expectedVersion = track.Title + " " + track.Album;
            var candidateVersion = record.trackName + " " + record.albumName;
            var penalty = 0;
            foreach (var group in versions)
                if (HasVersion(candidateVersion, group) != HasVersion(expectedVersion, group)) penalty += 12;
            var score = (int)Math.Max(0, Math.Min(100, Math.Round(weighted / weights * 100) - penalty));
            return new LyricMatch { Record = record, Score = score, EvidenceScore = (int)Math.Round(weights / expectedWeights * 100) };
        }

        private static void AddScore(double score, double weight, ref double weighted, ref double weights)
        {
            weighted += score * weight;
            weights += weight;
        }

        private static double TitleSimilarity(string expected, string candidate)
        {
            var best = Similarity(expected, candidate);
            foreach (Match bracket in Regex.Matches(candidate, @"\[([^\[\]]+)\]|【([^【】]+)】"))
            {
                var alias = bracket.Groups[1].Success ? bracket.Groups[1].Value : bracket.Groups[2].Value;
                if (!versions.Any(markers => HasVersion(alias, markers)))
                    best = Math.Max(best, Similarity(expected, alias));
            }
            return best;
        }

        private static bool HasVersion(string value, string[] markers)
        {
            return markers.Any(marker => marker.All(character => character < 128 && Char.IsLetter(character))
                ? Regex.IsMatch(value, @"(?<![\p{L}\p{N}])" + Regex.Escape(marker) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)
                : value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static double DurationSimilarity(double expected, double actual)
        {
            var difference = Math.Abs(expected - actual);
            if (difference <= 2) return 1;
            var tolerance = Math.Max(8, expected * 0.08);
            return Math.Max(0, 1 - (difference - 2) / (tolerance - 2));
        }

        private static double Similarity(string first, string second)
        {
            var left = Normalize(first);
            var right = Normalize(second);
            if (left.Length == 0 || right.Length == 0) return 0;
            if (left == right) return 1;
            var edit = 1.0 - EditDistance(left, right) / (double)Math.Max(left.Length, right.Length);
            var bigrams = Dice(Bigrams(left), Bigrams(right));
            var tokens = Dice(Tokens(first), Tokens(second));
            return Math.Max(edit, Math.Max(bigrams, tokens));
        }

        private static string Normalize(string value)
        {
            value = Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),
                @"official\s+(?:audio|video)|lyrics?\s+video|music\s+video|\b(?:official|lyrics?|audio|video|mv|hd|4k)\b|歌词版|歌詞版|字幕版|官方(?:音频|音訊|视频|影片)", " ", RegexOptions.IgnoreCase);
            var normalized = new StringBuilder();
            foreach (var character in value)
                if (Char.IsLetterOrDigit(character)) normalized.Append(character);
            return normalized.ToString();
        }

        private static HashSet<string> Bigrams(string value)
        {
            var result = new HashSet<string>();
            for (var i = 0; i + 1 < value.Length; i++) result.Add(value.Substring(i, 2));
            if (result.Count == 0) result.Add(value);
            return result;
        }

        private static HashSet<string> Tokens(string value)
        {
            return new HashSet<string>(Regex.Matches(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\p{L}\p{N}]+")
                .Cast<Match>().Select(match => match.Value));
        }

        private static double Dice(HashSet<string> first, HashSet<string> second)
        {
            if (first.Count == 0 || second.Count == 0) return 0;
            var overlap = first.Count(value => second.Contains(value));
            return 2.0 * overlap / (first.Count + second.Count);
        }

        private static int EditDistance(string first, string second)
        {
            var previous = Enumerable.Range(0, second.Length + 1).ToArray();
            var current = new int[second.Length + 1];
            for (var row = 1; row <= first.Length; row++)
            {
                current[0] = row;
                for (var column = 1; column <= second.Length; column++)
                    current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1),
                        previous[column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1));
                var swap = previous;
                previous = current;
                current = swap;
            }
            return previous[second.Length];
        }
    }

    public sealed class LyricsStore : IDisposable
    {
        private readonly string directory;
        private readonly HttpClient http;

        public LyricsStore(string directory)
        {
            this.directory = directory;
            Directory.CreateDirectory(directory);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LyricConnector/0.1");
        }

        private string CachePath(Track track)
        {
            using (var digest = SHA256.Create())
                return Path.Combine(directory, BitConverter.ToString(digest.ComputeHash(Encoding.UTF8.GetBytes(track.Key))).Replace("-", "") + ".json");
        }

        public void Save(Track track, LyricRecord record)
        {
            var path = CachePath(track);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(record), Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        public LyricRecord Import(Track track, string path)
        {
            var record = new LyricRecord { trackName = track.Title, artistName = track.Artist, albumName = track.Album,
                duration = track.Duration, localText = File.ReadAllText(path, Encoding.UTF8) };
            if (record.Document().PlainText.Length == 0) throw new InvalidOperationException("这个歌词文件为空。");
            return record;
        }

        private async Task<string> GetAsync(string path, CancellationToken cancellation)
        {
            using (var response = await http.GetAsync("https://lrclib.net" + path, cancellation).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    throw new HttpRequestException("歌词服务暂时不可用。");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public async Task<LyricResult> FindAsync(Track track, CancellationToken cancellation)
        {
            var cache = CachePath(track);
            if (File.Exists(cache))
                return new LyricResult { Selected = new JavaScriptSerializer().Deserialize<LyricRecord>(File.ReadAllText(cache, Encoding.UTF8)), Candidates = new List<LyricMatch>() };
            var query = SongQuery.Infer(track);
            if (!query.Inferred && track.Artist.Length > 0 && track.Duration > 0)
            {
                var signature = "track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist)
                    + "&duration=" + track.Duration.ToString("0.###", CultureInfo.InvariantCulture);
                if (track.Album.Length > 0) signature += "&album_name=" + Uri.EscapeDataString(track.Album);
                var exact = await GetAsync("/api/get?" + signature, cancellation).ConfigureAwait(false);
                if (exact != null)
                {
                    var record = new JavaScriptSerializer().Deserialize<LyricRecord>(exact);
                    if (String.Equals(record.trackName, track.Title, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(record.artistName, track.Artist, StringComparison.OrdinalIgnoreCase)
                        && record.duration.HasValue && Math.Abs(record.duration.Value - track.Duration) <= 2)
                        return new LyricResult { Selected = record, Candidates = new List<LyricMatch>() };
                }
            }
            var candidates = await SearchAsync(query.Title, query.Artist, cancellation).ConfigureAwait(false);
            var directSyncedMatch = candidates.Any(record => String.Equals(record.trackName, query.Title, StringComparison.OrdinalIgnoreCase)
                && (query.Artist.Length == 0 || String.Equals(record.artistName, query.Artist, StringComparison.OrdinalIgnoreCase))
                && !String.IsNullOrWhiteSpace(record.syncedLyrics));
            var expandedSearch = !directSyncedMatch;
            if (expandedSearch)
            {
                var related = new List<LyricRecord>();
                if (track.Album.Length > 0)
                {
                    var albumResults = await QueryAnyFieldAsync(track.Album, cancellation).ConfigureAwait(false);
                    related.AddRange(albumResults.Where(record => Contains(record.albumName, track.Album)
                        || Contains(record.artistName, query.Artist) || Contains(record.artistName, track.Artist)));
                }
                if (related.Count == 0 && query.Artist.Length > 0)
                {
                    var artistResults = await QueryAnyFieldAsync(query.Artist, cancellation).ConfigureAwait(false);
                    related.AddRange(artistResults.Where(record => Contains(record.artistName, query.Artist)
                        || Contains(record.artistName, track.Artist)));
                }
                candidates.AddRange(related);
            }
            var unique = candidates.GroupBy(CandidateKey).Select(group => group.First());
            return new LyricResult { ExpandedSearch = expandedSearch, Candidates = LyricMatcher.Rank(track, query.Title, query.Artist, unique) };
        }

        public async Task<List<LyricRecord>> SearchAsync(string title, string artist, CancellationToken cancellation)
        {
            var records = await QueryAsync(title, artist, cancellation).ConfigureAwait(false);
            if (records.Count == 0 && artist.Length > 0)
                records = await QueryAsync(title, "", cancellation).ConfigureAwait(false);
            return records;
        }

        private async Task<List<LyricRecord>> QueryAsync(string title, string artist, CancellationToken cancellation)
        {
            var query = "track_name=" + Uri.EscapeDataString(title);
            if (artist.Length > 0) query += "&artist_name=" + Uri.EscapeDataString(artist);
            var response = await GetAsync("/api/search?" + query, cancellation).ConfigureAwait(false);
            if (response == null) return new List<LyricRecord>();
            return new JavaScriptSerializer().Deserialize<List<LyricRecord>>(response);
        }

        private async Task<List<LyricRecord>> QueryAnyFieldAsync(string term, CancellationToken cancellation)
        {
            var response = await GetAsync("/api/search?q=" + Uri.EscapeDataString(term), cancellation).ConfigureAwait(false);
            if (response == null) return new List<LyricRecord>();
            return new JavaScriptSerializer().Deserialize<List<LyricRecord>>(response);
        }

        private static bool Contains(string value, string term)
        {
            return value != null && term.Length > 0 && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CandidateKey(LyricRecord record)
        {
            return record.id > 0 ? "id:" + record.id.ToString(CultureInfo.InvariantCulture)
                : String.Join("\n", record.trackName ?? "", record.artistName ?? "", record.albumName ?? "");
        }

        public void Dispose() { http.Dispose(); }
    }

    public sealed class DesktopSettings
    {
        public string DeviceId { get; set; }
        public string Font { get; set; }
        public double FontSize { get; set; }
        public double BackgroundOpacity { get; set; }
        public double Offset { get; set; }
        public bool Topmost { get; set; }
        public bool ClickThrough { get; set; }
        public bool? ShowLyrics { get; set; }
        public string CurrentLineColor { get; set; }
        public string NextLineColor { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }

        public static DesktopSettings Defaults()
        {
            return new DesktopSettings { DeviceId = "", Font = "Microsoft YaHei UI", FontSize = 28,
                BackgroundOpacity = 0.64, ShowLyrics = true, CurrentLineColor = "#F4D7E7", NextLineColor = "#B0A9C6",
                Topmost = true, Width = 760, Left = -1, Top = -1 };
        }
    }
}
