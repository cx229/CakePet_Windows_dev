param(
    [Parameter(Mandatory=$true)][string]$Package,
    [int]$DurationMs=5000
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $Package).Path
$output=Join-Path ([System.IO.Path]::GetTempPath()) ("CakePet-host-probe-"+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
function Invoke-Probe {
    param([string]$Project,[string[]]$Arguments,[string]$Name)
    $file=Join-Path $output "$Name.json"
    $all=@($Arguments)+@($file,"$DurationMs")
    & dotnet run --project $Project -c Release --no-build -- @all
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $Name (exit $LASTEXITCODE)" }
    if (!(Test-Path $file)) { throw "No result for $Name" }
    Get-Content -Raw $file | ConvertFrom-Json
}
try {
    $native='tests/CakePet.NativeProbe'
    $wpf='src/CakePet.Windows'
    $rows=@(
        (Invoke-Probe -Project $native -Arguments @('baseline') -Name 'dotnet-baseline')
        (Invoke-Probe -Project $native -Arguments @('tray') -Name 'native-tray')
        (Invoke-Probe -Project $native -Arguments @('sprite',$root) -Name 'native-sprite')
        (Invoke-Probe -Project $wpf -Arguments @('--memory-probe','tray',$root) -Name 'wpf-tray')
        (Invoke-Probe -Project $wpf -Arguments @('--memory-probe','sprite',$root) -Name 'wpf-sprite')
    )
    $lines=@('## Isolated host memory probe','These are short-running Windows CI samples, not measurements of the user desktop. The sprite cases use one half-scale pet, with food and thoughts disabled. No forced GC or working-set trimming.','', '| Phase | Working set MiB | Private bytes MiB | Managed estimate MiB | Cache MiB | Tray registered |','|---|---:|---:|---:|---:|---|')
    foreach($r in $rows){
        $line='| {0} | {1:F1} | {2:F1} | {3:F1} | {4:F1} | {5} |' -f $r.phase,($r.workingSetBytes/1MB),($r.privateBytes/1MB),($r.managedLiveBytes/1MB),($r.imageCacheBytes/1MB),$r.trayRegistered
        $lines+=$line;Write-Host $line
        Write-Host ($r|ConvertTo-Json -Depth 6 -Compress)
    }
    if($env:GITHUB_STEP_SUMMARY){$lines | Add-Content -Encoding utf8 $env:GITHUB_STEP_SUMMARY}
}
finally { Remove-Item -LiteralPath $output -Recurse -Force }
