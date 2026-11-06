$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.4314.35'
$cache = Join-Path $root '.dependencies'
$package = Join-Path $cache ('webview2-' + $version)
if (-not (Test-Path (Join-Path $package 'lib/net462/Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $zip = Join-Path $cache ('webview2-' + $version + '.zip')
    Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $package -Force
}
Copy-Item -LiteralPath (Join-Path $package 'lib/net462/Microsoft.Web.WebView2.Core.dll'),(Join-Path $package 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $package 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $root -Force
Copy-Item -LiteralPath (Join-Path $package 'LICENSE.txt') -Destination (Join-Path $root 'WebView2-LICENSE.txt') -Force
& (Join-Path $root 'source/build.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Noctune build failed.' }
Write-Host 'Built Noctune.exe. See DEPENDENCIES.md before launching.'
