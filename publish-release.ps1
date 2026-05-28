#Requires -Version 5.1
<#
.SYNOPSIS
  发布 LdtEditor 单文件包到 dist\。

.DESCRIPTION
  默认：仅生成 self-contained win-x64（无需本机安装 .NET）。
  -FrameworkDependent（别名 -Fdd）：仅生成 framework-dependent 单文件（体积小，需安装 Desktop Runtime）。
  -All：同时生成 FDD 与 self-contained。

.EXAMPLE
  .\publish-release.ps1
.EXAMPLE
  .\publish-release.ps1 -FrameworkDependent
.EXAMPLE
  .\publish-release.ps1 -Fdd
.EXAMPLE
  .\publish-release.ps1 -All
#>
param(
    [Alias('Fdd')]
    [switch] $FrameworkDependent,
    [switch] $All
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$proj = Join-Path $root 'LdtEditor\LdtEditor.csproj'
if (-not (Test-Path $proj)) {
    throw "Project not found: $proj"
}

$csprojRaw = Get-Content -LiteralPath $proj -Raw
if ($csprojRaw -notmatch '<Version>\s*([^<]+?)\s*</Version>') {
    throw "Missing <Version> in LdtEditor.csproj"
}
$v = $matches[1].Trim()

$dist = Join-Path $root 'dist'
if (-not (Test-Path -LiteralPath $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}

$doFdd = $All -or $FrameworkDependent
$doSc = $All -or (-not $FrameworkDependent)

function Publish-FddSingleFile {
    param([string] $OutDir)
    if (Test-Path -LiteralPath $OutDir) {
        Remove-Item -LiteralPath $OutDir -Recurse -Force
    }
    Write-Host "Publishing framework-dependent (single-file) -> $OutDir"
    & dotnet publish $proj -c Release -o $OutDir `
        /p:PublishSingleFile=true `
        /p:PublishSelfContained=false /p:SelfContained=false `
        /p:EnableCompressionInSingleFile=false `
        /p:DebugType=none /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Publish-SelfContainedSingleFileWin64 {
    param([string] $OutDir)
    if (Test-Path -LiteralPath $OutDir) {
        Remove-Item -LiteralPath $OutDir -Recurse -Force
    }
    Write-Host "Publishing self-contained win-x64 (single-file) -> $OutDir"
    & dotnet publish $proj -c Release -r win-x64 --self-contained true -o $OutDir `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:DebugType=none /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($doFdd) {
    $outFdd = Join-Path $dist "LdtEditor-$v-fdd"
    Publish-FddSingleFile -OutDir $outFdd
}

if ($doSc) {
    $outSc = Join-Path $dist "LdtEditor-$v-self-contained-win-x64"
    Publish-SelfContainedSingleFileWin64 -OutDir $outSc
}

Write-Host "Done. Version $v"
