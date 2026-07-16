[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'),
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root 'src\ENTHLatencyTester.cs'
$logo = Join-Path $root 'ENTH LOGO 2025 WHITE.png'
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$exe = Join-Path $outputDirectory 'ENTH Latency TESTER.exe'

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$frameworkRoots = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $frameworkRoots | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'C# compiler not found. Install .NET Framework 4.x developer tools.' }

& $csc /nologo /target:winexe /optimize+ "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

Copy-Item -Force $logo (Join-Path $outputDirectory (Split-Path $logo -Leaf))

$signRequested = $CertificatePath -or $CertificateThumbprint
if ($signRequested) {
    if (-not $TimestampUrl) { throw 'A trusted RFC 3161 TimestampUrl is required for signed releases.' }
    if ($CertificatePath -and $CertificateThumbprint) { throw 'Use either CertificatePath or CertificateThumbprint, not both.' }

    $sdkRoots = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    $signtool = $sdkRoots | ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK.' }

    $signArgs = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/d', 'ENTH Latency Tester')
    if ($CertificatePath) {
        $resolvedCertificate = (Resolve-Path $CertificatePath).Path
        $signArgs += @('/f', $resolvedCertificate)
        if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
    } else {
        $signArgs += @('/sha1', ($CertificateThumbprint -replace '\s', ''))
    }
    $signArgs += $exe

    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE." }
    & $signtool verify /pa /v $exe
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed with exit code $LASTEXITCODE." }
}

$signature = Get-AuthenticodeSignature -FilePath $exe
[pscustomobject]@{
    Executable = $exe
    SignatureStatus = $signature.Status
    Signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
}
