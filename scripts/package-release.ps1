$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $projectDirectory 'release'
$portableDirectory = Join-Path $releaseDirectory 'LoviKadr-Portable'
$archivePath = Join-Path $releaseDirectory 'LoviKadr-Portable-x64.zip'
$setupPath = Join-Path $releaseDirectory 'LoviKadr-Setup-x64.exe'

if (!(Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw 'Build the installer before packaging the release.' }
Compress-Archive -LiteralPath $portableDirectory -DestinationPath $archivePath -CompressionLevel Optimal -Force
$checksums = @($setupPath, $archivePath) | ForEach-Object {
    $inputStream = [System.IO.File]::OpenRead($_)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = [System.BitConverter]::ToString($algorithm.ComputeHash($inputStream)).Replace('-', '').ToLowerInvariant()
        '{0}  {1}' -f $hash, (Split-Path -Leaf $_)
    }
    finally { $inputStream.Dispose(); $algorithm.Dispose() }
}
[System.IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $checksums, [System.Text.Encoding]::ASCII)
