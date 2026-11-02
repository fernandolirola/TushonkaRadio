# zezinho's Tushonka Radio

A minimal audio player for the main menu and hideout, with YouTube playlists and local music.

**Open from RADIO in the native bottom menu, beside HIDEOUT/CraftQueue, or use F8.** The default differs from CraftQueue's F9 so the two players do not open together.

The compact view shows cover art, song title, artist, previous/play/next, shuffle, repeat, and a thin seek bar. Drag the dotted grip on the left to move the bar or the lower-right grip to resize it. Click X to hide it and use RADIO or F8 to bring it back. Click the title, cover or chevron to open the library. The UI uses the same charcoal (#111111), orange (#f2aa3c) and cream (#efebe0) as Shopping List and Cultist Circle Planner. Only the visible player rectangle receives mouse input; empty space around the collapsed player belongs to the game.

## Playback

- YouTube is audio-only. The first time a track plays, the backend caches its audio locally. The next play uses that cache.
- Music continues when the player closes or minimizes, for both YouTube and local files.
- Music pauses during raid loading, raids and results screens. Optional resume continues afterward; manually paused music stays paused.
- The game's stock menu music is muted only while Radio is actually playing. Other sounds stay available.
- Saved playlists, volume, shuffle/repeat and separate menu/hideout selections are remembered.
- Local songs use title/artist/album tags and embedded cover art. A same-name JPG/PNG or cover.jpg/cover.png in the Music folder can provide artwork when no embedded picture exists.

## Install

Requires SPT 4.1.6, BepInEx 5, Windows with WebView2, and windowed or borderless mode.

1. Install [Anvil WebOverlay 1.11.0 or newer](https://github.com/maschine34675/WebOverlay/releases) if it is not already installed.
2. Close the game and extract `TushonkaRadio-1.0.15.zip` into its folder.
3. Open RADIO or press F8, expand the library and choose a playlist or local song.

The supplied YouTube playlist is included as a preset. Up to 500 entries are imported. Cached audio is limited to 150 MB per track. To reclaim disk space, open F12, find Tushonka Radio, and click Clear YouTube cache under YouTube. 

The ZIP does not contain music, user configuration, game DLLs, or the shared Anvil library. It includes the local metadata library, its license and corresponding library source. YouTube support is included in the package.

## Local music

Put files directly in `BepInEx/plugins/HideoutRadio/Player/Music/`, then choose Local music and Refresh. Supported extensions: MP3, OGG, WAV, M4A, AAC, FLAC, OPUS and WEBM. Codec support depends on WebView2. The top-level Music folder is scanned, up to 1,000 tracks. Your music files are read without changing their tags.

## Settings (F12)

Enabled; Portuguese; Open key (F8 by default); Show Radio button; Resume after raid; Mute stock music while playing.

Existing custom key bindings are preserved. If an old installation still uses F9, choose F8 or another free key in F12.

## Build and preview

```
dotnet build Client/HideoutRadio.csproj -c Release -warnaserror
node --test tools/Checks/core.test.cjs
dotnet run --project tools/Checks/Checks.csproj -c Release
python tools/preview.py
```

References default to `C:\SPT`; override the client build with `-p:SPTPath=...`. The preview is localhost:8766. `/dev.html` has explicit controls for simulated raid transitions. YouTube in the preview uses the same C# backend via the checks runner. Browser-selected local files last only for that preview session.

## Português

Abra RÁDIO na barra inferior, ao lado de HIDEOUT/CraftQueue, ou aperte F8. O player compacto mostra capa, nome da faixa, artista e controles. Clique na capa, no título ou na seta para abrir a biblioteca.

YouTube e arquivos locais continuam tocando com o player fechado ou minimizado. A música pausa nas raids e pode continuar ao voltar. O suporte ao YouTube já vem incluído no mod.













