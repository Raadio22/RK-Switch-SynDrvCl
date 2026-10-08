[CmdletBinding()]
param(
    [string]$InnoCompiler = 'D:\ChatGPT\Tools\InnoSetup-6.7.3\ISCC.exe',
    [string]$OutputDirectory = 'D:\ChatGPT\Projekty\RK-Switch-SynDrvCl\02_Vystupy_ChatGPT'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$tempRoot = 'D:\ChatGPT\Temp'
$buildRoot = 'D:\ChatGPT\Build\RK-Switch-SynDrvCl'
$projectPath = Join-Path $repoRoot 'RKSwitch.SynDrvCl.csproj'
$installerScript = Join-Path $PSScriptRoot 'RK-Switch-SynDrvCl.iss'

New-Item -ItemType Directory -Force -Path $tempRoot, $buildRoot, $OutputDirectory | Out-Null
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup compiler nebyl nalezen: $InnoCompiler"
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Neplatná verze projektu: $version"
}

$stageRoot = Join-Path $buildRoot $version
$publishDir = Join-Path $stageRoot 'publish'
if (Test-Path -LiteralPath $stageRoot) {
    $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
    $resolvedBuildRoot = (Resolve-Path -LiteralPath $buildRoot).Path
    if (-not $resolvedStage.StartsWith($resolvedBuildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Odmítnuto odstranění neočekávané složky: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish selhal s kódem $LASTEXITCODE" }

$arguments = @(
    "/DAppVersion=$version",
    "/DPublishDir=$publishDir",
    "/DOutputDir=$OutputDirectory",
    $installerScript
)
& $InnoCompiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Inno Setup kompilace selhala s kódem $LASTEXITCODE" }

$setupPath = Join-Path $OutputDirectory "RK-Switch-SynDrvCl-$version-Setup-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Výsledný instalátor nebyl nalezen: $setupPath"
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
$hashPath = Join-Path $OutputDirectory "RK-Switch-SynDrvCl-$version-Setup-win-x64.sha256"
Set-Content -LiteralPath $hashPath -Encoding ascii -NoNewline -Value "$hash  $([IO.Path]::GetFileName($setupPath))`r`n"

$signature = Get-AuthenticodeSignature -LiteralPath $setupPath
[PSCustomObject]@{
    Version = $version
    Installer = $setupPath
    Sha256 = $hash
    SignatureStatus = $signature.Status
    HashFile = $hashPath
}
