# Noctune

A Windows music player by **L3B4D**, built on YumaPlayer by **MuwMx**.

Noctune adapts the original project for Windows with a dark interface, a movable in-app glass player, an expanded player, animated playback progress, synchronized lyrics when timing is available, discovery, playlists, downloads and local playback.

## Status

This repository contains the Windows source and build assets. It is not an installer or a self-contained portable release. External playback tools are required at runtime. Provider availability, account permissions and network conditions affect streaming and lyrics.

Spotify, Qobuz and local-model lyric translation are optional. No credentials or language models are included.

## Build

Use 64-bit Windows 10/11 with .NET Framework 4.8 and Windows SDK metadata available under `Windows/System32/WinMetadata`.

1. Run `powershell -File build.ps1` from the repository directory. The script downloads the pinned Microsoft WebView2 SDK from NuGet and compiles `Noctune.exe`.
2. Install the Microsoft Edge WebView2 Evergreen Runtime for the expanded player.
3. Arrange the runtime dependencies described in [DEPENDENCIES.md](DEPENDENCIES.md) beside the executable before launching.

The existing build uses the .NET Framework C# compiler and Windows metadata paths on drive C. Adjust `source/build.cmd` if Windows is installed elsewhere.

## Using the app

Search for songs, open local audio files, or browse discovery. Right-click a song for queue, playlist, and offline actions. **Downloads** in the desktop sidebar opens saved songs. Click the floating player's artwork to open the expanded player; drag the compact player to move it within the window. Settings includes optional accounts, audio controls, backup/restore, reduced motion, and **About Noctune**.

User state is stored in `data/` beside the executable. Keep the app in a writable folder. Do not commit that directory: it can contain account/session information, listening history, library data and downloaded audio.

## Credits and license

With recognition and thanks to **MuwMx**, creator of [YumaPlayer](https://github.com/MuwMx/YumaPlayer), whose work formed the basis of this Windows adaptation. Upstream credits include ArchiveTune, Metrolist and SimpMusic.

Noctune retains the **GNU GPL v3** license. See [LICENSE](LICENSE) and [CREDITS.txt](CREDITS.txt). Branding changes do not replace upstream authorship. Third-party components retain their own licenses; preserve their notices when assembling a distributable. Sample music artwork remains the property of its respective rights holders.
