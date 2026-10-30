# Publie l'exe (single-file, self-contained) puis le MSI dans dist\, signés avec le certificat global TomLine.
# -Publish : crée la release GitHub (tag Va.b.c depuis <Version> du csproj) avec le MSI seul (pas d'exe portable ;
# la mise à jour automatique télécharge ce MSI).
# Pour changer de version : modifier <Version> dans StreamLauncher.csproj (ex. 1.2.0 -> V1.2.0).
# Prérequis MSI : wix 4.0.x + extension Util (voir le dépôt filtres, tools\Build-Installer.ps1).
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
dotnet publish "$root\StreamLauncher.csproj" -c Release -o $dist
if ($LASTEXITCODE -ne 0) { throw 'publish echoue' }

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like 'CN=TomLine prod&co*' -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw 'certificat TomLine introuvable' }
function Sign($path) {
    $s = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    # UnknownError = racine auto-signee non approuvee sur ce poste : la signature est bien posee
    if (-not (Get-AuthenticodeSignature $path).SignerCertificate) { throw "non signe : $path" }
    "Signature : $($s.Status) - $path ($([math]::Round((Get-Item $path).Length/1MB,1)) Mo)"
}

$exe = Join-Path $dist 'StreamLauncher.exe'
Sign $exe

$v = [version](([xml](Get-Content "$root\StreamLauncher.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
$ver = '{0}.{1}.{2}' -f $v.Major, $v.Minor, [math]::Max($v.Build, 0)
$msi = Join-Path $dist "StreamLauncher-V$ver-win-x64.msi"
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (Test-Path $msi) { Remove-Item $msi -Force }
& $wix build "$root\installer\Product.wxs" -arch x64 -ext WixToolset.Util.wixext -d "Version=$ver" -d "ExePath=$exe" -o $msi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msi)) { throw 'wix build echoue' }
Sign $msi

if ($Publish) {
    gh release create "V$ver" $msi --repo Rimfire03/Stream_launcher --title "V$ver" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'gh release echoue' }
    "Release V$ver publiee"
}
