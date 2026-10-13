using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace LyricConnector
{
    public sealed class SongQuery
    {
        private static readonly Regex noise = new Regex(@"KTV|字幕|歌詞|歌词|lyrics?|official|music\s*video|\bMV\b|\bHD\b|\b4K\b|1080p|全新|cover|翻唱|原唱|\bOST\b|伴奏|\blive\b|remix", RegexOptions.IgnoreCase);
        private static readonly Regex annotations = new Regex(@"\[[^\[\]]*\]|【[^【】]*】|\([^()]*\)|（[^（）]*）");
        public string Title { get; private set; }
        public string Artist { get; private set; }
        public bool Inferred { get; private set; }

        private static string Clean(string value)
        {
            value = annotations.Replace(value, match => noise.IsMatch(match.Value) ? "" : match.Value);
            value = Regex.Replace(value, @"\s+(?:lyrics?(?:\s+video)?|official\s+(?:audio|video)|歌詞版|歌词版|字幕版)\s*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(value, @"\s+", " ").Trim(' ', '-', '–', '—', '《', '》', '【', '】', '"', '「', '」');
        }

        public static SongQuery Infer(Track track)
        {
            var title = track.Title;
            var artist = track.Artist;
            var original = Regex.Match(title, @"(?:原唱(?:者)?|original\s+artist|歌手)\s*[:：]\s*([^|｜丨\[\]【】()（）]+)", RegexOptions.IgnoreCase);
            if (original.Success) artist = original.Groups[1].Value.Trim();
            var quoted = Regex.Match(title, @"《([^《》]+)》");
            if (quoted.Success)
            {
                if (!original.Success)
                {
                    var prefix = title.Substring(0, quoted.Index).Trim(' ', '-', '–', '—', ':', '：');
                    if (prefix.Length > 0 && prefix.Length <= 40 && !noise.IsMatch(prefix)) artist = prefix;
                }
                title = quoted.Groups[1].Value;
            }
            else
            {
                var segments = Regex.Split(title, @"[|｜丨]").Select(Clean).Where(part => part.Length > 0).ToArray();
                if (segments.Length > 1)
                {
                    var principal = segments.FirstOrDefault(part => !noise.IsMatch(part)
                        && !String.Equals(part, track.Artist, StringComparison.OrdinalIgnoreCase));
                    title = principal ?? segments[0];
                }
                title = Clean(title);
                var pair = Regex.Split(title, @"\s+[-–—]\s+", RegexOptions.None);
                if (pair.Length == 2 && (noise.IsMatch(track.Title) || segments.Length > 1
                    || String.Equals(pair[0], track.Artist, StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(track.Artist, @"频道|頻道|channel|\bTV\b|字幕|歌詞|歌词", RegexOptions.IgnoreCase)))
                {
                    var cover = Regex.Match(pair[1], @"^(.+?)\s+cover\b", RegexOptions.IgnoreCase);
                    if (cover.Success)
                    {
                        title = pair[0];
                        if (!original.Success) artist = cover.Groups[1].Value.Trim();
                    }
                    else if (!noise.IsMatch(pair[0]))
                    {
                        if (!original.Success) artist = pair[0].Trim();
                        title = pair[1];
                    }
                }
                // Pinyin / English video titles sometimes carry the actual Chinese name in parentheses.
                var alias = Regex.Match(title, @"[（(]([^()（）]*[\u4e00-\u9fff][^()（）]*)[）)]");
                if (alias.Success && !noise.IsMatch(alias.Groups[1].Value)
                    && !Regex.IsMatch(title.Substring(0, alias.Index), @"[\u4e00-\u9fff]")) title = alias.Groups[1].Value;
                var bracket = Regex.Match(title, @"^【([^【】]+)】$");
                if (bracket.Success && !noise.IsMatch(bracket.Groups[1].Value)) title = bracket.Groups[1].Value;
            }
            title = Clean(title);
            // An uncertain extraction never erases the phone's original title.
            if (title.Length == 0) title = track.Title;
            return new SongQuery { Title = title, Artist = artist,
                Inferred = title != track.Title || artist != track.Artist };
        }
    }
}
