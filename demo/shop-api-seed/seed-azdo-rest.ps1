#!/usr/bin/env pwsh
<#
.SYNOPSIS
  REST-based variant of seed-azdo.ps1. Uses AAD bearer tokens directly instead
  of the `az devops` CLI extension (which fails on MSA-backed az logins).

.DESCRIPTION
  Idempotent. Creates `shop-api` repo (if missing), pushes 4 commits to build
  realistic history, creates the build pipeline, and (optionally) adds the
  Function App's managed identity to the AzDO org + Contributors group.

.EXAMPLE
  ./seed-azdo-rest.ps1 -OrgName jonahanderssonazuredemos -ProjectName msbuild2026eshopdemo `
      -MiObjectId c8fa61da-c30c-4c87-a738-fe726a735d24 -MiDisplayName 'msi-build2026-mcp-azure-functions'
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OrgName,
    [Parameter(Mandatory)][string]$ProjectName,
    [string]$RepoName = 'shop-api',
    [string]$SeedSourcePath = $PSScriptRoot,
    [string]$MiObjectId,
    [string]$MiDisplayName
)

$ErrorActionPreference = 'Stop'
$AzDoResource = '499b84ac-1321-427f-aa17-267ca6975798'

function Get-Token { az account get-access-token --resource $AzDoResource --query accessToken -o tsv }
function Invoke-AzDo {
    param([string]$Method='GET',[Parameter(Mandatory)][string]$Url,$Body,[string]$ContentType='application/json')
    $tok = Get-Token
    $h = @{ Authorization = "Bearer $tok"; Accept = 'application/json' }
    $params = @{ Method = $Method; Uri = $Url; Headers = $h }
    if ($null -ne $Body) {
        $params.ContentType = $ContentType
        if ($Body -is [string]) { $params.Body = $Body } else { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    }
    Invoke-RestMethod @params
}

$orgUrl   = "https://dev.azure.com/$OrgName"
$vsspsUrl = "https://vssps.dev.azure.com/$OrgName"

Write-Host "==> Verifying project '$ProjectName'"
$proj = Invoke-AzDo -Url "$orgUrl/_apis/projects/$ProjectName`?api-version=7.1"
Write-Host "  id: $($proj.id)"

# --- Repo ---
Write-Host "==> Ensuring repo '$RepoName'"
$repos = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories`?api-version=7.1").value
$repo  = $repos | Where-Object { $_.name -eq $RepoName }
if (-not $repo) {
    $repo = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/git/repositories`?api-version=7.1" `
        -Body @{ name = $RepoName; project = @{ id = $proj.id } }
    Write-Host "  Created. id: $($repo.id)"
} else {
    Write-Host "  Exists. id: $($repo.id)"
}
$cloneUrl = "$orgUrl/$ProjectName/_git/$RepoName"

# --- Push history (only if repo is empty) ---
$needsPush = $true
try {
    $refs = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$($repo.id)/refs`?filter=heads/main&api-version=7.1").value
    if ($refs.Count -gt 0) { $needsPush = $false; Write-Host "  main branch already exists; skipping push." }
} catch { }

$lastGoodSha = $null; $badSha = $null
if ($needsPush) {
    Write-Host "==> Building 4-commit history in temp dir"
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "shop-api-$(Get-Random)"
    New-Item -ItemType Directory -Path $workDir | Out-Null
    Push-Location $workDir
    try {
        git init -b main 2>&1 | Out-Null
        git config user.email 'demo@build2026.local'
        git config user.name  'Build 2026 Demo'

        Write-Host "  Commit 1/4: initial scaffold"
        Copy-Item "$SeedSourcePath/global.json"   "." -Force
        Copy-Item "$SeedSourcePath/.gitignore"    "." -Force
        Copy-Item "$SeedSourcePath/README.md"     "." -Force
        git add -A | Out-Null; git commit -m 'Initial scaffold' --quiet

        Write-Host "  Commit 2/4: add Web API"
        Copy-Item "$SeedSourcePath/src" "." -Recurse -Force
        Remove-Item 'src/ShopApi/Data/Migrations/manifest.json' -ErrorAction SilentlyContinue
        Remove-Item 'src/ShopApi/Data/Migrations/20260515_AddCustomerLoyalty.cs' -ErrorAction SilentlyContinue
        git add -A | Out-Null; git commit -m 'Add Web API with Customer + Order models' --quiet

        Write-Host "  Commit 3/4: add tests + pipeline (last-known-good)"
        Copy-Item "$SeedSourcePath/tests"               "." -Recurse -Force
        Copy-Item "$SeedSourcePath/azure-pipelines.yml" "." -Force
        Copy-Item "$SeedSourcePath/ShopApi.slnx"        "." -Force
        Copy-Item "$SeedSourcePath/scripts"             "." -Recurse -Force
        git add -A | Out-Null; git commit -m 'Add tests and CI pipeline' --quiet
        $lastGoodSha = (git rev-parse HEAD).Trim()
        Write-Host "  last-good: $lastGoodSha"

        Write-Host "  Commit 4/4: bad CustomerLoyalty migration"
        Copy-Item "$SeedSourcePath/src/ShopApi/Data/Migrations/20260515_AddCustomerLoyalty.cs" 'src/ShopApi/Data/Migrations/' -Force
        Copy-Item "$SeedSourcePath/src/ShopApi/Data/Migrations/manifest.json"                  'src/ShopApi/Data/Migrations/' -Force
        git add -A | Out-Null; git commit -m 'Add CustomerLoyalty migration' --quiet
        $badSha = (git rev-parse HEAD).Trim()
        Write-Host "  bad: $badSha"

        Write-Host "==> Pushing to AzDO using bearer-token http.extraHeader"
        $tok = Get-Token
        $hdr = "Authorization: Bearer $tok"
        git remote add origin $cloneUrl
        git -c http.extraHeader="$hdr" push origin main 2>&1 | Write-Host
    } finally {
        Pop-Location
        Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- Pipeline ---
Write-Host ""
Write-Host "==> Ensuring pipeline '$RepoName-CI'"
$pipelineName = "$RepoName-CI"
$pipelines = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/pipelines`?api-version=7.1").value
$pipeline = $pipelines | Where-Object { $_.name -eq $pipelineName }
if (-not $pipeline) {
    $body = @{
        name = $pipelineName
        folder = '\\'
        configuration = @{
            type = 'yaml'
            path = 'azure-pipelines.yml'
            repository = @{
                id = $repo.id
                name = $RepoName
                type = 'azureReposGit'
            }
        }
    }
    $pipeline = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/pipelines`?api-version=7.1" -Body $body
    Write-Host "  Created. id: $($pipeline.id)"
} else {
    Write-Host "  Exists. id: $($pipeline.id)"
}

# --- ShopWeb release pipeline ---
# The repo already contains src/ShopWeb (pushed in commit 2/4). We ship the
# pipeline YAML separately so it can be added to an existing repo idempotently.

function Ensure-FileOnMain {
    param(
        [Parameter(Mandatory)][string]$RepoId,
        [Parameter(Mandatory)][string]$Path,        # e.g. '/azure-pipelines-shopweb.yml'
        [Parameter(Mandatory)][string]$LocalFile,
        [Parameter(Mandatory)][string]$CommitMessage
    )

    # Does the file already exist on main?
    $exists = $true
    try {
        Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$RepoId/items`?path=$([uri]::EscapeDataString($Path))&versionDescriptor.version=main&api-version=7.1" | Out-Null
    } catch { $exists = $false }
    if ($exists) { Write-Host "  $Path already on main; skipping commit."; return }

    # Need the current tip of main for the push's oldObjectId.
    $mainRef = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/git/repositories/$RepoId/refs`?filter=heads/main&api-version=7.1").value | Select-Object -First 1
    if (-not $mainRef) { throw "main ref not found for repo $RepoId" }

    $content = Get-Content -LiteralPath $LocalFile -Raw
    $pushBody = @{
        refUpdates = @(@{ name = 'refs/heads/main'; oldObjectId = $mainRef.objectId })
        commits = @(@{
            comment = $CommitMessage
            changes = @(@{
                changeType = 'add'
                item       = @{ path = $Path }
                newContent = @{ content = $content; contentType = 'rawtext' }
            })
        })
    }
    $push = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/git/repositories/$RepoId/pushes`?api-version=7.1" -Body $pushBody
    Write-Host "  Pushed $Path (commit $($push.commits[0].commitId.Substring(0,8)))"
}

Write-Host ""
Write-Host "==> Ensuring ShopWeb pipeline YAML on main"
Ensure-FileOnMain -RepoId $repo.id `
    -Path '/azure-pipelines-shopweb.yml' `
    -LocalFile (Join-Path $SeedSourcePath 'azure-pipelines-shopweb.yml') `
    -CommitMessage 'Add ShopWeb release pipeline'

Write-Host ""
Write-Host "==> Ensuring pipeline 'shop-web-CD'"
$webPipelineName = 'shop-web-CD'
$pipelines = (Invoke-AzDo -Url "$orgUrl/$ProjectName/_apis/pipelines`?api-version=7.1").value
$webPipeline = $pipelines | Where-Object { $_.name -eq $webPipelineName }
if (-not $webPipeline) {
    $body = @{
        name = $webPipelineName
        folder = '\\'
        configuration = @{
            type = 'yaml'
            path = 'azure-pipelines-shopweb.yml'
            repository = @{
                id = $repo.id
                name = $RepoName
                type = 'azureReposGit'
            }
        }
    }
    $webPipeline = Invoke-AzDo -Method POST -Url "$orgUrl/$ProjectName/_apis/pipelines`?api-version=7.1" -Body $body
    Write-Host "  Created. id: $($webPipeline.id)"
} else {
    Write-Host "  Exists. id: $($webPipeline.id)"
}
Write-Host "  NOTE: First run will fail until you create the 'azure-shopweb' service connection."
Write-Host "        See header of azure-pipelines-shopweb.yml for one-time setup."

# --- Managed Identity (optional) ---
if ($MiObjectId -and $MiDisplayName) {
    Write-Host ""
    Write-Host "==> Ensuring MI '$MiDisplayName' is in the AzDO org"
    $allUsers = (Invoke-AzDo -Url "$vsspsUrl/_apis/graph/users`?api-version=7.1-preview.1").value
    $miUser = $allUsers | Where-Object { $_.originId -eq $MiObjectId }
    if (-not $miUser) {
        # add as service principal
        $sp = Invoke-AzDo -Method POST -Url "$vsspsUrl/_apis/graph/serviceprincipals`?api-version=7.1-preview.1" `
            -Body @{ originId = $MiObjectId }
        $miUser = $sp
        Write-Host "  Added. descriptor: $($sp.descriptor)"
    } else {
        Write-Host "  Already in org. descriptor: $($miUser.descriptor)"
    }

    Write-Host "==> Adding MI to '$ProjectName Contributors' group"
    $groups = (Invoke-AzDo -Url "$vsspsUrl/_apis/graph/groups`?api-version=7.1-preview.1&scopeDescriptor=$($proj.descriptor)").value
    if (-not $groups) {
        # fallback: search all groups by displayName
        $groups = (Invoke-AzDo -Url "$vsspsUrl/_apis/graph/groups`?api-version=7.1-preview.1").value
    }
    $contribGroup = $groups | Where-Object { $_.displayName -eq 'Contributors' -and $_.principalName -match [regex]::Escape("[$ProjectName]") }
    if (-not $contribGroup) { $contribGroup = $groups | Where-Object { $_.principalName -eq "[$ProjectName]\\Contributors" } }
    if (-not $contribGroup) {
        Write-Warning "  Couldn't find Contributors group for project. Listing candidates:"
        $groups | Where-Object { $_.displayName -eq 'Contributors' } | Select-Object displayName, principalName | Format-Table
    } else {
        try {
            Invoke-AzDo -Method PUT -Url "$vsspsUrl/_apis/graph/memberships/$($miUser.descriptor)/$($contribGroup.descriptor)`?api-version=7.1-preview.1" | Out-Null
            Write-Host "  Added to $($contribGroup.principalName)."
        } catch {
            Write-Host "  Membership PUT: $($_.Exception.Message)"
        }
    }
}

Write-Host ""
Write-Host "============================================================"
Write-Host " Done."
Write-Host "============================================================"
Write-Host " Repo:     $cloneUrl"
Write-Host " Pipeline: $orgUrl/$ProjectName/_build`?definitionId=$($pipeline.id)"
if ($lastGoodSha) { Write-Host " last-good SHA: $lastGoodSha" }
if ($badSha)      { Write-Host " bad SHA:       $badSha" }
