#!/usr/bin/env pwsh

<#
.SYNOPSIS
  Runs database migrations for shop-api. Used by Azure Pipelines.

.DESCRIPTION
  Reads src/ShopApi/Data/Migrations/manifest.json. If simulateTimeout is true,
  this script emits a realistic command-timeout error and exits non-zero —
  which is what the demo agent will diagnose as a deployment failure.

  In a real production system this would shell out to `dotnet ef database
  update` or your migration tool of choice.
#>

[CmdletBinding()]
param(
    [string]$ManifestPath = "src/ShopApi/Data/Migrations/manifest.json"
)

$ErrorActionPreference = 'Stop'

Write-Host "==> Running database migrations for shop-api"
Write-Host ""

if (-not (Test-Path $ManifestPath)) {
    Write-Host "##[warning] No migration manifest found at $ManifestPath"
    Write-Host "##[warning] Nothing to do. Exiting 0."
    exit 0
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

Write-Host "Migration:        $($manifest.name)"
Write-Host "Expected runtime: $($manifest.expectedDuration)"
Write-Host "Timeout:          $($manifest.timeoutSeconds)s"
Write-Host ""

if ($manifest.simulateTimeout -eq $true) {
    # Realistic-looking output that mimics what a real ADO.NET timeout looks
    # like in the build log. The MCP server's diagnose_deployment tool reads
    # the build log and surfaces the "Command timeout expired" line.
    Write-Host "==> Applying migration $($manifest.name)..."
    Start-Sleep -Seconds 2
    Write-Host "    Executing: ALTER TABLE Orders ADD COLUMN..."
    Start-Sleep -Seconds 3
    Write-Host ""
    Write-Host "##[error] Microsoft.Data.SqlClient.SqlException (0x80131904):"
    Write-Host "##[error] Execution Timeout Expired. The timeout period elapsed prior"
    Write-Host "##[error] to completion of the operation or the server is not responding."
    Write-Host "##[error] Command timeout expired."
    Write-Host "##[error]   at line 412 in ALTER TABLE Orders ADD COLUMN..."
    Write-Host ""
    Write-Host "##[error] Migration $($manifest.name) failed after $($manifest.timeoutSeconds)s."
    exit 1
}

Write-Host "==> Migration applied successfully."
Write-Host ""
exit 0
