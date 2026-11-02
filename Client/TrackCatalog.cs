using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace HideoutRadio;

internal sealed class Track
{
    public string id = "", name = "", artist = "", album = "", cover = "", url = "";
    public double duration;
}
internal static class TrackCatalog
{
    public static Track[] Read(string folder, string artwork)
    {
        Directory.CreateDirectory(artwork);
        var formats = new[] { ".mp3", ".ogg", ".wav", ".m4a", ".aac", ".flac", ".opus", ".webm" };
        return Directory.EnumerateFiles(folder).Where(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0 && formats.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Take(1000).Select(f => ReadOne(f, artwork)).ToArray();
    }
    internal static Track ReadOne(string file, string artwork)
    {
        var track = new Track { id = Path.GetFileName(file), name = Path.GetFileNameWithoutExtension(file), url = "Music/" + Uri.EscapeDataString(Path.GetFileName(file)) };
        byte[]? cover = null;
        try
        {
            using var tag = TagLib.File.Create(file, TagLib.ReadStyle.None);
            if (!string.IsNullOrWhiteSpace(tag.Tag.Title)) track.name = tag.Tag.Title;
            track.artist = string.Join(", ", tag.Tag.Performers ?? new string[0]); track.album = tag.Tag.Album ?? "";
            track.duration = tag.Properties?.Duration.TotalSeconds ?? 0;
            var picture = tag.Tag.Pictures.FirstOrDefault(x => x.Type == TagLib.PictureType.FrontCover) ?? tag.Tag.Pictures.FirstOrDefault();
            if (picture != null && picture.Data.Count <= 2 * 1024 * 1024) cover = picture.Data.Data;
        }
        catch { /* Files without readable tags still play using their filename. */ }
        if (cover == null)
        {
            foreach (var candidate in new[] { Path.ChangeExtension(file, ".jpg"), Path.ChangeExtension(file, ".png"), Path.Combine(Path.GetDirectoryName(file)!, "cover.jpg"), Path.Combine(Path.GetDirectoryName(file)!, "cover.png") })
            {
                try { if (File.Exists(candidate) && new FileInfo(candidate).Length <= 2 * 1024 * 1024 && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0) { cover = File.ReadAllBytes(candidate); break; } } catch { }
            }
        }
        if (cover != null && cover.Length >= 8)
        {
            string ext = cover[0] == 0xff && cover[1] == 0xd8 ? ".jpg" : cover[0] == 137 && cover[1] == 80 && cover[2] == 78 && cover[3] == 71 ? ".png" : "";
            if (ext.Length > 0)
            {
                using var hash = SHA256.Create();
                string name = BitConverter.ToString(hash.ComputeHash(cover)).Replace("-", "").ToLowerInvariant() + ext;
                string cached = Path.Combine(artwork, name);
                if (!File.Exists(cached)) File.WriteAllBytes(cached, cover);
                track.cover = "Artwork/" + name;
            }
        }
        return track;
    }
}
