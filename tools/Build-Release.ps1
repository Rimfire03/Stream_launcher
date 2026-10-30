# Publie l'exe (single-file, self-contained) puis le MSI dans dist\, signés avec le certificat global TomLine.
# -Publish : crée la release GitHub (tag Va.b.c depuis <Version> du csproj) avec le MSI seul (pas d'exe portable ;
# la mise à jour automatique télécharge ce MSI).
# Pour changer de version : modifier <Version> dans StreamLauncher.csproj (ex. 1.2.0 -> V1.2.0).
# Prérequis MSI : wix 4.0.6 + extensions Util et UI (wix extension add -g WixToolset.UI.wixext/4.0.6 ; voir aussi le dépôt filtres).
param([switch]$Publish, [switch]$Dev)
$ErrorActionPreference = 'Stop'
# canal : main = release stable ; -Dev = pre-release (tag Va.b.c-dev) pour les testeurs du canal dev, depuis la branche dev
$branch = (git -C (Split-Path $PSScriptRoot -Parent) rev-parse --abbrev-ref HEAD).Trim()
if ($Publish -and $Dev -and $branch -ne 'dev') { throw "-Dev se publie depuis la branche dev (branche actuelle : $branch)" }
if ($Publish -and -not $Dev -and $branch -ne 'main') { throw "release stable depuis main (branche actuelle : $branch ; utiliser -Dev pour le canal dev)" }
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
# Identifiants Kick : lus dans le depot prive TomLine-signing-keys (kick/Stream-Launcher-Kick.md), ecrits dans KickSecrets.g.cs
# (ignore par git) le temps du build seulement ; ils ne sont jamais commites dans ce depot.
$kickFile = Join-Path $root 'KickSecrets.g.cs'
try {
    $b64 = gh api repos/Rimfire03/TomLine-signing-keys/contents/kick/Stream-Launcher-Kick.md --jq .content
    if ($LASTEXITCODE -ne 0) { throw 'lecture du depot TomLine-signing-keys impossible (gh auth ?)' }
    $md = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($b64 -join '')))
    $kid = [regex]::Match($md, 'Identifiant client \| `([^`]+)`').Groups[1].Value
    $ksecret = [regex]::Match($md, 'Secret client \| `([^`]+)`').Groups[1].Value
    if (-not $kid -or -not $ksecret) { throw 'identifiants Kick introuvables dans le depot prive' }
    # Identifiants YouTube / Google (youtube/Stream-Launcher-YouTube.md, meme depot prive)
    $b64y = gh api repos/Rimfire03/TomLine-signing-keys/contents/youtube/Stream-Launcher-YouTube.md --jq .content
    if ($LASTEXITCODE -ne 0) { throw 'lecture des identifiants YouTube impossible (depot prive)' }
    $mdy = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($b64y -join '')))
    $yid = [regex]::Match($mdy, 'Identifiant client \| `([^`]+)`').Groups[1].Value
    $ysecret = [regex]::Match($mdy, 'Secret client \| `([^`]+)`').Groups[1].Value
    if (-not $yid -or -not $ysecret) { throw 'identifiants YouTube introuvables dans le depot prive' }
    Set-Content $kickFile -Encoding utf8 -Value "static partial class BuiltIn { static partial void Load() { KickId = `"$kid`"; KickSecret = `"$ksecret`"; YouTubeId = `"$yid`"; YouTubeSecret = `"$ysecret`"; } }"
    dotnet publish "$root\StreamLauncher.csproj" -c Release -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'publish echoue' }
} finally {
    Remove-Item $kickFile -Force -ErrorAction SilentlyContinue
}

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like 'CN=TomLine prod&co*' -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw 'certificat TomLine introuvable' }
function Sign($path) {
    $s = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    # UnknownError = racine auto-signee non approuvee sur ce poste : la signature est bien posee
    if (-not (Get-AuthenticodeSignature $path).SignerCertificate) { throw "non signe : $path" }
    "Signature : $($s.Status) - $path ($([math]::Round((Get-Item $path).Length/1MB,1)) Mo)"
}

# CLUF affiché par l'assistant MSI (installer\CLUF.txt -> RTF)
$rtf = Join-Path $dist 'CLUF.rtf'
& "$PSScriptRoot\Make-Rtf.ps1" -Out $rtf

$exe = Join-Path $dist 'StreamLauncher.exe'
Sign $exe

$v = [version](([xml](Get-Content "$root\StreamLauncher.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
$ver = '{0}.{1}.{2}' -f $v.Major, $v.Minor, [math]::Max($v.Build, 0)
$msi = Join-Path $dist "StreamLauncher-V$ver-win-x64.msi"
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (Test-Path $msi) { Remove-Item $msi -Force }
& $wix build "$root\installer\Product.wxs" -arch x64 -ext WixToolset.Util.wixext -ext WixToolset.UI.wixext -culture fr-FR -d "Version=$ver" -d "ExePath=$exe" -d "IconPath=$root\assets\app.ico" -d "LicenseRtf=$rtf" -o $msi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msi)) { throw 'wix build echoue' }
Sign $msi

if ($Publish) {
    if ($Dev) { gh release create "V$ver-dev" $msi --repo Rimfire03/Stream_launcher --title "V$ver (dev)" --generate-notes --prerelease --target dev }
    else { gh release create "V$ver" $msi --repo Rimfire03/Stream_launcher --title "V$ver" --generate-notes }
    if ($LASTEXITCODE -ne 0) { throw 'gh release echoue' }
    "Release V$ver$(if ($Dev) { '-dev' }) publiee"
}
