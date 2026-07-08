#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Produces a REAL failed build for shop-api in Azure DevOps for the demo.

.DESCRIPTION
  1. Sets simulateTimeout=true in Data/Migrations/manifest.json on main (one commit).
  2. Queues the shop-api-CI pipeline.
  3. Waits for the run to finish and prints the build URL + result.

  The migration step fails with a realistic "command timeout expired" message,
  which is exactly what the agent's diagnose tool reports. The agent's rollback
  PR reverts this manifest back to simulateTimeout=false.

  Uses AAD bearer tokens (works with MSA-backed az logins; az devops CLI does not).

.EXAMPLE
  ./trigger-failed-build.ps1
#>

[CmdletBinding()]
param(
    [string]$OrgName     = '<your-org>',
    [string]$ProjectName = '<your-project>',
    [string]$RepoName    = 'shop-api',
    [string]$PipelineName = 'shop-api-CI',
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'
$AzDoResource = '499b84ac-1321-427f-aa17-267ca6975798'

function Get-Token { az account get-access-token --resource $AzDoResource --query accessToken -o tsv }
function Invoke-AzDo {
    param([string]$Method='GET',[Parameter(Mandatory)][string]$Url,$Body)
    $tok = Get-Token
    $h = @{ Authorization = "Bearer $tok"; Accept = 'application/json' }
    $params = @{ Method = $Method; Uri = $Url; Headers = $h }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }
    Invoke-RestMethod @params
}

$orgUrl = "https://dev.azure.com/$OrgName"

Write-Host "==> Resolving repo '$RepoName'"
$repo = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories?api-version=7.1").value |
        Where-Object { $_.name -eq $RepoName }
if (-not $repo) { throw "Repo '$RepoName' not found." }
Write-Host "  repoId: $($repo.id)"

Write-Host "==> Resolving pipeline '$PipelineName'"
$pipeline = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/pipelines?api-version=7.1").value |
            Where-Object { $_.name -eq $PipelineName }
if (-not $pipeline) { throw "Pipeline '$PipelineName' not found." }
Write-Host "  pipelineId: $($pipeline.id)"

# --- Read current manifest.json on main ---
$manifestPath = '/src/ShopApi/Data/Migrations/manifest.json'
Write-Host "==> Reading $manifestPath on main"
$itemUrl = "$orgUrl/$ProjectName/_apis/git/repositories/$($repo.id)/items?path=$([uri]::EscapeDataString($manifestPath))&versionDescriptor.version=main&includeContent=true&api-version=7.1"
$item = Invoke-AzDo -Url $itemUrl
$manifest = $item.content | ConvertFrom-Json
Write-Host "  current simulateTimeout: $($manifest.simulateTimeout)"

if ($manifest.simulateTimeout -ne $true) {
    Write-Host "==> Flipping simulateTimeout -> true and pushing to main"
    $manifest.simulateTimeout = $true
    $newContent = ($manifest | ConvertTo-Json -Depth 20)

    $mainRef = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$($repo.id)/refs?filter=heads/main&api-version=7.1").value | Select-Object -First 1
    $pushBody = @{
        refUpdates = @(@{ name = 'refs/heads/main'; oldObjectId = $mainRef.objectId })
        commits = @(@{
            comment = 'Trigger CustomerLoyalty migration timeout (demo failure)'
            changes = @(@{
                changeType = 'edit'
                item       = @{ path = $manifestPath }
                newContent = @{ content = $newContent; contentType = 'rawtext' }
            })
        })
    }
    $push = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/git/repositories/$($repo.id)/pushes?api-version=7.1" -Body $pushBody
    $badSha = $push.commits[0].commitId
    Write-Host "  pushed commit: $($badSha.Substring(0,8))"
} else {
    Write-Host "  Already true — manifest will fail as-is."
    $badSha = $null
}

# --- Queue the pipeline ---
Write-Host "==> Queuing pipeline run on main"
$runBody = @{ resources = @{ repositories = @{ self = @{ refName = 'refs/heads/main' } } } }
$run = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/pipelines/$($pipeline.id)/runs?api-version=7.1" -Body $runBody
$runId = $run.id
$runUrl = "$orgUrl/$ProjectName/_build/results?buildId=$runId"
Write-Host "  runId: $runId"
Write-Host "  URL:   $runUrl"

if ($NoWait) { return }

Write-Host "==> Waiting for run to finish..."
$deadline = (Get-Date).AddMinutes(8)
do {
    Start-Sleep -Seconds 12
    $status = Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/pipelines/$($pipeline.id)/runs/$runId`?api-version=7.1"
    Write-Host "  state=$($status.state) result=$($status.result)"
} while ($status.state -ne 'completed' -and (Get-Date) -lt $deadline)

Write-Host ""
Write-Host "==================== RESULT ===================="
Write-Host "  Pipeline : $PipelineName (run $runId)"
Write-Host "  State    : $($status.state)"
Write-Host "  Result   : $($status.result)"
Write-Host "  URL      : $runUrl"
if ($badSha) { Write-Host "  Bad SHA  : $($badSha.Substring(0,8))" }
Write-Host "================================================"
