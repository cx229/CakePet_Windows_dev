param(
    [Parameter(Mandatory=$true)][string]$Jit,
    [Parameter(Mandatory=$true)][string]$Aot,
    [Parameter(Mandatory=$true)][string]$Package,
    [int]$DurationMs=30000
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $Package).Path
$jitExe=(Resolve-Path $Jit).Path
$aotExe=(Resolve-Path $Aot).Path
$out=New-Item -ItemType Directory -Force -Path artifacts/framework-report
$rows=@()
foreach($count in @(-1,0,1,10)){
    foreach($variant in @(@{Name='JIT';Exe=$jitExe},@{Name='NativeAOT';Exe=$aotExe})){
        $duration=if($count -eq -1){5000}else{$DurationMs}
        $file=Join-Path $out.FullName "$($variant.Name)-$count.json"
        $arguments=@('--framework-probe',('"{0}"' -f $root),"$count","$duration",('"{0}"' -f $file))
        $p=Start-Process -FilePath $variant.Exe -ArgumentList $arguments -PassThru
        try{
            if(!$p.WaitForExit($duration+90000)){Stop-Process -Id $p.Id -Force;throw "Probe timed out: $($variant.Name) $count"}
            if($p.ExitCode -ne 0 -or !(Test-Path $file)){
                if(Test-Path "$file.error"){Get-Content "$file.error" | Write-Host}
                throw "Probe failed: $($variant.Name) $count exit=$($p.ExitCode)"
            }
        }finally{$p.Dispose()}
        $report=Get-Content -Raw $file | ConvertFrom-Json
        if($report.runtime -ne $variant.Name){throw "Wrong runtime: $($report.runtime)"}
        $last=$report.samples[-1]
        $row=[pscustomobject]@{Runtime=$report.runtime;Count=$count;WorkingSetMiB=[Math]::Round($last.workingSetBytes/1MB,2);PrivateMiB=[Math]::Round($last.privateBytes/1MB,2);GcCommittedMiB=[Math]::Round($last.gcCommittedBytes/1MB,2);NativeCacheMiB=[Math]::Round($last.nativeCacheBytes/1MB,2);CpuMs=[Math]::Round($last.cpuMilliseconds,1);Ticks=$last.ticks;Handles=$last.handles;Threads=$last.threads;Modules=($last.frameworkModules -join ', ')}
        $rows+=$row
        Write-Host ($row|ConvertTo-Json -Compress)
    }
}
$rows|Export-Csv (Join-Path $out.FullName 'comparison.csv') -NoTypeInformation
$lines=@('## Production resident framework comparison','Same production host and settings, isolated data; no forced GC or working set trim. -1 = empty process, 0 = full host with no pets.','', '|Runtime|Pets|WS MiB|Private MiB|GC MiB|Cache MiB|CPU ms|Ticks|Handles|Threads|','|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach($r in $rows){$lines+="|$($r.Runtime)|$($r.Count)|$($r.WorkingSetMiB)|$($r.PrivateMiB)|$($r.GcCommittedMiB)|$($r.NativeCacheMiB)|$($r.CpuMs)|$($r.Ticks)|$($r.Handles)|$($r.Threads)|"}
$lines|Write-Host
if($env:GITHUB_STEP_SUMMARY){$lines|Add-Content -Encoding utf8 $env:GITHUB_STEP_SUMMARY}
