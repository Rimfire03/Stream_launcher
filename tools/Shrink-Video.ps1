# Reduit une video MP4 (Windows MediaTranscoder, sans ffmpeg) pour passer sous la limite d'envoi des outils (10 Mo).
param([Parameter(Mandatory)][string]$In, [Parameter(Mandatory)][string]$Out, [int]$VideoKbps = 1200, [int]$AudioKbps = 64)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.Media.Transcoding.MediaTranscoder, Windows.Media.Transcoding, ContentType = WindowsRuntime]
[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media.MediaProperties, ContentType = WindowsRuntime]

$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$type) {
    $t = $asTaskGeneric.MakeGenericMethod($type).Invoke($null, @($op))
    $t.Wait(-1) | Out-Null
    $t.Result
}
$asTaskAction = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncActionWithProgress`1' })[0]

$src = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync((Resolve-Path -LiteralPath $In).Path)) ([Windows.Storage.StorageFile])
if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }
New-Item -ItemType File -Path $Out | Out-Null
$dst = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync((Resolve-Path -LiteralPath $Out).Path)) ([Windows.Storage.StorageFile])

$profile = [Windows.Media.MediaProperties.MediaEncodingProfile]::CreateMp4([Windows.Media.MediaProperties.VideoEncodingQuality]::HD1080p)
$profile.Video.Bitrate = [uint32]($VideoKbps * 1000)
$profile.Audio.Bitrate = [uint32]($AudioKbps * 1000)

$tc = New-Object Windows.Media.Transcoding.MediaTranscoder
$prep = Await ($tc.PrepareFileTranscodeAsync($src, $dst, $profile)) ([Windows.Media.Transcoding.PrepareTranscodeResult])
if (-not $prep.CanTranscode) { throw "transcodage impossible : $($prep.FailureReason)" }
$task = $asTaskAction.MakeGenericMethod([double]).Invoke($null, @($prep.TranscodeAsync()))
$task.Wait(-1) | Out-Null
"{0:N1} Mo -> {1:N1} Mo" -f ((Get-Item -LiteralPath $In).Length / 1MB), ((Get-Item -LiteralPath $Out).Length / 1MB)
