<#
.SYNOPSIS
  Unity エディタのウィンドウ レイアウトを退避／復元する。

.DESCRIPTION
  Unity を CLI / バッチモードで起動すると、終了時に現在のレイアウトが
  書き出され、主人が保存した「2 by 3 ＋ One Column」等が初期レイアウトに
  戻ってしまうことがある。
  AI／CI がエディタを起動する前に -Backup、終わったら -Restore を呼ぶこと。

  対象:
    UserSettings/Layouts/*.dwlt      (Unity 6 の既定の保存先)
    Library/CurrentLayout-*.dwlt     (古い Unity / 一部の構成)

.EXAMPLE
  pwsh Tools/editor-layout-backup.ps1 -Backup
  unity run ... / Unity.exe -batchmode ...
  pwsh Tools/editor-layout-backup.ps1 -Restore

.NOTES
  退避先は Tools/.layout-backup/ (git 管理外)。
#>
[CmdletBinding(DefaultParameterSetName = 'Backup')]
param(
    [Parameter(ParameterSetName = 'Backup')][switch]$Backup,
    [Parameter(ParameterSetName = 'Restore')][switch]$Restore,
    [Parameter(ParameterSetName = 'Status')][switch]$Status,
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectPath) {
    $ProjectPath = Split-Path -Parent $PSScriptRoot
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

$BackupRoot = Join-Path $PSScriptRoot '.layout-backup'

# 退避対象: (プロジェクトからの相対ディレクトリ, ワイルドカード)
$Targets = @(
    @{ Dir = 'UserSettings/Layouts'; Filter = '*.dwlt' },
    @{ Dir = 'Library';              Filter = 'CurrentLayout-*.dwlt' }
)

function Get-LayoutFiles {
    foreach ($t in $Targets) {
        $dir = Join-Path $ProjectPath $t.Dir
        if (Test-Path $dir) {
            Get-ChildItem -Path $dir -Filter $t.Filter -File -ErrorAction SilentlyContinue |
                ForEach-Object {
                    [pscustomobject]@{
                        Full     = $_.FullName
                        Relative = (Join-Path $t.Dir $_.Name) -replace '\\', '/'
                        Size     = $_.Length
                        Written  = $_.LastWriteTime
                    }
                }
        }
    }
}

function Invoke-Backup {
    if (Test-Path $BackupRoot) { Remove-Item $BackupRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

    $files = @(Get-LayoutFiles)
    if ($files.Count -eq 0) {
        Write-Host "[layout] 退避対象のレイアウトが見つかりません ($ProjectPath)"
        return
    }
    foreach ($f in $files) {
        $dest = Join-Path $BackupRoot $f.Relative
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
        Copy-Item -Path $f.Full -Destination $dest -Force
        Write-Host ("[layout] backup  {0}  ({1} bytes, {2:yyyy-MM-dd HH:mm:ss})" -f $f.Relative, $f.Size, $f.Written)
    }
    # 署名 (復元後の照合用)
    $files | Select-Object Relative, Size |
        ConvertTo-Json -Depth 3 |
        Out-File -FilePath (Join-Path $BackupRoot 'manifest.json') -Encoding utf8
    Write-Host "[layout] 退避先: $BackupRoot"
}

function Invoke-Restore {
    if (-not (Test-Path $BackupRoot)) {
        Write-Host "[layout] 退避がありません。復元は行いません。"
        return
    }
    $restored = 0
    foreach ($t in $Targets) {
        $srcDir = Join-Path $BackupRoot $t.Dir
        if (-not (Test-Path $srcDir)) { continue }
        $dstDir = Join-Path $ProjectPath $t.Dir
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Get-ChildItem -Path $srcDir -Filter $t.Filter -File | ForEach-Object {
            $dst = Join-Path $dstDir $_.Name
            Copy-Item -Path $_.FullName -Destination $dst -Force
            Write-Host ("[layout] restore {0}/{1}  ({2} bytes)" -f $t.Dir, $_.Name, $_.Length)
            $restored++
        }
    }
    Write-Host "[layout] 復元したファイル数: $restored"
}

function Invoke-Status {
    Write-Host "[layout] project: $ProjectPath"
    Write-Host "[layout] --- 現在 ---"
    $cur = @(Get-LayoutFiles)
    if ($cur.Count -eq 0) { Write-Host "  (なし)" }
    foreach ($f in $cur) {
        Write-Host ("  {0}  {1} bytes  {2:yyyy-MM-dd HH:mm:ss}" -f $f.Relative, $f.Size, $f.Written)
    }
    Write-Host "[layout] --- 退避 ---"
    if (-not (Test-Path $BackupRoot)) { Write-Host "  (なし)"; return }
    Get-ChildItem -Path $BackupRoot -Recurse -Filter '*.dwlt' -File | ForEach-Object {
        Write-Host ("  {0}  {1} bytes  {2:yyyy-MM-dd HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime)
    }
}

switch ($PSCmdlet.ParameterSetName) {
    'Backup'  { Invoke-Backup }
    'Restore' { Invoke-Restore }
    'Status'  { Invoke-Status }
}
