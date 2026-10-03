# Render every plugin in .\in under several window states and report clipped text.
#   .\check.ps1                 all plugins, all variants
#   .\check.ps1 -Only CounterThrust
param([string]$Only = '')
Set-Location $PSScriptRoot
$collapsedKeys = 'Window.Collapsed','GUI.Collapsed','Desktop GUI.Collapsed','Touch Filters.ControlWindowCollapsed'
$wKeys = 'Window.Width','Window.ExpandedWidth','Window.WindowWidth','GUI.WindowWidth','Desktop GUI.WindowWidth','Touch Filters.ControlWindowWidth'
$hKeys = 'Window.Height','Window.ExpandedHeight','Window.WindowHeight','GUI.WindowHeight','Desktop GUI.WindowHeight','Touch Filters.ControlWindowHeight'
$scaleKeys = 'Window.Scale','Window.UIScale','Window.UserScale','GUI.ManualUIScale','Desktop GUI.UIScale','Touch Filters.ControlWindowScale'
function Opts($keys, $val) { $keys | ForEach-Object { '--set'; "$_=$val" } }
$variants = [ordered]@{
    default   = @()
    collapsed = (Opts $collapsedKeys 'true')
    minsize   = (Opts $wKeys 1) + (Opts $hKeys 1)
    scale2    = (Opts $scaleKeys 2.0) + @('--w', '2560', '--h', '1440')
    scalesmall = (Opts $scaleKeys 0.65)
}
$total = 0
Get-ChildItem in -Filter *.dll | Where-Object { $_.Name -match $Only } | ForEach-Object {
    foreach ($v in $variants.Keys) {
        $out = & .\bin\uisim.exe $_.FullName --out out --tag $v @($variants[$v]) 2>&1
        $clips = @($out | Where-Object { $_ -like 'CLIP*' })
        $errs = @($out | Where-Object { $_ -like 'ERR*' } | Where-Object { $_ -notmatch 'Update:|coroutine:' })
        $status = if ($clips.Count -eq 0) { 'ok' } else { "$($clips.Count) CLIPPED" }
        '{0,-50} {1,-10} {2}' -f $_.BaseName, $v, $status
        $clips | ForEach-Object { '      ' + $_.Substring(0, [Math]::Min(190, $_.Length)) }
        $errs | ForEach-Object { '      ' + $_.Substring(0, [Math]::Min(190, $_.Length)) }
        $total += $clips.Count
    }
}
"total clipped: $total"
