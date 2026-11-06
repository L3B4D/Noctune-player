# Runtime dependencies

Binaries, accounts, cached music and user data are intentionally not committed.

| Component | Expected location | Source |
|---|---|---|
| WebView2 SDK 1.0.4314.35 | `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll` beside Noctune.exe | NuGet package Microsoft.Web.WebView2; restored by build.ps1 |
| WebView2 Evergreen Runtime | Installed on Windows | https://developer.microsoft.com/microsoft-edge/webview2/ |
| VideoLAN VLC 3.x, Windows x64 | `tools/vlc/libvlc.dll`, `libvlccore.dll`, and complete plugins folder | https://www.videolan.org/vlc/ |
| FFmpeg, Windows x64 | `tools/ffmpeg/ffmpeg.exe` | https://ffmpeg.org/download.html |
| yt-dlp, Windows x64 unpacked distribution | `tools/resolver/yt-dlp.exe` and its supporting files | https://github.com/yt-dlp/yt-dlp/releases |
| Node.js, Windows x64 | `tools/node.exe` | https://nodejs.org/ |
| Optional pronunciation packages | `tools/pronunciation/node_modules/` | Run `npm ci` in tools/pronunciation using its lockfile |

Install the Microsoft Visual C++ x64 runtime if required by the chosen native dependencies. Keep each dependency's license and notices with any packaged distribution. Download dependencies only from their upstream publishers. A source build alone does not install these playback components.
