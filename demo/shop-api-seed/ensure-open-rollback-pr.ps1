#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Ensures there is an OPEN rollback PR in shop-api for the demo, and prints its ID.

.DESCRIPTION
  The FakeDeploymentService returns a hardcoded PR id so the agent's reply links
  to a real, clickable PR on stage. That PR must be OPEN (active). This script:
    1. Checks the configured PR id; if it's still active, does nothing.
    2. Otherwise creates a fresh rollback branch from the last-known-good commit
       (b1e3d847 / latest green) and opens a new active PR into main.
    3. Prints the new PR id so you can update FakeDeploymentService.prId.

  Uses AAD bearer tokens (works with MSA-backed az logins).

.EXAMPLE
  ./ensure-open-rollback-pr.ps1
#>

[CmdletBinding()]
param(
    [string]$OrgName     = '<your-org>',
    [string]$ProjectName = '<your-project>',
    [string]$RepoName    = 'shop-api',
    [int]$CurrentPrId    = 2
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

$repo = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories?api-version=7.1").value |
        Where-Object { $_.name -eq $RepoName }
if (-not $repo) { throw "Repo '$RepoName' not found." }
$repoId = $repo.id
Write-Host "==> repo '$RepoName' id=$repoId"

# 1. Is the current PR still open/active?
$prOk = $false
try {
    $pr = Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/pullRequests/$CurrentPrId`?api-version=7.1"
    Write-Host "==> PR #$CurrentPrId status: $($pr.status)"
    if ($pr.status -eq 'active') { $prOk = $true }
} catch {
    Write-Host "==> PR #$CurrentPrId not found."
}

if ($prOk) {
    Write-Host ""
    Write-Host "PR #$CurrentPrId is already ACTIVE — nothing to do."
    Write-Host "URL: $orgUrl/$ProjectName/_git/$RepoName/pullrequest/$CurrentPrId"
    return
}

# 2. Find the last-known-good commit on main (most recent green commit).
#    The seed history uses 'Add tests and CI pipeline' as last-good (b1e3d847-ish).
Write-Host "==> Locating last-known-good commit on main"
$commits = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/commits?searchCriteria.itemVersion.version=main&searchCriteria.`$top=20&api-version=7.1").value
$good = $commits | Where-Object { $_.comment -match 'tests and CI pipeline' } | Select-Object -First 1
if (-not $good) { $good = $commits | Select-Object -First 1 }  # fallback: tip
$goodSha = $good.commitId
Write-Host "  last-good: $($goodSha.Substring(0,8))  '$($good.comment)'"

$mainRef = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/refs?filter=heads/main&api-version=7.1").value | Select-Object -First 1
$mainTip = $mainRef.objectId

# 3. Create a rollback branch that reverts manifest.json back to simulateTimeout=false.
$shortSha = $goodSha.Substring(0,7)
$branchName = "rollback/auto-$shortSha-$([DateTime]::UtcNow.ToString('HHmmss'))"
$manifestPath = '/src/ShopApi/Data/Migrations/manifest.json'

Write-Host "==> Reading current manifest on main and reverting simulateTimeout -> false"
$mainManifestUrl = "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/items?path=$([uri]::EscapeDataString($manifestPath))&versionDescriptor.version=main&includeContent=true&api-version=7.1"
$manifestObj = (Invoke-AzDo -Url $mainManifestUrl).content | ConvertFrom-Json
$manifestObj.simulateTimeout = $false
$goodManifest = ($manifestObj | ConvertTo-Json -Depth 20)

Write-Host "==> Creating branch '$branchName' off main tip and reverting manifest"
$pushBody = @{
    refUpdates = @(@{ name = "refs/heads/$branchName"; oldObjectId = $mainTip })
    commits = @(@{
        comment = "Rollback shop-api to $shortSha - revert CustomerLoyalty migration (automated)"
        changes = @(@{
            changeType = 'edit'
            item       = @{ path = $manifestPath }
            newContent = @{ content = $goodManifest; contentType = 'rawtext' }
        })
    })
}
$push = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/pushes?api-version=7.1" -Body $pushBody
Write-Host "  branch commit: $($push.commits[0].commitId.Substring(0,8))"

# 4. Open the PR.
Write-Host "==> Opening pull request -> main"
$prBody = @{
    sourceRefName = "refs/heads/$branchName"
    targetRefName = 'refs/heads/main'
    title = "Rollback shop-api to $shortSha - automated by agent"
    description = "The latest deployment failed: schema migration ``20260603_AddCustomerLoyalty`` timed out during ``ALTER TABLE Orders`` (exceeded the 30s pipeline timeout). Rolling back to last-known-good commit $shortSha to restore service. The migration needs batching for large tables before it can be re-applied."
}
$newPr = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/git/repositories/$repoId/pullRequests?api-version=7.1" -Body $prBody
$newPrId = $newPr.pullRequestId

Write-Host ""
Write-Host "==================== NEW OPEN PR ===================="
Write-Host "  PR id : $newPrId"
Write-Host "  Title : $($newPr.title)"
Write-Host "  URL   : $orgUrl/$ProjectName/_git/$RepoName/pullrequest/$newPrId"
Write-Host "===================================================="
Write-Host ""
Write-Host "NEXT: update FakeDeploymentService.cs -> var prId = $newPrId;"
