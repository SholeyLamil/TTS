<#
.SYNOPSIS
  Trial generator for the Transaction Telemetry Service.
  Simulates realistic transaction lifecycles and reports API latency + acceptance.

.EXAMPLE
  ./scripts/send-events.ps1 -Count 200
  ./scripts/send-events.ps1 -Count 50 -DelayMs 100 -ApiKey dev-local-key
#>
[CmdletBinding()]
param(
    [int]    $Count   = 100,                       # number of transactions to simulate
    [string] $BaseUrl = "http://localhost:8080",
    [string] $ApiKey  = "dev-local-key",
    [int]    $DelayMs = 0,                          # pause between HTTP calls (throttle)
    [double] $FailRate    = 0.12,                   # fraction of transactions that fail
    [double] $ReverseRate = 0.05                    # fraction that get reversed
)

$ErrorActionPreference = "Stop"
$endpoint   = "$BaseUrl/api/events"
$headers    = @{ "X-API-Key" = $ApiKey }
$currencies = @("USD", "EUR", "GBP", "NGN")
$channels   = @("card", "transfer", "ussd", "wallet")

$latencies = New-Object System.Collections.Generic.List[double]
$accepted  = 0
$rejected  = 0
$totalEvents = 0

function Send-Event {
    param([string]$EventType, [string]$TxnId, [hashtable]$Data)
    $payload = @{
        eventType      = $EventType
        eventTimestamp = [DateTime]::UtcNow.ToString("o")   # NOW, so it lands in default Grafana range
        transactionId  = $TxnId
        data           = $Data
    } | ConvertTo-Json -Compress

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers `
                -Body $payload -ContentType "application/json" -UseBasicParsing
        $sw.Stop()
        $script:latencies.Add($sw.Elapsed.TotalMilliseconds)
        if ([int]$r.StatusCode -eq 202) { $script:accepted++ } else { $script:rejected++ }
    } catch {
        $sw.Stop()
        $script:rejected++
    }
    $script:totalEvents++
    if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }
}

Write-Host "Sending $Count transaction lifecycles to $endpoint ..." -ForegroundColor Cyan
$start = [DateTime]::UtcNow

for ($i = 1; $i -le $Count; $i++) {
    $txn      = "TXN{0:D6}" -f (Get-Random -Minimum 1 -Maximum 999999)
    $amount   = [math]::Round((Get-Random -Minimum 100 -Maximum 500000) / 100.0, 2)
    $currency = $currencies | Get-Random
    $channel  = $channels   | Get-Random
    $roll     = Get-Random -Minimum 0.0 -Maximum 1.0

    # Lifecycle: received -> sent -> response -> (completed | failed) -> [reversed]
    Send-Event "TRANSACTION_RECEIVED"            $txn @{ amount = $amount; currency = $currency; channel = $channel }
    Send-Event "TRANSACTION_SENT_FOR_PROCESSING" $txn @{ amount = $amount; currency = $currency }
    Send-Event "TRANSACTION_RESPONSE_RECEIVED"   $txn @{ amount = $amount; latencyMs = (Get-Random -Minimum 20 -Maximum 800) }

    if ($roll -lt $FailRate) {
        Send-Event "TRANSACTION_FAILED" $txn @{ amount = $amount; currency = $currency; reason = "processor_declined" }
    } else {
        Send-Event "TRANSACTION_COMPLETED" $txn @{ amount = $amount; currency = $currency }
        if ((Get-Random -Minimum 0.0 -Maximum 1.0) -lt $ReverseRate) {
            Send-Event "TRANSACTION_REVERSED" $txn @{ amount = $amount; currency = $currency; reason = "customer_dispute" }
        }
    }

    if ($i % 25 -eq 0) { Write-Host "  $i / $Count transactions sent" }
}

$wall = ([DateTime]::UtcNow - $start).TotalSeconds
$sorted = $latencies | Sort-Object
function Pct([double]$p) { if ($sorted.Count -eq 0) { return 0 }; return [math]::Round($sorted[[int][math]::Floor($p * ($sorted.Count - 1))], 1) }

Write-Host ""
Write-Host "===== TRIAL REPORT =====" -ForegroundColor Green
Write-Host ("Transactions simulated : {0}" -f $Count)
Write-Host ("Events sent            : {0}" -f $totalEvents)
Write-Host ("Accepted (202)         : {0}" -f $accepted)
Write-Host ("Rejected / errors      : {0}" -f $rejected)
Write-Host ("Wall-clock time        : {0:N1} s" -f $wall)
Write-Host ("Throughput             : {0:N1} events/sec" -f ($totalEvents / [math]::Max($wall, 0.001)))
Write-Host "--- API response latency (ms) ---"
Write-Host ("  avg : {0:N1}" -f (($latencies | Measure-Object -Average).Average))
Write-Host ("  p50 : {0}" -f (Pct 0.50))
Write-Host ("  p95 : {0}" -f (Pct 0.95))
Write-Host ("  max : {0}" -f (Pct 1.00))
Write-Host "=========================" -ForegroundColor Green
