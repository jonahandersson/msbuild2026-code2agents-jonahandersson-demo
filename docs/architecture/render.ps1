# Render Mermaid diagrams → PNG (blue/white theme)
# Usage: pwsh ./render.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Sanity check
if (-not (Get-Command mmdc -ErrorAction SilentlyContinue)) {
  Write-Error "mmdc not found. Install: npm install -g @mermaid-js/mermaid-cli"
  exit 1
}

$files = Get-ChildItem -Filter *.mmd
foreach ($f in $files) {
  $out = $f.BaseName + '.png'
  Write-Host "→ Rendering $($f.Name) → $out" -ForegroundColor Cyan
  mmdc -i $f.Name `
       -o $out `
       -c mermaid.config.json `
       -p puppeteer.config.json `
       -b white `
       -w 2400 -H 1800 `
       --scale 2
}

Write-Host "`nDone. PNGs ready for slides:" -ForegroundColor Green
Get-ChildItem *.png | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}}
