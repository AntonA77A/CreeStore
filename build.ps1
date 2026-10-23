# Baut dist\CreeStore.exe und dist\CreeStoreSetup.exe mit dem C#-Compiler, der in Windows schon drin ist.
#   .\build.ps1 -Repo "dein-name/CreeStore" -Version "1.0.0"
param(
    [string]$Repo = "AntonA77A/CreeStore",
    [string]$Version = "1.0.0",
    [string]$Branch = "main"
)
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$dist = Join-Path $root "dist"
$obj = Join-Path $root "obj"
$icon = Join-Path $root "assets\creestore.ico"
New-Item -ItemType Directory -Force $dist, $obj, (Split-Path $icon) | Out-Null

$Version = $Version.TrimStart("v", "V")

if (-not (Test-Path $icon)) {
    # Creeper-Gesicht als 256px-Icon zeichnen
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $face = @(
        "........",
        ".XX..XX.",
        ".XX..XX.",
        "...XX...",
        "..XXXX..",
        "..XXXX..",
        "..X..X..",
        "........")
    $rnd = New-Object System.Random 7
    for ($y = 0; $y -lt 8; $y++) {
        for ($x = 0; $x -lt 8; $x++) {
            if ($face[$y][$x] -eq "X") {
                $c = [System.Drawing.Color]::FromArgb(18, 28, 18)
            } else {
                $s = $rnd.Next(-22, 22)
                $c = [System.Drawing.Color]::FromArgb(90 + $s, 185 + $s, 70 + $s)
            }
            $brush = New-Object System.Drawing.SolidBrush $c
            $g.FillRectangle($brush, $x * 32, $y * 32, 32, 32)
            $brush.Dispose()
        }
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray()
    # ICO-Datei mit einem PNG-Bild
    $fs = [System.IO.File]::Create($icon)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)
    $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$png.Length); $w.Write([UInt32]22)
    $w.Write($png)
    $w.Close()
}

@"
namespace CreeStore
{
    static class BuildInfo
    {
        public const string Repo = "$Repo";
        public const string Branch = "$Branch";
        public const string Version = "$Version";
    }
}
"@ | Set-Content -Encoding UTF8 (Join-Path $obj "BuildInfo.cs")

$common = @(
    "/nologo", "/optimize+", "/target:winexe", "/codepage:65001",
    "/win32icon:$icon",
    "/r:System.Web.Extensions.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll"
)
$shared = @("$root\src\Shared.cs", "$obj\BuildInfo.cs")

& $csc @common "/out:$dist\CreeStore.exe" @shared "$root\src\CreeStore.cs"
if ($LASTEXITCODE) { throw "CreeStore.exe konnte nicht gebaut werden" }

& $csc @common "/out:$dist\CreeStoreSetup.exe" "/resource:$dist\CreeStore.exe,CreeStore.exe" @shared "$root\src\Setup.cs"
if ($LASTEXITCODE) { throw "CreeStoreSetup.exe konnte nicht gebaut werden" }

Write-Host "Fertig: $dist\CreeStoreSetup.exe  (Repo: $Repo, Version: $Version)"
