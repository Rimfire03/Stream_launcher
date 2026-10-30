# Publie l'exe portable (single-file, self-contained) dans dist\ puis le signe avec le certificat global TomLine.
# -Publish : crée la release GitHub (tag Va.b.c depuis <Version> du csproj) avec l'exe en asset ; l'app s'y met à jour.
# Pour changer de version : modifier <Version> dans StreamLauncher.csproj (ex. 1.2.0 -> V1.2.0).
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
dotnet publish "$root\StreamLauncher.csproj" -c Release -o $dist
if ($LASTEXITCODE -ne 0) { throw 'publish echoue' }

$exe = Join-Path $dist 'StreamLauncher.exe'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like 'CN=TomLine prod&co*' } | Select-Object -First 1
if (-not $cert) { throw 'certificat TomLine introuvable' }
$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
# UnknownError = racine auto-signee non approuvee sur ce poste : la signature est bien posee
"Signature : $($sig.Status) - $exe ($([math]::Round((Get-Item $exe).Length/1MB,1)) Mo)"

if ($Publish) {
    $v = [version](([xml](Get-Content "$root\StreamLauncher.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    $tag = 'V{0}.{1}.{2}' -f $v.Major, $v.Minor, [math]::Max($v.Build, 0)
    gh release create $tag $exe --repo Rimfire03/Stream_launcher --title $tag --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'gh release echoue' }
    "Release $tag publiee"
}
