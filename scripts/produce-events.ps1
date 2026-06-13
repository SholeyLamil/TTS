<#
.SYNOPSIS
  Publishes simulated transaction lifecycle events to the Kafka topic the service consumes.
  Replaces the old HTTP send-events.ps1 (events now arrive via Kafka, not an API).

.EXAMPLE
  ./scripts/produce-events.ps1 -Count 100
  ./scripts/produce-events.ps1 -Count 500 -Topic transaction-events
#>
[CmdletBinding()]
param(
    [int]    $Count       = 100,                   # number of transactions to simulate
    [string] $Topic       = "transaction-events",
    [string] $Container   = "tts-kafka-1",         # kafka container name
    [double] $FailRate    = 0.12,
    [double] $ReverseRate = 0.05
)
$ErrorActionPreference = "Stop"
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    $env:Path += ";C:\Program Files\Docker\Docker\resources\bin"
}

$currencies = @("USD", "EUR", "GBP", "NGN")
$lines = New-Object System.Collections.Generic.List[string]

function New-Event([string]$type, [string]$txn, [hashtable]$data) {
    @{
        eventType      = $type
        eventTimestamp = [DateTime]::UtcNow.ToString("o")
        transactionId  = $txn
        data           = $data
    } | ConvertTo-Json -Compress
}

for ($i = 1; $i -le $Count; $i++) {
    $txn      = "TXN{0:D6}" -f (Get-Random -Minimum 1 -Maximum 999999)
    $amount   = [math]::Round((Get-Random -Minimum 100 -Maximum 500000) / 100.0, 2)
    $currency = $currencies | Get-Random

    $lines.Add((New-Event "TRANSACTION_RECEIVED"            $txn @{ amount = $amount; currency = $currency }))
    $lines.Add((New-Event "TRANSACTION_SENT_FOR_PROCESSING" $txn @{ amount = $amount; currency = $currency }))
    $lines.Add((New-Event "TRANSACTION_RESPONSE_RECEIVED"   $txn @{ amount = $amount; latencyMs = (Get-Random -Minimum 20 -Maximum 800) }))

    if ((Get-Random -Minimum 0.0 -Maximum 1.0) -lt $FailRate) {
        $lines.Add((New-Event "TRANSACTION_FAILED" $txn @{ amount = $amount; currency = $currency; reason = "processor_declined" }))
    } else {
        $lines.Add((New-Event "TRANSACTION_COMPLETED" $txn @{ amount = $amount; currency = $currency }))
        if ((Get-Random -Minimum 0.0 -Maximum 1.0) -lt $ReverseRate) {
            $lines.Add((New-Event "TRANSACTION_REVERSED" $txn @{ amount = $amount; currency = $currency; reason = "customer_dispute" }))
        }
    }
}

Write-Host "Publishing $($lines.Count) events ($Count transactions) to topic '$Topic'..." -ForegroundColor Cyan

# Pipe one JSON message per line into the broker's console producer.
$lines -join "`n" | docker exec -i $Container /opt/kafka/bin/kafka-console-producer.sh `
    --bootstrap-server localhost:9092 --topic $Topic

Write-Host "Done. Published $($lines.Count) events. The consumer will write them to InfluxDB." -ForegroundColor Green
