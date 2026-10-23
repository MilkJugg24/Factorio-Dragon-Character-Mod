<#
Multiplayer test on one PC, everything under $env:TEMP\avatar-mp (no personal saves or mods touched).
  mp.ps1 setup                    folders, configs, mods (avatar-characters_0.3.1.zip + bundle 1.0.1) for server, client, client2
  mp.ps1 create                   a new game with the server's mods -> save\mp-test.zip
  mp.ps1 server                   starts the headless server (RCON 27015 / test) on save\mp-test.zip
  mp.ps1 client [client|client2]  starts a graphical client that joins localhost
  mp.ps1 stop-client [name] / stop-server
#>
param([Parameter(Mandatory = $true)][string]$Action, [string]$Name = "client")
$ErrorActionPreference = "Stop"
$repo = "C:\Users\MilkJ\source\Factorio-Dragon-Character-Mod"
$exe = "C:\Program Files (x86)\Steam\steamapps\common\Factorio\bin\x64\factorio.exe"
$root = Join-Path $env:TEMP "avatar-mp"
$save = Join-Path $root "save\mp-test.zip"
function Quote([string]$s) { '"' + $s + '"' }

function Instance([string]$n) {
    $dir = Join-Path $root $n
    @{ dir = $dir; config = (Join-Path $dir "config\config.ini"); mods = (Join-Path $root "$n-mods") }
}

function Write-ModList([string]$mods) {
    $names = foreach ($z in Get-ChildItem $mods -Filter *.zip) { $z.BaseName -replace '_[0-9.]+$', '' }
    $list = @(@{ name = "base"; enabled = $true })
    $list += foreach ($n in "space-age", "quality", "elevated-rails") { @{ name = $n; enabled = $true } }
    $list += foreach ($n in $names) { @{ name = $n; enabled = $true } }
    @{ mods = $list } | ConvertTo-Json -Depth 4 | Set-Content -Encoding ASCII (Join-Path $mods "mod-list.json")
}

switch ($Action) {
    "setup" {
        New-Item -ItemType Directory -Force -Path (Join-Path $root "save") | Out-Null
        $modZip = Join-Path $root "avatar-characters_0.3.1.zip"
        & py -3 -c @"
import os, zipfile
src = r'$repo\avatar-characters'
with zipfile.ZipFile(r'$modZip', 'w', zipfile.ZIP_DEFLATED) as z:
    for d, _, fs in os.walk(src):
        for f in fs:
            p = os.path.join(d, f)
            z.write(p, 'avatar-characters_0.3.1/' + os.path.relpath(p, src).replace(os.sep, '/'))
print('zipped', r'$modZip')
"@
        foreach ($n in "server", "client", "client2") {
            $i = Instance $n
            New-Item -ItemType Directory -Force -Path (Split-Path $i.config), $i.mods | Out-Null
            "[path]`r`nread-data=__PATH__executable__/../../data`r`nwrite-data=$($i.dir -replace '\\', '/')`r`n" |
                Set-Content -Encoding ASCII $i.config
            Copy-Item $modZip $i.mods
            Copy-Item (Join-Path $repo "build\igc\avatar-bundle-test-server_1.0.1.zip") $i.mods
            Write-ModList $i.mods
        }
        @{
            name = "Avatar MP test"; description = "local test"; visibility = @{ public = $false; lan = $false }
            require_user_verification = $false; max_upload_in_kilobytes_per_second = 0; minimum_latency_in_ticks = 0
            ignore_player_limit_for_returning_players = $false; allow_commands = "admins-only"
            autosave_interval = 60; autosave_slots = 1; afk_autokick_interval = 0; auto_pause = $false
            only_admins_can_pause_the_game = $true; autosave_only_on_server = $true
        } | ConvertTo-Json | Set-Content -Encoding ASCII (Join-Path $root "server-settings.json")
        Get-ChildItem -Recurse $root -Include *.zip, *.ini, *.json | Select-Object FullName | Format-Table -HideTableHeaders | Out-String
    }
    "create" {
        $i = Instance "server"
        $p = Start-Process -FilePath $exe -ArgumentList @("--config", (Quote $i.config), "--mod-directory", (Quote $i.mods),
            "--create", (Quote $save)) -Wait -PassThru -WindowStyle Hidden
        "create exit $($p.ExitCode); save exists: $(Test-Path $save)"
        Get-Content (Join-Path $i.dir "factorio-current.log") | Select-String "Error|avatar|Loading mod" | ForEach-Object { $_.Line }
    }
    "server" {
        $i = Instance "server"
        $env:SteamAppId = "427520"; $env:SteamGameId = "427520"
        $p = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden -ArgumentList @(
            "--config", (Quote $i.config), "--mod-directory", (Quote $i.mods), "--start-server", (Quote $save),
            "--server-settings", (Quote (Join-Path $root "server-settings.json")),
            "--rcon-port", "27015", "--rcon-password", "test", "--port", "34197",
            "--console-log", (Quote (Join-Path $root "server-console.log")))
        $p.Id | Set-Content (Join-Path $root "server.pid")
        "server pid $($p.Id)"
    }
    "client" {
        $i = Instance $Name
        $env:SteamAppId = "427520"; $env:SteamGameId = "427520"
        $p = Start-Process -FilePath $exe -PassThru -ArgumentList @(
            "--config", (Quote $i.config), "--mod-directory", (Quote $i.mods), "--mp-connect", "localhost:34197",
            "--disable-audio", "--window-size", "1280x720")
        $p.Id | Set-Content (Join-Path $root "$Name.pid")
        "$Name pid $($p.Id)"
    }
    "stop-client" {
        $f = Join-Path $root "$Name.pid"
        if (Test-Path $f) { Stop-Process -Id ([int](Get-Content $f)) -ErrorAction SilentlyContinue; Remove-Item $f; "stopped $Name" }
    }
    "stop-server" {
        $f = Join-Path $root "server.pid"
        if (Test-Path $f) { Stop-Process -Id ([int](Get-Content $f)) -ErrorAction SilentlyContinue; Remove-Item $f; "stopped server" }
    }
}
