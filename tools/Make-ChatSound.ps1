# Genere assets\chat-default.wav : petit "pop" a deux notes (PCM 16 bits, mono, 44,1 kHz), son par defaut du chat unifie.
# Cree par ce script (aucun droit d'auteur tiers) : le son de Chatty est sous GPL et n'est pas redistribue.
$ErrorActionPreference = 'Stop'
$rate = 44100
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\chat-default.wav'
$notes = @(@{ F = 660.0; Ms = 90 }, @{ F = 990.0; Ms = 150 })
$samples = New-Object System.Collections.Generic.List[int16]
foreach ($n in $notes) {
    $len = [int]($rate * $n.Ms / 1000)
    for ($i = 0; $i -lt $len; $i++) {
        $t = $i / $rate
        $env = [math]::Exp(-9.0 * $i / $len) * [math]::Min(1.0, $i / 80.0) # quick attack, soft decay
        $v = [math]::Sin(2 * [math]::PI * $n.F * $t) * 0.55 + [math]::Sin(2 * [math]::PI * $n.F * 2 * $t) * 0.12
        $samples.Add([int16]($v * $env * 22000))
    }
}
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$dataLen = $samples.Count * 2
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int32](36 + $dataLen)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
$w.Write([Text.Encoding]::ASCII.GetBytes('fmt ')); $w.Write([int32]16); $w.Write([int16]1); $w.Write([int16]1)
$w.Write([int32]$rate); $w.Write([int32]($rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int32]$dataLen)
foreach ($s in $samples) { $w.Write($s) }
$w.Flush()
[IO.File]::WriteAllBytes($out, $ms.ToArray())
"{0} ({1:N1} Ko, {2} ms)" -f $out, ((Get-Item $out).Length / 1KB), [int]($samples.Count * 1000 / $rate)
