<#
  CI ONLY. Do not run this on a PC you care about: it writes test events into the Application event log.

  Builds tests/samples/perf.evtx for the performance tests on a throw-away GitHub Actions runner:
  it registers a temporary event source, writes 60,000 events (IDs 1-999, mixed levels, "Disk 3" in 1% of the messages),
  exports them, then removes the source.

  On your own PC, make the perf log by exporting an existing log instead (read-only):
      wevtutil epl System tests\samples\perf.evtx
#>
$ErrorActionPreference = 'Stop'

if (-not $env:CI) {
    throw 'This script writes to the Application event log and is meant for CI runners only (CI environment variable not set).'
}

$source = 'HvViewerPerf'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output = Join-Path $root 'tests\samples\perf.evtx'
New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null

if (-not [System.Diagnostics.EventLog]::SourceExists($source)) {
    New-EventLog -LogName Application -Source $source
}

$types = 'Information', 'Warning', 'Error'
$random = [System.Random]::new(42)
for ($i = 1; $i -le 60000; $i++) {
    $id = $random.Next(1, 1000)
    $type = $types[$random.Next(0, 3)]
    $text = if ($i % 100 -eq 0) { "Retry on Disk 3 after timeout (item $i)" } else { "Routine message number $i for the performance test" }
    Write-EventLog -LogName Application -Source $source -EntryType $type -EventId $id -Message $text
}

wevtutil epl Application $output "/q:*[System[Provider[@Name='$source']]]"
Remove-EventLog -Source $source
Write-Host "Wrote $output"
