# Rebuild (publish) and restart post-kun.
# A running instance only brings itself to front on second launch, so stop it first.
$ErrorActionPreference = 'Stop'

Get-Process PostKun -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500  # wait for the exe lock to be released

dotnet publish -c Release -o (Join-Path $PSScriptRoot 'publish\PostKun') --nologo (Join-Path $PSScriptRoot 'post-kun.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process (Join-Path $PSScriptRoot 'publish\PostKun\PostKun.exe')
Write-Host 'Restarted.'
