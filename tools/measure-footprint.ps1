param([int]$Seconds = 120)
$ErrorActionPreference = 'Stop'

$process = Get-Process Companion | Select-Object -First 1
$cores = [Environment]::ProcessorCount
$cpuStart = $process.TotalProcessorTime
$start = Get-Date
$samples = New-Object System.Collections.Generic.List[double]
for ($i = 0; $i -lt $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    $samples.Add($process.WorkingSet64 / 1MB)
}
$process.Refresh()
$elapsed = ((Get-Date) - $start).TotalSeconds
$cpu = ($process.TotalProcessorTime - $cpuStart).TotalSeconds / $elapsed / $cores * 100
$average = ($samples | Measure-Object -Average).Average
$maximum = ($samples | Measure-Object -Maximum).Maximum

'Working set: avg {0:N1} MB, max {1:N1} MB' -f $average, $maximum
'Private bytes: {0:N1} MB' -f ($process.PrivateMemorySize64 / 1MB)
'CPU: {0:N2} % of all cores' -f $cpu
if ($average -lt 60 -and $cpu -lt 1) { 'PASS (targets: < 60 MB working set, < 1 % CPU)' }
else { 'FAIL (targets: < 60 MB working set, < 1 % CPU)' }
