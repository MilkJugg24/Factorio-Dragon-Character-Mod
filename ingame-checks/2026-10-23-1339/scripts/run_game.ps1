<#
Runs Factorio from its own temporary folder (config.ini + write-data, no personal saves or mods).
  run_game.ps1 -Name a -Mods <zip/folder>,... [-VideoMemory low] [-MenuOnly] [-Out <dir>] [-Done <script-output relative path>]
-MenuOnly: start to the main menu, record the time to "Factorio initialised", then quit.
Otherwise: --create a map, load it in the graphical game, wait for script-output\<Done>, copy results.
#>
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string[]]$Mods = @(),
    [string]$VideoMemory = "",
    [switch]$MenuOnly,
    [switch]$KeepFolder,
    [string]$Out = "",
    [string]$Done = "space-test\done.txt",
    [int]$TimeoutMinutes = 8,
    [switch]$DebugOverlay,
    [switch]$CaptureOnReady
)
$ErrorActionPreference = "Stop"
$exe = "C:\Program Files (x86)\Steam\steamapps\common\Factorio\bin\x64\factorio.exe"
$work = Join-Path $env:TEMP "avatar-$Name"
$modDir = Join-Path $work "mods"
$config = Join-Path $work "config\config.ini"
if (-not $KeepFolder -and (Test-Path $work)) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path $modDir, (Split-Path $config) | Out-Null
$ini = "[path]`r`nread-data=__PATH__executable__/../../data`r`nwrite-data=$($work -replace '\\', '/')`r`n"
if ($VideoMemory) { $ini += "[graphics]`r`nvideo-memory-usage=$VideoMemory`r`n" }
if ($DebugOverlay) { $ini += "[debug]`r`nshow-fps=always`r`nshow-time-usage=always`r`n" }
if (-not $KeepFolder -or -not (Test-Path $config)) { $ini | Set-Content -Encoding ASCII $config }
foreach ($m in $Mods) { if (-not (Test-Path (Join-Path $modDir (Split-Path -Leaf $m)))) { Copy-Item -Recurse $m $modDir } }
$names = foreach ($item in Get-ChildItem $modDir) {
    if ($item.PSIsContainer) { (Get-Content (Join-Path $item.FullName "info.json") -Raw | ConvertFrom-Json).name }
    elseif ($item.Extension -eq ".zip") { $item.BaseName -replace '_[0-9.]+$', '' }
}
$list = @(@{ name = "base"; enabled = $true })
$list += foreach ($n in "space-age", "quality", "elevated-rails") { @{ name = $n; enabled = $true } }
$list += foreach ($n in $names) { @{ name = $n; enabled = $true } }
@{ mods = $list } | ConvertTo-Json -Depth 4 | Set-Content -Encoding ASCII (Join-Path $modDir "mod-list.json")
$common = @("--config", "`"$config`"", "--mod-directory", "`"$modDir`"")
$env:SteamAppId = "427520"; $env:SteamGameId = "427520"
$log = Join-Path $work "factorio-current.log"
function Measure-Game($p) {
    $p.Refresh()
    $line = "{0:HH:mm:ss} working set {1:N0} MB, private {2:N0} MB" -f (Get-Date), ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB)
    try {
        $d = ((Get-Counter "\GPU Process Memory(pid_$($p.Id)_*)\Dedicated Usage" -ErrorAction Stop).CounterSamples | Measure-Object CookedValue -Sum).Sum
        $s = ((Get-Counter "\GPU Process Memory(pid_$($p.Id)_*)\Shared Usage" -ErrorAction Stop).CounterSamples | Measure-Object CookedValue -Sum).Sum
        $line += ", GPU dedicated {0:N0} MB, GPU shared {1:N0} MB" -f ($d / 1MB), ($s / 1MB)
    } catch { $line += ", GPU counters unavailable" }
    $line
}
function Capture-Window($p, [string]$path) {
    Add-Type -AssemblyName System.Drawing
    if (-not ("WinRect" -as [type])) {
        Add-Type 'using System; using System.Runtime.InteropServices; public static class WinRect { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); }'
    }
    $p.Refresh()
    $r = New-Object WinRect+RECT; [void][WinRect]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}


if ($MenuOnly) {
    if (Test-Path $log) { Remove-Item $log }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $game = Start-Process -FilePath $exe -PassThru -ArgumentList ($common + @("--disable-audio", "--window-size", "1280x720"))
    while (-not $game.HasExited -and $sw.Elapsed.TotalMinutes -lt $TimeoutMinutes) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern "Factorio initialised" -Quiet)) { break }
        Start-Sleep -Milliseconds 200
    }
    $wall = $sw.Elapsed.TotalSeconds
    Start-Sleep -Seconds 4  # the menu is drawn
    $game.Refresh()
    "wall-clock to 'Factorio initialised': {0:N1} s; working set {1:N0} MB; private {2:N0} MB" -f $wall, ($game.WorkingSet64 / 1MB), ($game.PrivateMemorySize64 / 1MB)
    try {
        $gpu = (Get-Counter "\GPU Process Memory(pid_$($game.Id)_*)\Dedicated Usage" -ErrorAction Stop).CounterSamples | Measure-Object CookedValue -Sum
        "GPU dedicated memory: {0:N0} MB" -f ($gpu.Sum / 1MB)
        $sh = (Get-Counter "\GPU Process Memory(pid_$($game.Id)_*)\Shared Usage" -ErrorAction Stop).CounterSamples | Measure-Object CookedValue -Sum
        "GPU shared memory: {0:N0} MB" -f ($sh.Sum / 1MB)
    } catch { "GPU counters unavailable: $_" }
    if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force }
    Start-Sleep -Seconds 2
    if ($Out) { New-Item -ItemType Directory -Force -Path $Out | Out-Null; Copy-Item $log (Join-Path $Out "factorio-current.log") }
    Select-String -Path $log -Pattern "Factorio initialised|Sprites loaded|atlas|VRAM|mipmaps uploaded|Loading sprites|video memory|GPU|Graphics|Display" |
        ForEach-Object { $_.Line.TrimEnd() }
    return
}

$save = Join-Path $work "test.zip"
Start-Process -FilePath $exe -ArgumentList ($common + @("--create", "`"$save`"")) -Wait -WindowStyle Hidden
if (-not (Test-Path $save)) { Get-Content $log -Tail 20; throw "no map" }
$game = Start-Process -FilePath $exe -PassThru -ArgumentList ($common + @("--load-game", "`"$save`"", "--disable-audio", "--window-size", "1280x720"))
$game.Id | Set-Content (Join-Path $work "game.pid")
$doneFile = Join-Path $work "script-output\$Done"
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$readyFile = Join-Path (Split-Path -Parent $doneFile) "ready.txt"
$shots = 0; $measures = @()
if ($Out) { New-Item -ItemType Directory -Force -Path $Out | Out-Null }
while (-not (Test-Path $doneFile) -and -not $game.HasExited -and (Get-Date) -lt $deadline) {
    if ($CaptureOnReady -and (Test-Path $readyFile)) {
        $shots++
        $measures += Measure-Game $game
        if ($Out) { Capture-Window $game (Join-Path $Out ("screen-{0:D2}.png" -f $shots)) }
        Start-Sleep -Seconds 5
    } else { Start-Sleep -Seconds 1 }
}
if ($measures) { $measures | Tee-Object -FilePath (Join-Path $Out "memory.txt") }
Start-Sleep -Seconds 3
if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force }
Start-Sleep -Seconds 1
if ($Out) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    $results = Join-Path $work ("script-output\" + (Split-Path -Parent $Done))
    if (Test-Path $results) { Copy-Item -Recurse -Force (Join-Path $results "*") $Out }
    Copy-Item $log (Join-Path $Out "factorio-current.log")
}
"done file: $(Test-Path $doneFile)"
