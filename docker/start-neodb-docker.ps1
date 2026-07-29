Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " NeoDB Studio: Docker Multi-DBMS Sandbox Launcher" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$dockerFile = "$PSScriptRoot/docker-compose.neodb.yml"

if (!(Test-Path $dockerFile)) {
    Write-Host "[ERROR] docker-compose.neodb.yml not found in $PSScriptRoot" -ForegroundColor Red
    exit 1
}

Write-Host "`n[1/2] Starting Docker Containers for NeoDB Studio..." -ForegroundColor Yellow
docker compose -f $dockerFile up -d

Write-Host "`n[2/2] Container Status:" -ForegroundColor Yellow
docker compose -f $dockerFile ps

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ✅ All DBMS Sandboxes and gRPC API are running!" -ForegroundColor Green
Write-Host "  - NeoDB gRPC API: localhost:50051" -ForegroundColor Gray
Write-Host "  - PostgreSQL:     localhost:5432  (User: postgres, Pass: password)" -ForegroundColor Gray
Write-Host "  - MySQL:          localhost:3307  (User: root, Pass: password)" -ForegroundColor Gray
Write-Host "  - MariaDB:        localhost:3308  (User: root, Pass: password)" -ForegroundColor Gray
Write-Host "  - SQL Server:     localhost:1433  (User: sa, Pass: Password123!)" -ForegroundColor Gray
Write-Host "  - Oracle:         localhost:1521" -ForegroundColor Gray
Write-Host "  - MongoDB:        localhost:27017" -ForegroundColor Gray
Write-Host "  - Redis:          localhost:6380" -ForegroundColor Gray
Write-Host "  - ClickHouse:     localhost:8123" -ForegroundColor Gray
Write-Host "  - Cassandra:      localhost:9042" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Green
