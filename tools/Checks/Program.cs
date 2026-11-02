using HideoutRadio;using Mono.Cecil;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
if(args.Contains("--bridge"))
{
    var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
    var data=JObject.Parse(Console.In.ReadToEnd());string? request=(string?)data["request"];
    try
    {
        var backend=new YouTubeAudio(Environment.GetEnvironmentVariable("TUSHONKA_TEST_CACHE") ?? Path.Combine(root,"Player","YouTubeCache"));
                if ((string?)data["type"] == "youtube-list")
        {
            var list = await backend.List((string?)data["url"] ?? "", CancellationToken.None, (bool?)data["fetchTitle"] == true);
            Console.WriteLine(JsonConvert.SerializeObject(new { type = "youtube-list", request, tracks = list.Tracks, title = list.Title }));
            return;
        }
        object result=(string?)data["type"] switch {
            "youtube-audio" => new {type="youtube-audio",request,url=await backend.Resolve((string?)data["id"]??"",CancellationToken.None)},
            "ready" or "refresh" => new {type="library",tracks=TrackCatalog.Read(Path.Combine(root,"Player","Music"),Path.Combine(root,"Player","Artwork"))},
            _ => new {type="ack"}
        };
        Console.WriteLine(JsonConvert.SerializeObject(result));
    }
    catch(Exception ex){Console.WriteLine(JsonConvert.SerializeObject(new {type="youtube-error",request,error=ex.Message}));}
    return;
}

int count=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);count++;Console.WriteLine("PASS "+msg);}
Check(PlaybackGate.Context(true,false,"MainMenu","")=="menu","main menu allows radio");
Check(PlaybackGate.Context(true,false,"Hideout","")=="hideout","hideout allows radio");
Check(PlaybackGate.Context(true,false,"Inventory","Hideout")=="hideout","hideout inventory keeps context");
Check(PlaybackGate.Context(true,false,"Traders","MainMenu")=="menu","menu subpages keep context");
foreach(var screen in new[]{"BattleUI","TimeHasCome","FinalCountdown","MatchMakerAccept","ExitStatus","HealthTreatment"})Check(PlaybackGate.Context(true,false,screen,"MainMenu")=="blocked",screen+" pauses playback");
Check(PlaybackGate.Context(true,true,"Inventory","MainMenu")=="blocked","raid world overrides menu-like screens");
Check(PlaybackGate.Context(false,false,"MainMenu","")=="blocked","disabled plugin pauses playback");
Check(PlaybackGate.Context(true,false,"Login","")=="blocked","login does not play");
Check(PlaybackGate.Context(true,false,"NewUnknownScreen","MainMenu")=="blocked","unknown screen defaults to paused");
using var game=AssemblyDefinition.ReadAssembly(@"C:\SPT\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll");
var sounds=game.MainModule.GetType("EFT.UI.GUISounds");
Check(sounds.Fields.Any(f=>f.Name=="audioSource_3"&&f.FieldType.FullName=="UnityEngine.AudioSource"),"stock music source hook matches installed game");
Check(sounds.Methods.Count(m=>m.Name=="PlayMenuBackgroundMusic")==1,"stock playback method exists unambiguously");
using var overlay=AssemblyDefinition.ReadAssembly(@"C:\SPT\BepInEx\plugins\Anvil-WebOverlay\Anvil-WebOverlay.dll");
var api=overlay.MainModule.GetType("WebOverlay.IWebOverlay");Check(new[]{"Post","Show","Hide","Navigate","Dispose"}.Take(4).All(n=>api.Methods.Any(m=>m.Name==n)),"overlay playback/visibility API present");
Check(overlay.MainModule.GetType("WebOverlay.VirtualHost").Properties.Any(p=>p.Name=="Access"),"virtual host access control present");
Console.WriteLine($"{count} checks passed");

Check(YouTubeAudio.Canonical("https://www.youtube.com/watch?v=EfqsJm6BKng&list=PLtesting_123")=="https://www.youtube.com/playlist?list=PLtesting_123","backend normalizes playlist URLs");
Check(YouTubeAudio.Canonical("https://youtu.be/EfqsJm6BKng")=="https://www.youtube.com/watch?v=EfqsJm6BKng","backend normalizes video URLs");
foreach(var url in new[]{"file:///C:/secret","https://youtube.com.evil.test/watch?v=EfqsJm6BKng","https://user@youtube.com/watch?v=EfqsJm6BKng","https://youtube.com/watch?v=--exec"}){bool denied=false;try{YouTubeAudio.Canonical(url);}catch(ArgumentException){denied=true;}Check(denied,"backend rejects unsafe URL "+url.Split(':')[0]);}
var scratch=Path.Combine(AppContext.BaseDirectory,"metadata-fixture");Directory.CreateDirectory(scratch);var art=Path.Combine(scratch,"art");Directory.CreateDirectory(art);var wav=Path.Combine(scratch,"fixture.wav");
using(var w=new BinaryWriter(File.Create(wav))){w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+16000);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(16000);w.Write(new byte[16000]);}
using(var tag=TagLib.File.Create(wav)){tag.Tag.Title="Metadata title";tag.Tag.Performers=new[]{"Metadata artist"};tag.Tag.Album="Metadata album";tag.Save();}
var trackMetadata=TrackCatalog.ReadOne(wav,art);Check(trackMetadata.name=="Metadata title"&&trackMetadata.artist=="Metadata artist"&&trackMetadata.album=="Metadata album","local title/artist/album tags are read");
var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j9xkAAAAASUVORK5CYII=");File.WriteAllBytes(Path.ChangeExtension(wav,".png"),png);trackMetadata=TrackCatalog.ReadOne(wav,art);Check(trackMetadata.cover.StartsWith("Artwork/")&&File.Exists(Path.Combine(art,Path.GetFileName(trackMetadata.cover))),"sidecar cover is cached with a safe URL");
var taskbar=game.MainModule.GetType("EFT.UI.MenuTaskBar");Check(taskbar.Methods.Any(m=>m.Name=="Awake")&&taskbar.Fields.Any(f=>f.Name=="_toggleButtons"),"native bottom-menu injection members match installed game");
Console.WriteLine($"Total: {count} checks passed");

using(var tag=TagLib.File.Create(wav)){tag.GetTag(TagLib.TagTypes.Id3v2,true).Pictures=new TagLib.IPicture[]{new TagLib.Picture(new TagLib.ByteVector(png)){Type=TagLib.PictureType.FrontCover}};tag.Save();}
File.Delete(Path.ChangeExtension(wav,".png"));var embedded=TrackCatalog.ReadOne(wav,art);Check(embedded.cover.StartsWith("Artwork/"),"embedded album artwork is extracted without a sidecar");
Console.WriteLine($"Final: {count} checks passed");



