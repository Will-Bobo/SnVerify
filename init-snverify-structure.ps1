# ============================
# SnVerify 项目目录初始化脚本
# 适用环境：Windows 10 / 11
# 仅创建目录 & 文档占位文件
# ============================

Write-Host "Initializing SnVerify project structure..." -ForegroundColor Cyan

# 根目录下的文档目录
$docs = @(
    "docs",
    "docs/00_Project_Context.md",
    "docs/01_PRD_SN_Verify_PC.md",
    "docs/02_Architecture_Guardrails.md",
    "docs/03_Dev_Rules_TDD_and_AI.md",
    "docs/04_Phase1_Minimal_Closed_Loop.md"
)

# SnVerify 主工程内部目录（不创建 WPF 默认文件）
$snverifyDirs = @(
    "SnVerify/Views",
    "SnVerify/ViewModels",
    "SnVerify/Domain/Models",
    "SnVerify/Domain/State",
    "SnVerify/Domain/Rules",
    "SnVerify/Services/Adb",
    "SnVerify/Services/Mes",
    "SnVerify/Services/Logging",
    "SnVerify/Infrastructure/Input",
    "SnVerify/Infrastructure/Time",
    "SnVerify/Infrastructure/Config",
    "SnVerify/Resources"
)

# 测试工程目录
$testDirs = @(
    "SnVerify.Tests/Domain",
    "SnVerify.Tests/Services"
)

# Cursor 约束目录
$cursorDirs = @(
    ".cursor",
    ".cursor/rules.md"
)

# README
$rootFiles = @(
    "README.md"
)

function Create-ItemSafe($path) {
    if (-not (Test-Path $path)) {
        if ($path.EndsWith(".md")) {
            New-Item -ItemType File -Path $path | Out-Null
            Write-Host "Created file: $path" -ForegroundColor Green
        } else {
            New-Item -ItemType Directory -Path $path | Out-Null
            Write-Host "Created directory: $path" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Skipped (exists): $path" -ForegroundColor DarkGray
    }
}

# 创建 docs
foreach ($item in $docs) {
    Create-ItemSafe $item
}

# 创建主工程结构
foreach ($dir in $snverifyDirs) {
    Create-ItemSafe $dir
}

# 创建测试工程结构
foreach ($dir in $testDirs) {
    Create-ItemSafe $dir
}

# 创建 cursor 目录
foreach ($item in $cursorDirs) {
    Create-ItemSafe $item
}

# 创建根文件
foreach ($file in $rootFiles) {
    Create-ItemSafe $file
}

Write-Host "`nSnVerify directory structure initialized successfully." -ForegroundColor Cyan
Write-Host "Next step: create SnVerify.Tests NUnit project and fill docs." -ForegroundColor Magenta
