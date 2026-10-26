param(
    [string]$OutputFolder = 'ShopWindows',
    [string]$UnityEditorPath = ''
)
$ErrorActionPreference = 'Stop'
if ($OutputFolder -notmatch '^[A-Za-z0-9_-]+$') { throw '构建目录名称无效。' }
$projectPath = Join-Path $PSScriptRoot 'PaperShop'
$editorVersion = '6000.3.26f1'
$editorPath = $null
if (-not [string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
        throw "UnityEditorPath 指定的编辑器不存在：$UnityEditorPath"
    }
    $editorPath = (Resolve-Path -LiteralPath $UnityEditorPath).Path
} else {
    $editorCandidates = @($env:UNITY_EDITOR_PATH, "D:\Unity\Editors\$editorVersion\Editor\Unity.exe")
    foreach ($programDirectory in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not [string]::IsNullOrWhiteSpace($programDirectory)) {
            $editorCandidates += Join-Path $programDirectory "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
        }
    }
    foreach ($candidate in $editorCandidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $editorPath = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
}
if (-not $editorPath) {
    throw "找不到 Unity $editorVersion。请传入 -UnityEditorPath，或设置 UNITY_EDITOR_PATH 环境变量。"
}
$logPath = Join-Path $PSScriptRoot 'foundation-build.log'
$editorArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectPath + '"'), '-buildTarget', 'StandaloneWindows64', '-executeMethod', 'BuildShopFoundation.Build', '-shopOutputFolder', $OutputFolder, '-logFile', ('"' + $logPath + '"'))
$buildProcess = Start-Process -FilePath $editorPath -ArgumentList $editorArguments -WindowStyle Hidden -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) { throw "构建失败，查看 $logPath" }
Write-Output (Join-Path $PSScriptRoot "$OutputFolder\AnonTokyo.exe")
