using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        public double duration { get; set; }
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
        public List<LyricRecord> Candidates { get; set; }
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
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    throw new HttpRequestException("歌词服务暂时不可用，请稍后重新搜索或导入 LRC。");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public async Task<LyricResult> FindAsync(Track track, CancellationToken cancellation)
        {
            var cache = CachePath(track);
            if (File.Exists(cache))
                return new LyricResult { Selected = new JavaScriptSerializer().Deserialize<LyricRecord>(File.ReadAllText(cache, Encoding.UTF8)), Candidates = new List<LyricRecord>() };
            var query = SongQuery.Infer(track);
            if (!query.Inferred && track.Artist.Length > 0)
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
                        && Math.Abs(record.duration - track.Duration) <= 2)
                        return new LyricResult { Selected = record, Candidates = new List<LyricRecord>() };
                }
            }
            var candidates = await SearchAsync(query.Title, query.Artist, cancellation).ConfigureAwait(false);
            return new LyricResult { Candidates = candidates.OrderBy(record => String.IsNullOrEmpty(record.syncedLyrics) ? 1 : 0)
                .ThenBy(record => Math.Abs(record.duration - track.Duration)).ToList() };
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
