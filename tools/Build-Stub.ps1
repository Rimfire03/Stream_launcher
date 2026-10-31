# Recompile assets\LaunchStub.exe (embarque dans StreamLauncher.exe, copie sous le nom de chaque lanceur Stream Deck).
$root = Split-Path $PSScriptRoot -Parent
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /win32icon:"$root\assets\app.ico" /out:"$root\assets\LaunchStub.exe" "$root\tools\stub\LaunchStub.cs"
if ($LASTEXITCODE -ne 0) { throw 'csc echoue' }
"$((Get-Item "$root\assets\LaunchStub.exe").Length) octets"
