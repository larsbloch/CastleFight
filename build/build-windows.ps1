<#
.SYNOPSIS
    Builds the Windows release of Castle Fight into build\windows without opening the Godot editor.

.DESCRIPTION
    1. Finds the Godot .NET (mono) console executable of the right version. If there is none, it downloads the
       portable editor into build\tools (about 100 MB, once).
    2. Checks the Godot export templates and downloads them if they are missing (about 1 GB, once).
    3. Imports the project (writes .uid files for new scripts), exports "Windows Desktop" to
       build\windows\CastleFight.exe, and runs the exported game headless as a smoke test.

    Share the whole build\windows folder (or use -Zip): the exe does not run without the data_ folder next to it.

.PARAMETER Godot
    Path to a Godot console executable. Otherwise the GODOT environment variable, build\tools, PATH and
    WinGet installs are searched.

.PARAMETER Zip
    Also pack build\windows into build\CastleFight-windows.zip for sharing.

.PARAMETER SkipSmokeTest
    Do not run the exported game afterwards.

.EXAMPLE
    .\build\build-windows.ps1
    .\build\build-windows.ps1 -Zip
#>
param(
    [string]$Godot,
    [switch]$Zip,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar in PowerShell 5
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GodotVersion = '4.7.2'
$GodotExeName = "Godot_v$GodotVersion-stable_mono_win64_console.exe"
$ReleaseUrl = "https://github.com/godotengine/godot/releases/download/$GodotVersion-stable"

$BuildDir = $PSScriptRoot
$RepoRoot = Split-Path $BuildDir -Parent
$ProjectDir = Join-Path $RepoRoot 'game'
$OutputDir = Join-Path $BuildDir 'windows'
$ToolsDir = Join-Path $BuildDir 'tools'
$OutputExe = Join-Path $OutputDir 'CastleFight.exe'
$TemplatesDir = Join-Path $env:APPDATA "Godot\export_templates\$GodotVersion.stable.mono"

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Fail([string]$text) { Write-Host "`nBUILD FAILED: $text" -ForegroundColor Red; exit 1 }

function Find-Godot {
    if ($Godot) {
        if (Test-Path $Godot) { return (Resolve-Path $Godot).Path }
        Fail "-Godot '$Godot' does not exist."
    }
    if ($env:GODOT -and (Test-Path $env:GODOT)) { return $env:GODOT }

    $local = Get-ChildItem $ToolsDir -Recurse -Filter $GodotExeName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($local) { return $local.FullName }
    $onPath = Get-Command $GodotExeName -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $winget = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $installed = Get-ChildItem $winget -Directory -Filter 'GodotEngine.GodotEngine.Mono*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter $GodotExeName -ErrorAction SilentlyContinue } |
        Select-Object -First 1
    if ($installed) { return $installed.FullName }
    return $null
}

function Install-Godot {
    Step "Godot $GodotVersion (.NET) not found: downloading the portable editor to build\tools"
    New-Item -ItemType Directory -Force $ToolsDir | Out-Null
    $zipPath = Join-Path $ToolsDir 'godot.zip'
    Invoke-WebRequest "$ReleaseUrl/Godot_v$GodotVersion-stable_mono_win64.zip" -OutFile $zipPath
    Expand-Archive $zipPath -DestinationPath $ToolsDir -Force
    Remove-Item $zipPath
    $exe = Get-ChildItem $ToolsDir -Recurse -Filter $GodotExeName | Select-Object -First 1
    if (-not $exe) { Fail "the download did not contain $GodotExeName." }
    return $exe.FullName
}

function Install-Templates {
    Step "Export templates missing: downloading Godot $GodotVersion .NET export templates (about 1 GB, once)"
    $tpz = Join-Path $env:TEMP "godot_$GodotVersion`_mono_templates.zip" # a .tpz is a zip; Expand-Archive wants .zip
    $unpacked = Join-Path $env:TEMP "godot_$GodotVersion`_mono_templates"
    Invoke-WebRequest "$ReleaseUrl/Godot_v$GodotVersion-stable_mono_export_templates.tpz" -OutFile $tpz
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    Expand-Archive $tpz -DestinationPath $unpacked -Force
    New-Item -ItemType Directory -Force $TemplatesDir | Out-Null
    Copy-Item (Join-Path $unpacked 'templates\*') $TemplatesDir -Recurse -Force
    Remove-Item $tpz, $unpacked -Recurse -Force
}

# ---- Tools ----

Step 'Checking tools'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'the .NET SDK is not installed (https://dotnet.microsoft.com/download).'
}
$GodotExe = Find-Godot
if (-not $GodotExe) { $GodotExe = Install-Godot }
$version = (& $GodotExe --version | Out-String).Trim()
Write-Host "Godot: $GodotExe ($version)"
if (-not $version.StartsWith("$GodotVersion.stable.mono")) {
    Fail "this project needs Godot $GodotVersion .NET (mono), found '$version'. Pass -Godot <path to $GodotExeName>."
}
if (-not (Test-Path (Join-Path $TemplatesDir 'windows_release_x86_64.exe'))) { Install-Templates }
Write-Host "Export templates: $TemplatesDir"

# ---- Build ----

Step 'Importing the project'
& $GodotExe --headless --path $ProjectDir --import
if ($LASTEXITCODE -ne 0) { Fail "the import exited with code $LASTEXITCODE." }

Step "Exporting to $OutputExe"
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutputDir | Out-Null
& $GodotExe --headless --path $ProjectDir --export-release 'Windows Desktop' $OutputExe
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutputExe)) { Fail 'the export did not produce CastleFight.exe (see the Godot output above).' }

# ---- Check ----

if (-not $SkipSmokeTest) {
    Step 'Smoke test: running the exported game headless for a few seconds'
    $log = Join-Path $env:TEMP 'castlefight_smoke.txt'
    $game = Start-Process $OutputExe -ArgumentList '--headless', '--fixed-fps', '60', '--quit-after', '700', '--', '--smoke', '--map', '2' `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $log
    $hud = Get-Content $log | Where-Object { $_ -like 'FPS*Units*' } | Select-Object -Last 1
    if ($game.ExitCode -ne 0 -or -not $hud) { Fail "the exported game did not run cleanly (exit code $($game.ExitCode), log: $log)." }
    Write-Host $hud
}

if ($Zip) {
    $zipPath = Join-Path $BuildDir 'CastleFight-windows.zip'
    Step "Packing $zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path (Join-Path $OutputDir '*') -DestinationPath $zipPath
}

$size = (Get-ChildItem $OutputDir -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("`nBuild OK: {0} ({1:N0} MB in build\windows)" -f $OutputExe, $size) -ForegroundColor Green
