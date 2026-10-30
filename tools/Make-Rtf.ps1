# Convertit installer\CLUF.txt (UTF-8) en RTF pour le dialogue de licence de l'assistant MSI (accents en \uN?).
param([Parameter(Mandatory)][string]$Out)
$root = Split-Path $PSScriptRoot -Parent
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 ')
$text = [IO.File]::ReadAllText("$root\installer\CLUF.txt", [Text.Encoding]::UTF8).Replace("`r", '')
foreach ($ch in $text.ToCharArray()) {
    if ($ch -eq "`n") { [void]$sb.Append('\par ') }
    elseif ($ch -eq '\' -or $ch -eq '{' -or $ch -eq '}') { [void]$sb.Append('\' + $ch) }
    elseif ([int]$ch -gt 127) { [void]$sb.Append('\u' + [int]$ch + '?') }
    else { [void]$sb.Append($ch) }
}
[void]$sb.Append('}')
[IO.File]::WriteAllText($Out, $sb.ToString(), [Text.Encoding]::ASCII)
