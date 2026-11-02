using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace HideoutRadio;

internal sealed class YouTubeAudio
{
    private const long MaxBytes = 150L * 1024 * 1024;
    private static readonly SemaphoreSlim CacheGate = new(1);
    private readonly string _cache;
    public YouTubeAudio(string cache) => _cache = cache;
    public static bool VideoId(string value) => Regex.IsMatch(value ?? "", "^[A-Za-z0-9_-]{11}$");
    public static string Canonical(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) throw new ArgumentException("Use a valid HTTPS YouTube video or playlist link.");
        var hosts = new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtube-nocookie.com" };
        if (!hosts.Contains(uri.Host)) throw new ArgumentException("Only YouTube links are supported.");
        var args = uri.Query.TrimStart('?').Split('&').Select(x => x.Split(new[] { '=' }, 2)).Where(x => x.Length == 2).GroupBy(x => x[0]).ToDictionary(x => x.Key, x => Uri.UnescapeDataString(x.Last()[1]));
        if (args.TryGetValue("list", out var list) && Regex.IsMatch(list, "^[A-Za-z0-9_-]{2,128}$")) return "https://www.youtube.com/playlist?list=" + list;
        string id = args.TryGetValue("v", out var video) ? video : uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/').Split('/')[0] : uri.AbsolutePath.Split('/').Last();
        if (!VideoId(id)) throw new ArgumentException("The YouTube link has no valid video or playlist ID.");
        return "https://www.youtube.com/watch?v=" + id;
    }
    public async Task<(Track[] Tracks, string? Title)> List(string url, CancellationToken token, bool fetchTitle = false)
    {
        string canonical = Canonical(url);
        using var client = new YoutubeClient();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var tracks = new List<Track>();
        string? title = null;
        if (canonical.Contains("/playlist?"))
        {
            if (fetchTitle)
            {
                try { title = (await client.Playlists.GetAsync(canonical, deadline.Token).ConfigureAwait(false)).Title; }
                catch (Exception) when (!deadline.IsCancellationRequested) { /* Track loading can still succeed. */ }
            }
            await foreach (var video in client.Playlists.GetVideosAsync(canonical, deadline.Token).ConfigureAwait(false))
            {
                tracks.Add(MakeTrack(video.Id.Value, video.Title, video.Author.ChannelTitle, video.Duration));
                if (tracks.Count == 500) break;
            }
        }
        else
        {
            var video = await client.Videos.GetAsync(canonical, deadline.Token).ConfigureAwait(false);
            tracks.Add(MakeTrack(video.Id.Value, video.Title, video.Author.ChannelTitle, video.Duration));
        }
        return (tracks.ToArray(), title ?? (tracks.Count == 1 && !canonical.Contains("/playlist?") ? tracks[0].name : null));
    }

    private static Track MakeTrack(string id, string title, string artist, TimeSpan? duration) => new Track
    {
        id = id, name = title, artist = artist,
        cover = "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg",
        duration = duration?.TotalSeconds ?? 0
    };

    public async Task<string> Resolve(string id, CancellationToken token, long limit = 100_000_000, string? protectedName = null)
    {
        if (!VideoId(id)) throw new ArgumentException("Invalid YouTube track.");
        await CacheGate.WaitAsync(token).ConfigureAwait(false);
        try { return await ResolveLocked(id, token, limit, protectedName).ConfigureAwait(false); }
        finally { CacheGate.Release(); }
    }

    private async Task<string> ResolveLocked(string id, CancellationToken token, long limit, string? protectedName)
    {
        Directory.CreateDirectory(_cache);
        if ((File.GetAttributes(_cache) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The YouTube cache folder cannot be a link.");
        foreach (string extension in new[] { ".m4a", ".webm", ".opus" })
        {
            string existing = Path.Combine(_cache, id + extension);
            if (File.Exists(existing) && new FileInfo(existing).Length > 0)
            {
                File.SetLastWriteTimeUtc(existing, DateTime.UtcNow);
                TrimCache(_cache, limit, protectedName, Path.GetFileName(existing));
                return "YouTubeCache/" + id + extension;
            }
        }

        using var client = new YoutubeClient();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var manifest = await client.Videos.Streams.GetManifestAsync(id, deadline.Token).ConfigureAwait(false);
        var audio = manifest.GetAudioOnlyStreams()
            .Where(s => (s.Container == Container.Mp4 || s.Container == Container.WebM) && s.Size.Bytes <= Math.Min(MaxBytes, limit))
            .OrderBy(s => s.Bitrate.BitsPerSecond < 64_000)
            .ThenBy(s => s.Size.Bytes)
            .FirstOrDefault()
            ?? throw new IOException("No supported audio stream is available for this track.");

        string name = id + (audio.Container == Container.Mp4 ? ".m4a" : ".webm");
        string target = Path.Combine(_cache, name);
        if (!TrimCache(_cache, limit - audio.Size.Bytes, protectedName))
            throw new IOException("There is not enough room in the YouTube cache. Increase its limit in F12.");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await client.Videos.Streams.DownloadAsync(audio, temporary, cancellationToken: deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            var size = new FileInfo(temporary).Length;
            if (size == 0 || size > Math.Min(MaxBytes, limit) || !TrimCache(_cache, limit - size, protectedName)) throw new IOException("Invalid audio download size.");
            try { File.Move(temporary, target); }
            catch (IOException) when (File.Exists(target) && new FileInfo(target).Length > 0) { }
            return "YouTubeCache/" + name;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static FileInfo[] CachedTracks(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            return Array.Empty<FileInfo>();
        return directory.EnumerateFiles()
            .Where(file => VideoId(Path.GetFileNameWithoutExtension(file.Name)) &&
                (file.Extension == ".m4a" || file.Extension == ".webm" || file.Extension == ".opus") &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
    }

    internal static bool TrimCache(string path, long budget, string? protectedName, string? keep = null)
    {
        var files = CachedTracks(path);
        long used = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (used <= budget) break;
            if (file.Name == protectedName || file.Name == keep) continue;
            long size = file.Length;
            try { file.Delete(); used -= size; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return used <= budget;
    }
}




