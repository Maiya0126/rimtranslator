# RimTranslator 一键发布脚本
# 用法：.\publish.ps1 [-Version 1.3.6]
# 产出：publish\RimTranslator-<版本>-full.exe（自包含+压缩+原生库）与 -lite.exe（框架依赖）
# 注意：full 必须同时开 EnableCompressionInSingleFile 与 IncludeNativeLibrariesForSelfExtract，
#       只开压缩会把 WPF 原生 DLL 排除在压缩包外 → 启动即 0xC000041D 闪退（v1.3.6 首发踩过）。
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pub = Join-Path $root "publish"

# 版本号：未指定时读 csproj
if (-not $Version) {
    $csproj = Get-Content (Join-Path $root "RimTrans.App\RimTrans.App.csproj") -Raw
    if ($csproj -match "<Version>([\d\.]+)</Version>") { $Version = $Matches[1] }
    else { throw "无法从 csproj 读取版本号，请用 -Version 指定" }
}
Write-Host "== 发布 v$Version ==" -ForegroundColor Cyan

# 版本号三处一致性检查（csproj / 底栏 / Updater）
$mismatch = @()
$csprojText = Get-Content (Join-Path $root "RimTrans.App\RimTrans.App.csproj") -Raw
$xamlText  = Get-Content (Join-Path $root "RimTrans.App\MainWindow.xaml") -Raw
$updText   = Get-Content (Join-Path $root "RimTrans.Core\Updater.cs") -Raw
if ($csprojText -notmatch [regex]::Escape("<Version>$Version</Version>")) { $mismatch += "csproj" }
if ($xamlText  -notmatch "v$Version") { $mismatch += "MainWindow.xaml 底栏" }
if ($updText   -notmatch [regex]::Escape("CurrentVersion = `"$Version`"")) { $mismatch += "Updater.cs" }
if ($mismatch) { throw "版本号不一致：$($mismatch -join '、') 未指向 $Version，请先统一再发布" }

New-Item -ItemType Directory -Path $pub -Force | Out-Null

# full：自包含 + 单文件 + 压缩 + 原生库自解压（两个参数必须成对）
Write-Host "[1/3] 构建 full（自包含，约 60MB）…" -ForegroundColor Yellow
dotnet publish (Join-Path $root "RimTrans.App\RimTrans.App.csproj") -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $pub "tmp-f")
if ($LASTEXITCODE -ne 0) { throw "full 构建失败" }

# lite：框架依赖 + 单文件
Write-Host "[2/3] 构建 lite（框架依赖，约 0.8MB）…" -ForegroundColor Yellow
dotnet publish (Join-Path $root "RimTrans.App\RimTrans.App.csproj") -c Release -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o (Join-Path $pub "tmp-l")
if ($LASTEXITCODE -ne 0) { throw "lite 构建失败" }

Move-Item (Join-Path $pub "tmp-f\RimTranslator.exe") (Join-Path $pub "RimTranslator-$Version-full.exe") -Force
Move-Item (Join-Path $pub "tmp-l\RimTranslator.exe") (Join-Path $pub "RimTranslator-$Version-lite.exe") -Force
Remove-Item (Join-Path $pub "tmp-f"), (Join-Path $pub "tmp-l") -Recurse -Force

# 冒烟：两个 exe 都要能启动出窗口
Write-Host "[3/3] 冒烟测试（启动 12 秒验证不闪退）…" -ForegroundColor Yellow
Add-Type -AssemblyName UIAutomationClient
foreach ($exe in @("RimTranslator-$Version-full.exe", "RimTranslator-$Version-lite.exe")) {
    $path = Join-Path $pub $exe
    $p = Start-Process $path -PassThru
    $ok = $false
    $elapsed = 0.0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 12) {
        Start-Sleep -Milliseconds 300
        if ($p.HasExited) { break }
        $rootEl = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
        if ($rootEl.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)) { $ok = $true; break }
    }
    $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    if (-not $ok) { throw "冒烟失败：$exe 未能在 12 秒内启动出窗口" }
    Write-Host "  $exe 启动 OK (${elapsed}s)" -ForegroundColor Green
}

Write-Host ""
Write-Host "== 完成 ==" -ForegroundColor Cyan
Get-ChildItem $pub -Filter "RimTranslator-$Version-*.exe" | ForEach-Object {
    Write-Host ("  {0}  {1:N2} MB" -f $_.Name, ($_.Length/1MB))
}
Write-Host ""
Write-Host "后续步骤（手动确认后执行）：" -ForegroundColor Cyan
Write-Host '  git add -A; git commit -m "v<版本>: ..."; git push'
Write-Host '  gh release create v<版本> publish\RimTranslator-<版本>-full.exe publish\RimTranslator-<版本>-lite.exe --title "v<版本> — ..." --notes-file <说明文件>'
