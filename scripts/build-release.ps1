param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',

    [Parameter(Mandatory = $false)]
    [string]$DotnetPath = 'dotnet',

    [Parameter(Mandatory = $false)]
    [string]$InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot $Version))
if (-not $releaseRoot.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved release directory is outside the repository artifacts directory.'
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

$portableRoot = Join-Path $releaseRoot 'portable'
$helperRoot = Join-Path $releaseRoot 'snapshot-helper'
New-Item -ItemType Directory -Path $portableRoot,$helperRoot -Force | Out-Null

& $DotnetPath test (Join-Path $repositoryRoot 'QQChatLocalReader.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

& $DotnetPath publish (Join-Path $repositoryRoot 'src\QQChatLocalReader.App\QQChatLocalReader.App.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $portableRoot
if ($LASTEXITCODE -ne 0) { throw 'Main application publish failed.' }

& $DotnetPath publish (Join-Path $repositoryRoot 'src\QQChatLocalReader.SnapshotHelper\QQChatLocalReader.SnapshotHelper.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $helperRoot
if ($LASTEXITCODE -ne 0) { throw 'Snapshot helper publish failed.' }

Copy-Item -LiteralPath (Join-Path $helperRoot 'QQChatLocalReader.SnapshotHelper.exe') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PRIVACY.md') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $portableRoot

$zipPath = Join-Path $releaseRoot "qq-chat-local-reader-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $languageCommit = '1ae7bf81dc0d2013235dfe4bb0b6f4e4a0b6b25c'
    $languageHash = 'e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d'
    $buildInputsRoot = Join-Path $artifactsRoot '.build-inputs'
    $languagePath = Join-Path $buildInputsRoot "ChineseSimplified-$languageCommit.isl"
    New-Item -ItemType Directory -Path $buildInputsRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $languagePath -PathType Leaf)) {
        $temporaryLanguagePath = $languagePath + '.tmp'
        try {
            Invoke-WebRequest -Uri "https://raw.githubusercontent.com/jrsoftware/issrc/$languageCommit/Files/Languages/ChineseSimplified.isl" -OutFile $temporaryLanguagePath
            Move-Item -LiteralPath $temporaryLanguagePath -Destination $languagePath
        }
        finally {
            if (Test-Path -LiteralPath $temporaryLanguagePath) { Remove-Item -LiteralPath $temporaryLanguagePath -Force }
        }
    }

    $actualLanguageHash = (Get-FileHash -LiteralPath $languagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualLanguageHash -ne $languageHash) { throw 'Pinned Inno Setup language file hash mismatch.' }
    & $InnoCompilerPath "/DMyAppVersion=$Version" "/DMySourceDir=$portableRoot" "/DMyOutputDir=$releaseRoot" "/DMyLanguageFile=$languagePath" (Join-Path $repositoryRoot 'installer\qq-chat-local-reader.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}

Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM

Write-Output $releaseRoot
