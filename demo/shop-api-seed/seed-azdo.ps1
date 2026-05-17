#!/usr/bin/env pwsh

<#
.SYNOPSIS
  Seeds an Azure DevOps project + repo + pipeline for the Build 2026 demo.

.DESCRIPTION
  Run this AFTER you've created the Azure DevOps organization manually
  (the CLI cannot create orgs — that's a portal-only operation).

  What this script does:
    1. Creates the project "Shop"
    2. Creates the repo "shop-api"
    3. Pushes the seed code to it (initial commit, then more commits to build
       realistic history)
    4. Creates the build pipeline
    5. Runs the pipeline a few times to build up history
    6. Pushes the "bad" commit that triggers the deliberate failure

.PARAMETER OrgUrl
  Your Azure DevOps organization URL.
  e.g. https://dev.azure.com/your-org

.PARAMETER SeedSourcePath
  Path to the shop-api-seed folder (this folder, by default).

.EXAMPLE
  ./seed-azdo.ps1 -OrgUrl https://dev.azure.com/contoso
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OrgUrl,
    [string]$ProjectName = 'Shop',
    [string]$RepoName    = 'shop-api',
    [string]$SeedSourcePath = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

# --- Pre-flight checks ---
Write-Host "==> Pre-flight checks"

$az = Get-Command az -ErrorAction SilentlyContinue
if (-not $az) {
    throw "Azure CLI ('az') not found. Install from https://aka.ms/installazurecli"
}

$devopsExt = az extension list --query "[?name=='azure-devops'].name" -o tsv
if (-not $devopsExt) {
    Write-Host "Installing azure-devops extension..."
    az extension add --name azure-devops
}

# Make sure we have a session
$account = az account show -o tsv --query 'user.name' 2>$null
if (-not $account) {
    Write-Host "Not signed in. Running 'az login'..."
    az login
}

az devops configure --defaults organization=$OrgUrl

Write-Host "  Signed in as: $account"
Write-Host "  Target org:   $OrgUrl"
Write-Host ""

# --- 1. Create project ---
Write-Host "==> Creating project '$ProjectName'"
$existingProject = az devops project show --project $ProjectName --query 'id' -o tsv 2>$null
if ($existingProject) {
    Write-Host "  Project already exists (id: $existingProject). Skipping creation."
} else {
    az devops project create `
        --name $ProjectName `
        --description "Demo project for Build 2026 MCP talk" `
        --visibility private `
        --source-control git `
        --process Agile `
        | Out-Null
    Write-Host "  Created."
}

az devops configure --defaults project=$ProjectName

# --- 2. Create or get the repo ---
Write-Host "==> Creating repo '$RepoName'"
$repoId = az repos show --repository $RepoName --query 'id' -o tsv 2>$null
if (-not $repoId) {
    $repoJson = az repos create --name $RepoName -o json | ConvertFrom-Json
    $repoId = $repoJson.id
    Write-Host "  Created. Repo id: $repoId"
} else {
    Write-Host "  Repo already exists (id: $repoId). Will push to existing."
}
$repoCloneUrl = "$OrgUrl/$ProjectName/_git/$RepoName"
Write-Host "  Clone URL: $repoCloneUrl"

# --- 3. Push seed code with multiple commits for realistic history ---
Write-Host ""
Write-Host "==> Building realistic git history"

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "shop-api-$(Get-Random)"
New-Item -ItemType Directory -Path $workDir | Out-Null
Push-Location $workDir

try {
    git init -b main | Out-Null
    git config user.email "demo@build2026.local"
    git config user.name "Build 2026 Demo"

    # Commit 1: minimal scaffold (just project files)
    Write-Host "  Commit 1/4: initial scaffold"
    Copy-Item -Path "$SeedSourcePath/global.json" -Destination "."
    Copy-Item -Path "$SeedSourcePath/.gitignore" -Destination "."
    Copy-Item -Path "$SeedSourcePath/README.md" -Destination "."
    git add -A | Out-Null
    git commit -m "Initial scaffold" --quiet

    # Commit 2: add the API
    Write-Host "  Commit 2/4: add Web API"
    Copy-Item -Path "$SeedSourcePath/src" -Destination "." -Recurse
    # Remove the migration's manifest for now — that's commit 4
    Remove-Item "src/ShopApi/Data/Migrations/manifest.json" -ErrorAction SilentlyContinue
    Remove-Item "src/ShopApi/Data/Migrations/20260515_AddCustomerLoyalty.cs" -ErrorAction SilentlyContinue
    git add -A | Out-Null
    git commit -m "Add Web API with Customer + Order models" --quiet

    # Commit 3: add tests + pipeline (this is the LAST KNOWN GOOD COMMIT)
    Write-Host "  Commit 3/4: add tests + pipeline (will be last-known-good)"
    Copy-Item -Path "$SeedSourcePath/tests" -Destination "." -Recurse
    Copy-Item -Path "$SeedSourcePath/azure-pipelines.yml" -Destination "."
    Copy-Item -Path "$SeedSourcePath/scripts" -Destination "." -Recurse
    git add -A | Out-Null
    git commit -m "Add tests and CI pipeline" --quiet
    $lastGoodSha = git rev-parse HEAD
    Write-Host "  Last known good SHA: $lastGoodSha"

    # Commit 4: the BAD migration (with simulateTimeout=true)
    Write-Host "  Commit 4/4: add CustomerLoyalty migration (will fail in CI)"
    Copy-Item -Path "$SeedSourcePath/src/ShopApi/Data/Migrations/20260515_AddCustomerLoyalty.cs" `
        -Destination "src/ShopApi/Data/Migrations/"
    Copy-Item -Path "$SeedSourcePath/src/ShopApi/Data/Migrations/manifest.json" `
        -Destination "src/ShopApi/Data/Migrations/"
    git add -A | Out-Null
    git commit -m "Add CustomerLoyalty migration" --quiet
    $badSha = git rev-parse HEAD
    Write-Host "  Bad commit SHA: $badSha"

    # Push to AzDO
    Write-Host ""
    Write-Host "==> Pushing to Azure DevOps"
    git remote add origin $repoCloneUrl
    git push origin main --quiet
}
finally {
    Pop-Location
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- 4. Create the build pipeline ---
Write-Host ""
Write-Host "==> Creating build pipeline"
$pipelineName = "$RepoName-CI"
$existingPipeline = az pipelines show --name $pipelineName --query 'id' -o tsv 2>$null
if ($existingPipeline) {
    Write-Host "  Pipeline already exists (id: $existingPipeline)."
} else {
    az pipelines create `
        --name $pipelineName `
        --repository $RepoName `
        --repository-type tfsgit `
        --branch main `
        --yaml-path azure-pipelines.yml `
        --skip-first-run `
        | Out-Null
    Write-Host "  Created pipeline '$pipelineName'."
}

# --- 5. Summary ---
Write-Host ""
Write-Host "============================================================"
Write-Host " Done."
Write-Host "============================================================"
Write-Host ""
Write-Host " Repo URL:       $repoCloneUrl"
Write-Host " Pipeline:       $OrgUrl/$ProjectName/_build"
Write-Host " Last good SHA:  $lastGoodSha"
Write-Host " Bad SHA:        $badSha"
Write-Host ""
Write-Host " Next steps:"
Write-Host "   1. In the AzDO portal, open the pipeline and run it."
Write-Host "      The first run (HEAD = bad commit) WILL FAIL — that's the point."
Write-Host "   2. Run it 1-2 more times so you have a few 'recent failures'."
Write-Host "   3. Add the Function App's Managed Identity as a user with"
Write-Host "      Contributor on this project (see SETUP-AZDO.md)."
Write-Host ""
