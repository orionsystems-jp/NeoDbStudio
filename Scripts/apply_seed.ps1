# ====================================================================
# NeoDB Studio - 初期化スクリプト非対応コンテナへのシード適用スクリプト
# ====================================================================
# ファイル名     : apply_seed.ps1
# ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\Scripts\apply_seed.ps1
# 処理概要/目的  : docker-entrypoint-initdb.d を持たない DBMS コンテナ（SQL Server / Oracle / Redis）へ
#                  シードデータを適用し、あわせて SQLite のサンプル DB ファイルを生成する
# 使用方法       : pwsh -File .\apply_seed.ps1          … 全対象へ適用
#                  pwsh -File .\apply_seed.ps1 -Only mssql,redis … 対象を限定して適用
# 依存関係       : Docker Desktop（各 neodb-* コンテナが起動済みであること）、Python 3（SQLite 生成時のみ）
# 注意事項       : MySQL / MariaDB / PostgreSQL / MongoDB は docker-compose.neodb.yml で
#                  docker-entrypoint-initdb.d へマウント済みのため本スクリプトの対象外。
#                  それらは `docker compose -f docker\docker-compose.neodb.yml down -v` の後
#                  `up -d` することで初回起動時に自動適用される。
# 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

[CmdletBinding()]
param(
    [ValidateSet('mssql', 'oracle', 'redis', 'sqlite')]
    [string[]] $Only = @('mssql', 'oracle', 'redis', 'sqlite')
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Test-ContainerRunning {
    param([Parameter(Mandatory)][string] $Name)

    $running = docker ps --filter "name=$Name" --format '{{.Names}}'
    return ($running -contains $Name)
}

# --- 1. SQL Server ---------------------------------------------------
if ($Only -contains 'mssql') {
    Write-Host '[1/4] SQL Server (neodb-mssql) へシードを適用します...' -ForegroundColor Cyan

    if (-not (Test-ContainerRunning -Name 'neodb-mssql')) {
        Write-Warning '  neodb-mssql が起動していないためスキップしました。'
    }
    else {
        $sqlPath = Join-Path $scriptRoot 'docker_seed\mssql\01_create_and_seed.sql'
        docker cp $sqlPath neodb-mssql:/tmp/neodb_seed.sql | Out-Null
        docker exec neodb-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Password123!' -C -i /tmp/neodb_seed.sql
        Write-Host '  SQL Server への適用が完了しました。' -ForegroundColor Green
    }
}

# --- 2. Oracle -------------------------------------------------------
if ($Only -contains 'oracle') {
    Write-Host '[2/4] Oracle (neodb-oracle) へシードを適用します...' -ForegroundColor Cyan

    if (-not (Test-ContainerRunning -Name 'neodb-oracle')) {
        Write-Warning '  neodb-oracle が起動していないためスキップしました。'
    }
    else {
        $sqlPath = Join-Path $scriptRoot 'docker_seed\oracle\01_create_and_seed.sql'
        docker cp $sqlPath neodb-oracle:/tmp/neodb_seed.sql | Out-Null
        docker exec neodb-oracle bash -lc 'sqlplus -S system/Password123!@localhost:1521/FREEPDB1 @/tmp/neodb_seed.sql'
        Write-Host '  Oracle への適用が完了しました。' -ForegroundColor Green
    }
}

# --- 3. Redis --------------------------------------------------------
if ($Only -contains 'redis') {
    Write-Host '[3/4] Redis (neodb-redis) へサンプルキーを投入します...' -ForegroundColor Cyan

    if (-not (Test-ContainerRunning -Name 'neodb-redis')) {
        Write-Warning '  neodb-redis が起動していないためスキップしました。'
    }
    else {
        # NeoDB Studio のオブジェクトツリーは ":" 区切りの名前空間を疑似テーブルとして表示するため、
        # user: / product: / order: の 3 名前空間でサンプルキーを構成する。
        # 他 DBMS（users 1,000件等）とスケールを揃え、パフォーマンス比較を行えるようにする
        $redisCommands = @'
FLUSHDB
SET app:name "NeoDB Studio"
SET app:version "1.0.0"
'@
        for ($i = 1; $i -le 1000; $i++) {
            $redisCommands += "`nHSET user:$i user_code USR-$($i.ToString('00000')) username_en Engineer_$i salary $((450000 + ($i * 3500) % 450000))"
            $redisCommands += "`nHSET product:$i sku SKU-$((1000 + $i)) stock_quantity $((($i * 7) % 500))"
            $redisCommands += "`nHSET order:$i order_no ORD-DKR-$($i.ToString('000000')) quantity $((($i % 8) + 1))"
        }

        $tempFile = Join-Path $env:TEMP 'neodb_redis_seed.txt'
        Set-Content -Path $tempFile -Value $redisCommands -Encoding ASCII
        docker cp $tempFile neodb-redis:/tmp/neodb_redis_seed.txt | Out-Null
        docker exec neodb-redis sh -c 'redis-cli < /tmp/neodb_redis_seed.txt > /dev/null && redis-cli DBSIZE'
        Remove-Item $tempFile -Force
        Write-Host '  Redis への適用が完了しました。' -ForegroundColor Green
    }
}

# --- 4. SQLite -------------------------------------------------------
if ($Only -contains 'sqlite') {
    Write-Host '[4/4] SQLite サンプル DB ファイルを生成します...' -ForegroundColor Cyan

    $pythonExe = (Get-Command python -ErrorAction SilentlyContinue)
    if ($null -eq $pythonExe) {
        Write-Warning '  Python が見つからないため SQLite DB の生成をスキップしました。'
    }
    else {
        $generator = Join-Path $scriptRoot 'sqlite_seed\create_db.py'
        & $pythonExe.Source $generator
        Write-Host '  SQLite DB ファイルの生成が完了しました。' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host 'シード適用処理が完了しました。' -ForegroundColor Green
Write-Host 'MySQL / MariaDB / PostgreSQL / MongoDB は compose の初期化スクリプトで自動投入されます。' -ForegroundColor Yellow
Write-Host '未反映の場合は下記を実行してください（ボリュームを削除して初期化を再実行します）:' -ForegroundColor Yellow
Write-Host '  docker compose -f docker\docker-compose.neodb.yml down -v' -ForegroundColor Yellow
Write-Host '  docker compose -f docker\docker-compose.neodb.yml up -d' -ForegroundColor Yellow
