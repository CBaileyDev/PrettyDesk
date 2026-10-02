<#
.SYNOPSIS
  Samples PrettyDesk's CPU and memory for a soak test (SPEC NFR-1, NFR-2) and writes a CSV plus a summary.

.DESCRIPTION
  Run it while PrettyDesk sits in the tray with its window closed. Defaults: 10 minutes at one sample every 5 seconds (NFR-1);
  pass -Hours 24 for the soak (NFR-2: no growth over 24 h). Only reads performance counters for the PrettyDesk process: nothing
  is installed, nothing leaves the machine, and no process list is recorded.

.EXAMPLE
  .\soak.ps1                          # 10 minute idle CPU/memory sample
  .\soak.ps1 -Hours 24 -OutFile soak24h.csv
#>
param(
  [double]$Hours = 0,
  [int]$Minutes = 10,
  [int]$IntervalSeconds = 5,
  [string]$ProcessName = 'PrettyDesk',
  [int]$ProcessId = 0,
  [string]$OutFile = "soak-$(Get-Date -Format 'yyyyMMdd-HHmm').csv"
)

$durationSeconds = if ($Hours -gt 0) { [int]($Hours * 3600) } else { $Minutes * 60 }
$cores = [Environment]::ProcessorCount
$samples = [System.Collections.Generic.List[object]]::new()
$started = Get-Date

$proc = if ($ProcessId -gt 0) { Get-Process -Id $ProcessId -ErrorAction SilentlyContinue } else { Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $proc) { throw "PrettyDesk is not running. Start it (window closed, in the tray) and run this again." }
Write-Host "Sampling PID $($proc.Id) every $IntervalSeconds s for $([math]::Round($durationSeconds / 60)) min on $cores logical cores..."

$lastCpu = $proc.TotalProcessorTime
$lastTime = Get-Date
while (((Get-Date) - $started).TotalSeconds -lt $durationSeconds) {
  Start-Sleep -Seconds $IntervalSeconds
  $proc.Refresh()
  if ($proc.HasExited) { throw "PrettyDesk exited during the soak at $(Get-Date -Format o)." }
  $now = Get-Date
  $cpu = $proc.TotalProcessorTime
  $cpuPercent = (($cpu - $lastCpu).TotalSeconds / (($now - $lastTime).TotalSeconds * $cores)) * 100
  $lastCpu = $cpu; $lastTime = $now
  $sample = [pscustomobject]@{
    Time = $now.ToString('o'); CpuPercent = [math]::Round($cpuPercent, 3)
    PrivateMB = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1); WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    Handles = $proc.HandleCount; Threads = $proc.Threads.Count
  }
  $samples.Add($sample)
  if ($samples.Count -eq 1) { $sample | Export-Csv -NoTypeInformation -Path $OutFile }
  else { $sample | Export-Csv -NoTypeInformation -Append -Path $OutFile }
}
$summary = [pscustomobject]@{
  Completed = $true; DurationSeconds = ((Get-Date) - $started).TotalSeconds
  ProcessId = $proc.Id; LogicalCores = $cores; Samples = $samples.Count
  CpuAveragePercent = ($samples | Measure-Object CpuPercent -Average).Average
  CpuPeakPercent = ($samples | Measure-Object CpuPercent -Maximum).Maximum
  PrivateMBPeak = ($samples | Measure-Object PrivateMB -Maximum).Maximum
}
$summary | ConvertTo-Json | Set-Content "$OutFile.summary.json"

$cpuAvg = ($samples | Measure-Object CpuPercent -Average).Average
$cpuMax = ($samples | Measure-Object CpuPercent -Maximum).Maximum
$first = $samples | Select-Object -First ([math]::Max(1, [int]($samples.Count * 0.1)))
$last = $samples | Select-Object -Last ([math]::Max(1, [int]($samples.Count * 0.1)))
$memStart = ($first | Measure-Object PrivateMB -Average).Average
$memEnd = ($last | Measure-Object PrivateMB -Average).Average
$memMax = ($samples | Measure-Object PrivateMB -Maximum).Maximum
$handlesGrowth = ($last | Measure-Object Handles -Average).Average - ($first | Measure-Object Handles -Average).Average

"{0,-34} {1}" -f 'Samples', $samples.Count
"{0,-34} {1:N3} %  (NFR-1 target < 0.2 %)" -f 'CPU average', $cpuAvg
"{0,-34} {1:N3} %" -f 'CPU peak sample', $cpuMax
"{0,-34} {1:N1} MB (NFR-2 target < 80 MB)" -f 'Private bytes, peak', $memMax
"{0,-34} {1:N1} MB -> {2:N1} MB (growth {3:N1} MB)" -f 'Private bytes, first/last 10%', $memStart, $memEnd, ($memEnd - $memStart)
"{0,-34} {1:N0}" -f 'Handle growth, first/last 10%', $handlesGrowth
"Wrote $OutFile"
