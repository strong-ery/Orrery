<#
.SYNOPSIS
    Builds the release zip to attach to a GitHub release: a self-contained single-file win-x64 build of Orrery.

.DESCRIPTION
    Matches what the app's updater expects: a zip with Orrery.App.exe at its root. Bump AppVersion.Current in
    src/Orrery.Core/Updates/AppVersion.cs (and <Version> in Orrery.App.csproj) to the release tag first, then
    create a GitHub release tagged v<version> and attach publish/orrery-<version>.zip.
#>
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$m = Select-String -Path (Join-Path $root "src/Orrery.Core/Updates/AppVersion.cs") -Pattern 'Current = "([^"]+)"'
if (-not $m) { throw "Couldn't read AppVersion.Current" }
$version = $m.Matches[0].Groups[1].Value
$out = Join-Path $root "publish/orrery"
$zip = Join-Path $root "publish/orrery-$version.zip"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
dotnet publish "$root/src/Orrery.App/Orrery.App.csproj" -c Release -r win-x64 -o $out `
    -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
Get-ChildItem $out -Filter "*.pdb" | Remove-Item -Force
Compress-Archive -Path "$out/*" -DestinationPath $zip
Write-Host "Done: $zip"
