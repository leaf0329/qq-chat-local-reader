param(
    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$buildInputsRoot = if ([string]::IsNullOrWhiteSpace($env:QCLR_NATIVE_BUILD_INPUTS)) {
    Join-Path $artifactsRoot '.build-inputs'
}
else {
    [System.IO.Path]::GetFullPath($env:QCLR_NATIVE_BUILD_INPUTS)
}
$sqlCipherCommit = 'c7e811b399379c948b423872ad7ba91d2ce38434'
$vcpkgCommit = '701d832d37ccc61ec86855927d71c55dd7f624dc'
$triplet = 'x64-windows-static-md'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'native\win-x64'
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$nativePath = Join-Path $outputRoot 'sqlcipher.dll'
$licenseRoot = Join-Path $outputRoot 'licenses'

if ((Test-Path -LiteralPath $nativePath -PathType Leaf) -and -not $Force) {
    Write-Output $nativePath
    exit 0
}

New-Item -ItemType Directory -Path $buildInputsRoot,$outputRoot,$licenseRoot -Force | Out-Null

function Get-PinnedRepository {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git') -PathType Container)) {
        if (Test-Path -LiteralPath $Destination) {
            $resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
            if (-not $resolvedDestination.StartsWith($buildInputsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw 'Refusing to replace a build-input directory outside artifacts.'
            }
            Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
        }
        git init --quiet $Destination
        if ($LASTEXITCODE -ne 0) { throw "Could not initialize $Destination." }
        git -C $Destination remote add origin $Url
        if ($LASTEXITCODE -ne 0) { throw "Could not configure $Url." }
    }

    git -C $Destination fetch --quiet --depth 1 origin $Commit
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch pinned commit $Commit." }
    git -C $Destination checkout --quiet --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw "Could not check out pinned commit $Commit." }
    $actualCommit = (git -C $Destination rev-parse HEAD).Trim()
    if ($actualCommit -ne $Commit) { throw "Pinned repository resolved to unexpected commit $actualCommit." }
}

$sqlCipherSource = Join-Path $buildInputsRoot "sqlcipher-$sqlCipherCommit"
$vcpkgRoot = Join-Path $buildInputsRoot "vcpkg-$vcpkgCommit"
Get-PinnedRepository -Url 'https://github.com/sqlcipher/sqlcipher.git' -Commit $sqlCipherCommit -Destination $sqlCipherSource
Get-PinnedRepository -Url 'https://github.com/microsoft/vcpkg.git' -Commit $vcpkgCommit -Destination $vcpkgRoot

$vcpkgExe = Join-Path $vcpkgRoot 'vcpkg.exe'
if (-not (Test-Path -LiteralPath $vcpkgExe -PathType Leaf)) {
    $ErrorActionPreference = 'Continue'
    try {
        $bootstrapOutput = @(& (Join-Path $vcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics 2>&1)
        $bootstrapExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = 'Stop'
    }
    $bootstrapOutput | Write-Output
    if ($bootstrapExitCode -ne 0) {
        $bootstrapDetail = (($bootstrapOutput | Select-Object -Last 16) -join "`n")
        $escapedBootstrapDetail = $bootstrapDetail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Host "::error title=vcpkg bootstrap failed::$escapedBootstrapDetail"
        throw 'vcpkg bootstrap failed.'
    }
}

$ErrorActionPreference = 'Continue'
try {
    $vcpkgOutput = @(& $vcpkgExe install "openssl:$triplet" --clean-after-build --disable-metrics 2>&1)
    $vcpkgExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = 'Stop'
}
$vcpkgOutput | Write-Output
if ($vcpkgExitCode -ne 0) {
    $vcpkgDetail = (($vcpkgOutput | Select-Object -Last 16) -join "`n")
    $escapedVcpkgDetail = $vcpkgDetail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Host "::error title=Pinned OpenSSL build failed::$escapedVcpkgDetail"
    throw 'Pinned OpenSSL build failed.'
}

$opensslRoot = Join-Path $vcpkgRoot "installed\$triplet"
$opensslInclude = Join-Path $opensslRoot 'include'
$opensslLib = Join-Path $opensslRoot 'lib'
$opensslCopyright = Join-Path $opensslRoot 'share\openssl\copyright'
foreach ($requiredPath in @($opensslInclude, (Join-Path $opensslLib 'libcrypto.lib'), $opensslCopyright)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) { throw "Missing OpenSSL build input: $requiredPath" }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw 'Visual Studio Build Tools were not found.' }
$visualStudioRoot = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if ([string]::IsNullOrWhiteSpace($visualStudioRoot)) { throw 'MSVC x64 build tools were not found.' }
$vcvarsPath = Join-Path $visualStudioRoot 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvarsPath -PathType Leaf)) { throw 'vcvars64.bat was not found.' }

$nmakeOptions = @(
    '-DSQLITE_HAS_CODEC',
    '-DSQLITE_EXTRA_INIT=sqlcipher_extra_init',
    '-DSQLITE_EXTRA_SHUTDOWN=sqlcipher_extra_shutdown',
    '-DSQLCIPHER_CRYPTO_OPENSSL',
    '-DSQLITE_TEMP_STORE=2',
    '-DSQLITE_ENABLE_FTS5',
    '-DSQLITE_OMIT_LOAD_EXTENSION',
    "-I`"$opensslInclude`""
) -join ' '
$linkLibraries = "`"$opensslLib\libcrypto.lib`" crypt32.lib ws2_32.lib bcrypt.lib advapi32.lib user32.lib rpcrt4.lib"
$buildCommandPath = Join-Path $buildInputsRoot 'build-sqlcipher.cmd'
$commandLines = @(
    '@echo off',
    "call `"$vcvarsPath`" >nul || exit /b 1",
    "cd /d `"$sqlCipherSource`" || exit /b 1",
    "nmake /nologo /f Makefile.msc clean >nul 2>nul",
    "nmake /nologo /f Makefile.msc sqlite3.dll USE_NATIVE_LIBPATHS=1 PLATFORM=x64 `"OPTS=$nmakeOptions`" `"LTLIBS=$linkLibraries`" || exit /b 1"
)
[System.IO.File]::WriteAllLines($buildCommandPath, $commandLines, [System.Text.Encoding]::ASCII)
$ErrorActionPreference = 'Continue'
try {
    $nativeBuildOutput = @(& cmd.exe /d /c $buildCommandPath 2>&1)
    $nativeBuildExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = 'Stop'
}
$nativeBuildOutput | Write-Output
if ($nativeBuildExitCode -ne 0) {
    $failureDetail = (($nativeBuildOutput | Select-Object -Last 16) -join "`n")
    $escapedFailureDetail = $failureDetail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Host "::error title=SQLCipher native build failed::$escapedFailureDetail"
    throw 'Pinned SQLCipher native build failed.'
}

$builtDll = Join-Path $sqlCipherSource 'sqlite3.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw 'SQLCipher build did not produce sqlite3.dll.' }
Copy-Item -LiteralPath $builtDll -Destination $nativePath -Force
Copy-Item -LiteralPath (Join-Path $sqlCipherSource 'LICENSE.md') -Destination (Join-Path $licenseRoot 'SQLCipher.txt') -Force
Copy-Item -LiteralPath $opensslCopyright -Destination (Join-Path $licenseRoot 'OpenSSL.txt') -Force

$apacheLicensePath = Join-Path $buildInputsRoot 'Apache-2.0.txt'
$apacheLicenseHash = 'cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30'
if (-not (Test-Path -LiteralPath $apacheLicensePath -PathType Leaf)) {
    Invoke-WebRequest -Uri 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile $apacheLicensePath
}
$actualApacheLicenseHash = (Get-FileHash -LiteralPath $apacheLicensePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualApacheLicenseHash -ne $apacheLicenseHash) { throw 'Apache-2.0 license text hash mismatch.' }
Copy-Item -LiteralPath $apacheLicensePath -Destination (Join-Path $licenseRoot 'SQLitePCLRaw-Apache-2.0.txt') -Force

$provenance = @(
    'SQLCipher native build provenance',
    "SQLCipher commit: $sqlCipherCommit",
    "vcpkg commit: $vcpkgCommit",
    "vcpkg triplet: $triplet",
    'Crypto provider: OpenSSL (statically linked)',
    'Target: Windows x64'
)
[System.IO.File]::WriteAllLines((Join-Path $outputRoot 'BUILD-PROVENANCE.txt'), $provenance, [System.Text.UTF8Encoding]::new($false))

Write-Output $nativePath
