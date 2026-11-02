using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WebOverlay;

namespace HideoutRadio;

[BepInPlugin(Id, "Tushonka Radio", "1.0.16")]
[BepInDependency("com.anvil.weboverlay", "1.11.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.zezinho.hideoutradio";
    private static Plugin? _instance;
    private static readonly AccessTools.FieldRef<GUISounds, AudioSource> MenuMusic = AccessTools.FieldRefAccess<GUISounds, AudioSource>("audioSource_3");
    private ConfigEntry<bool> _enabled = null!, _portuguese = null!, _resume = null!, _muteStock = null!, _button = null!;
    private ConfigEntry<KeyboardShortcut> _key = null!;
    private readonly List<TaskBarButton> _buttons = new();
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _listJob, _audioJob;
    private ConfigEntry<int> _cacheLimit = null!;
    private float _nextCacheUsage;
    private string _cacheUsage = "";
    private ConfigEntry<bool> _preloadNext = null!;
    private string _activeAudioRequest = "";
    private bool _scanning, _clearCachePending;
    private float _cacheClearDeadline;
    private string _cacheStatus = "";
    private IWebOverlay? _overlay;
    private readonly OverlayDrag _drag = new();
    private Harmony? _harmony;
    private AudioSource? _music;
    private bool _ownsMute, _oldMute, _wantedVisible;
    private float _heartbeat = -100, _nextState;
    private bool _playing;
    private int _epoch;
    private string _mode = "blocked";
    private string _error = "";
    private string Root => Path.GetDirectoryName(Info.Location)!;

    private void Awake()
    {
        _instance = this;
        _cacheLimit = Config.Bind("YouTube", "Cache limit (MB)", 100,
            new ConfigDescription("Maximum space for cached YouTube audio, including preloaded tracks.",
                new AcceptableValueRange<int>(16, 2048)));
        _preloadNext = Config.Bind("YouTube", "Preload next track", false,
            "Download one upcoming YouTube track while music plays. Uses extra bandwidth and disk space.");
        _enabled = Config.Bind("General", "Enabled", true, "Enable the menu and hideout music player.");
        _portuguese = Config.Bind("General", "Portuguese", false, "Use Portuguese in the player.");
        _key = Config.Bind("General", "Open key", new KeyboardShortcut(KeyCode.F8), "Open or close Tushonka Radio. F8 avoids CraftQueue's F9 shortcut.");
        _resume = Config.Bind("Playback", "Resume after raid", true, "Resume music that was playing when raid loading started. Manually paused music stays paused.");
        _muteStock = Config.Bind("Playback", "Mute stock music while playing", true, "Mute only the stock menu music while Tushonka Radio is playing. Does not mute hideout machinery or other sounds.");
        _button = Config.Bind("General", "Show Radio button", true, "Add RADIO to the native bottom menu, beside HIDEOUT and CraftQueue.");
        Directory.CreateDirectory(Path.Combine(Root, "Player", "Music"));
        Config.Bind("YouTube", "Clear YouTube cache", false,
            new ConfigDescription("Delete downloaded YouTube tracks. They will download again when played.",
                null, new ConfigurationManagerAttributes { CustomDrawer = DrawCacheControl }));
        try { YouTubeAudio.TrimCache(CacheDirectory, _cacheLimit.Value * 1_000_000L, null); }
        catch (IOException ex) { Logger.LogWarning("Could not trim the YouTube cache: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Logger.LogWarning("Could not trim the YouTube cache: " + ex.Message); }
        Config.SettingChanged += SettingsChanged;
        _harmony = new Harmony(Id);
        _harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("Tushonka Radio 1.0.16 ready. Open with F8 or the native RADIO menu button.");
    }

    private void SettingsChanged(object sender, SettingChangedEventArgs args) { _nextState = 0; Push(); }

    private void Update()
    {
        while (_pending.TryDequeue(out var action)) action();
        _drag.Update(_overlay);
        if (_clearCachePending && Time.unscaledTime >= _cacheClearDeadline)
        {
            _clearCachePending = false;
            _cacheStatus = "The player did not release the cache. Try again.";
        }
        if (Time.unscaledTime >= _nextState)
        {
            _nextState = Time.unscaledTime + .2f;
            var current = EftScreenManager._instance?.CurrentBaseScreenController;
            bool raid = Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is not HideoutGameWorld;
            string next = PlaybackGate.Context(_enabled.Value, raid, current?.ScreenType.ToString() ?? "", current?.RootScreen?.ScreenType.ToString() ?? "");
            _buttons.RemoveAll(button => !button.Alive);
            foreach (var button in _buttons) button.Sync(_button.Value && next != "blocked", _overlay?.IsVisible == true, _portuguese.Value);
            if (next != _mode)
            {
                bool wasBlocked = _mode == "blocked";
                _mode = next;
                _epoch++;
                if (_mode == "blocked") _playing = false;
                if (_mode == "blocked") { Push(); _overlay?.Hide(); }
                else if (wasBlocked && _wantedVisible && _overlay != null) _overlay.Show(_ => Push());
                else Push();
            }
        }
        if (_mode != "blocked" && _key.Value.IsDown()) Toggle();
        SetStockMute(_mode != "blocked" && _muteStock.Value && _playing && Time.unscaledTime - _heartbeat < 3f);
    }

    private void OpenFromTaskBar()
    {
        Logger.LogInfo("RADIO clicked in " + _mode + ".");
        try { Toggle(); }
        catch (Exception ex) { Logger.LogError("RADIO could not open: " + ex); }
    }

    private void Toggle()
    {
        if (_mode == "blocked") return;
        if (_overlay != null) { _overlay.Toggle(); return; }
        string player = Path.Combine(Root, "Player");
        if (!File.Exists(Path.Combine(player, "index.html"))) { _error = "Tushonka Radio: reinstall the Player folder."; return; }
        var overlay = WebOverlays.Create("Tushonka Radio", new OverlayOptions {
            Width = 440, Height = Math.Min(680, Math.Max(360, Screen.height - 100)), Frame = false,
            FreeCursorWhileShown = true, ClickThroughWhenUnfocused = true,
            DispatchOnMainThread = true, RememberBounds = true, PersistenceKey = Id,
            VirtualHosts = new[] { new VirtualHost("hideoutradio.assets", player) { Access = HostAccess.Deny } }
        });
        if (overlay == null) { Logger.LogError("Tushonka Radio: WebView2 is unavailable."); return; }
        _overlay = overlay;
        _wantedVisible = true;
        overlay.KeyPressed += key => {
            var shortcut = _key.Value;
            if (!shortcut.Modifiers.Any() && shortcut.MainKey >= KeyCode.F1 && shortcut.MainKey <= KeyCode.F12
                && key == 112 + shortcut.MainKey - KeyCode.F1) overlay.Hide();
        };
        overlay.MessageReceived += Receive;
        overlay.PageLoaded += () => { Push(); PushLibrary(); };
        overlay.VisibilityChanged += visible => {
            if (_mode != "blocked") _wantedVisible = visible;
            Push();
        };
        overlay.Failed += () => {
            Logger.LogError("Radio overlay failed: " + overlay.FailureMessage);
            _error = "Tushonka Radio: " + overlay.FailureMessage;
            _playing = false;
            SetStockMute(false);
            overlay.Dispose();
            if (ReferenceEquals(_overlay, overlay)) _overlay = null;
        };
        overlay.Navigate("https://hideoutradio.assets/index.html");
        overlay.Show();
    }

    private void Push()
    {
        if (_overlay == null || !_overlay.IsPageLoaded) return;
        Send(new { type = "runtime", mode = _mode, epoch = _epoch, visible = _overlay.IsVisible, resume = _resume.Value, preloadNext = _preloadNext.Value, language = _portuguese.Value ? "pt" : "en" });
    }

    private void Send(object value) => _overlay?.Post(JsonConvert.SerializeObject(value));

    private void Receive(string message)
    {
        if (message == null || message.Length > 8192) return;
        try
        {
            var data = JObject.Parse(message);
            switch ((string?)data["type"])
            {
                case "ready": Push(); PushLibrary(); break;
                case "refresh": PushLibrary(); break;
                case "drag":
                    string? drag = (string?)data["action"];
                    if (_mode != "blocked" && _overlay?.IsVisible == true && (drag == "move" || drag == "resize"))
                        _drag.Begin(drag == "resize", (double?)data["height"] ?? 90, (double?)data["scale"] ?? 1);
                    break;
                case "close": _overlay?.Hide(); break;
                case "language": _portuguese.Value = (string?)data["language"] == "pt"; break;
                case "cache-released": if (_clearCachePending) ClearCache(); break;
                case "youtube-list": LoadYouTubeList(data); break;
                case "cancel-preload":
                    if ((string?)data["request"] == _activeAudioRequest) _audioJob?.Cancel();
                    break;
                case "youtube-audio": if (!_clearCachePending && ((bool?)data["preload"] != true || (_preloadNext.Value && _mode != "blocked"))) LoadYouTubeTrack(data); break;
                case "status":
                    if ((int?)data["epoch"] != _epoch) return;
                    _heartbeat = Time.unscaledTime;
                    _playing = _mode != "blocked" && (bool?)data["playing"] == true;
                    break;
            }
        }
        catch (Exception ex) { Logger.LogWarning("Ignored radio message: " + ex.Message); }
    }

    private string CacheDirectory => Path.Combine(Root, "Player", "YouTubeCache");
    private YouTubeAudio AudioBackend() => new YouTubeAudio(CacheDirectory);

    private void DrawCacheControl(ConfigEntryBase entry)
    {
        if (GUILayout.Button("Clear YouTube cache") && !_clearCachePending)
        {
            _clearCachePending = true;
            _cacheClearDeadline = Time.unscaledTime + 5f;
            _cacheStatus = "Clearing YouTube cache...";
            _audioJob?.Cancel();
            if (_overlay?.IsPageLoaded == true) Send(new { type = "release-cache" });
            else ClearCache();
        }
        if (Time.unscaledTime >= _nextCacheUsage)
        {
            _nextCacheUsage = Time.unscaledTime + 2f;
            try { _cacheUsage = $"{YouTubeAudio.CachedTracks(CacheDirectory).Sum(file => file.Length) / 1_000_000d:0.0} / {_cacheLimit.Value} MB"; }
            catch (IOException) { _cacheUsage = "Cache usage unavailable"; }
            catch (UnauthorizedAccessException) { _cacheUsage = "Cache usage unavailable"; }
        }
        GUILayout.Label(_cacheUsage);
        if (_cacheStatus.Length > 0) GUILayout.Label(_cacheStatus);
    }

    private void ClearCache()
    {
        _clearCachePending = false;
        int deleted = 0, skipped = 0;
        try
        {
            var directory = new DirectoryInfo(CacheDirectory);
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var file in directory.EnumerateFiles())
                {
                    if (!YouTubeAudio.VideoId(Path.GetFileNameWithoutExtension(file.Name)) ||
                        !new[] { ".m4a", ".webm", ".opus" }.Contains(file.Extension)) continue;
                    try { file.Delete(); deleted++; }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
            }
            _cacheStatus = skipped == 0 ? $"Cleared {deleted} cached tracks."
                : $"Cleared {deleted} cached tracks. {skipped} files could not be deleted. Try again.";
        }
        catch (Exception ex)
        {
            _cacheStatus = "Could not clear the YouTube cache.";
            Logger.LogWarning(_cacheStatus + " " + ex.Message);
        }
    }

    private async void LoadYouTubeList(JObject data)
    {
        _listJob?.Cancel(); _listJob?.Dispose();
        var job = _listJob = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var token = job.Token;
        string request = ((string?)data["request"] ?? "").Substring(0, Math.Min(80, ((string?)data["request"] ?? "").Length));
        try
        {
            var result = await AudioBackend().List((string?)data["url"] ?? "", token, (bool?)data["fetchTitle"] == true);
            _pending.Enqueue(() => Send(new { type = "youtube-list", request, tracks = result.Tracks, title = result.Title }));
        }
        catch (Exception ex) { if (!job.IsCancellationRequested) _pending.Enqueue(() => Send(new { type = "youtube-error", request, error = ex.Message })); }
    }

    private async void LoadYouTubeTrack(JObject data)
    {
        _audioJob?.Cancel(); _audioJob?.Dispose();
        var job = _audioJob = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var token = job.Token;
        string request = (string?)data["request"] ?? "";
        try
        {
            _activeAudioRequest = request;
            var url = await AudioBackend().Resolve((string?)data["id"] ?? "", token, _cacheLimit.Value * 1_000_000L, Path.GetFileName((string?)data["protectedUrl"]));
            _pending.Enqueue(() => { if (!token.IsCancellationRequested) Send(new { type = "youtube-audio", request, url }); });
        }
        catch (Exception ex) { if (!job.IsCancellationRequested) _pending.Enqueue(() => Send(new { type = "youtube-error", request, error = ex.Message })); }
    }

    private async void PushLibrary()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            string root = Root;
            var tracks = await Task.Run(() => TrackCatalog.Read(Path.Combine(root, "Player", "Music"), Path.Combine(root, "Player", "Artwork")));
            _pending.Enqueue(() => { _scanning = false; Send(new { type = "library", tracks }); });
        }
        catch (Exception ex) { _pending.Enqueue(() => { _scanning = false; Send(new { type = "library", tracks = new object[0], error = ex.Message }); }); }
    }

    private void SetStockMute(bool mute)
    {
        if (_music == null) { _ownsMute = false; return; }
        if (mute)
        {
            if (!_ownsMute) { _oldMute = _music.mute; _ownsMute = true; }
            _music.mute = true;
        }
        else if (_ownsMute) { _music.mute = _oldMute; _ownsMute = false; }
    }

    private void TrackMusic(GUISounds sounds)
    {
        var source = MenuMusic(sounds);
        if (source == _music) return;
        SetStockMute(false);
        _music = source;
    }

    private void OnDestroy()
    {
        Config.SettingChanged -= SettingsChanged;
        _shutdown.Cancel();
        foreach (var button in _buttons) button.Destroy();
        _overlay?.Dispose();
        SetStockMute(false);
        _harmony?.UnpatchSelf();
        _instance = null;
    }

    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusic))]
    private static class MusicPatch
    {
        private static void Postfix(GUISounds __instance) => _instance?.TrackMusic(__instance);
    }

    [HarmonyPatch(typeof(MenuTaskBar), nameof(MenuTaskBar.Awake))]
    private static class TaskBarPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MenuTaskBar __instance)
        {
            if (_instance == null) return;
            try
            {
                var button = TaskBarButton.Create(__instance, _instance.OpenFromTaskBar);
                if (button != null) _instance._buttons.Add(button);
            }
            catch (Exception ex) { _instance.Logger.LogWarning("Radio menu button was not added: " + ex.Message); }
        }
    }
}












internal sealed class ConfigurationManagerAttributes
{
    public Action<ConfigEntryBase>? CustomDrawer;
}









