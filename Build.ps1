param(
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$assets = Join-Path $root "assets"

if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Force -Path $assets | Out-Null }

$icon = Join-Path $assets "DiskPulse.ico"
if (-not (Test-Path $icon)) {
    & (Join-Path $root "tools\MakeIcon.ps1")
}

$cscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "找不到 .NET Framework 4.x 的 csc.exe" }

$framework = Split-Path -Parent $csc
$references = @(
    (Join-Path $framework "System.dll"),
    (Join-Path $framework "System.Core.dll"),
    (Join-Path $framework "System.Drawing.dll"),
    (Join-Path $framework "System.Windows.Forms.dll"),
    (Join-Path $framework "System.Runtime.Serialization.dll"),
    (Join-Path $framework "System.Xml.dll")
)
foreach ($reference in $references) {
    if (-not (Test-Path $reference)) { throw "缺少程序集: $reference" }
}

$sources = Get-ChildItem -Path $src -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
$outExe = Join-Path $root "DiskPulse.exe"
$manifest = Join-Path $src "app.manifest"

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/utf8output",
    "/win32icon:$icon",
    "/win32manifest:$manifest",
    "/out:$outExe"
)
foreach ($reference in $references) { $arguments += "/reference:$reference" }
$arguments += $sources

Write-Host "使用编译器: $csc"
Write-Host "输出: $outExe"
& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$readmeSource = Join-Path $root "README.md"
$readmeOut = Join-Path $root "使用说明.md"
if (Test-Path $readmeSource) { Copy-Item $readmeSource $readmeOut -Force }

$zip = Join-Path $root "DiskPulse-win64.zip"
if (-not $NoZip) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $zipItems = @($outExe)
    if (Test-Path $readmeOut) { $zipItems += $readmeOut }
    $screenshot = Join-Path $root "screenshot.png"
    if (Test-Path $screenshot) { $zipItems += $screenshot }
    foreach ($scriptName in @("启用开机自启.cmd", "关闭开机自启.cmd")) {
        $scriptPath = Join-Path $root $scriptName
        if (Test-Path $scriptPath) { $zipItems += $scriptPath }
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $zipItems) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $item, [System.IO.Path]::GetFileName($item)) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "已生成压缩包: $zip"
}

$item = Get-Item $outExe
Write-Host ("编译完成: {0} ({1:N0} 字节)" -f $item.FullName, $item.Length)
