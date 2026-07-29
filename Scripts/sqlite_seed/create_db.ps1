$dbPath = "F:\NeoDbStudio_Project\Scripts\sqlite_seed\neodb_sqlite.db"
$dir = [System.IO.Path]::GetDirectoryName($dbPath)
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir }

# SQL DDL & DML スクリプトファイルを作成
$sqlContent = @"
CREATE TABLE IF NOT EXISTS users (
    user_id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_code TEXT NOT NULL UNIQUE,
    username_en TEXT NOT NULL,
    username_ja TEXT NOT NULL,
    email TEXT NOT NULL,
    department_ja TEXT NOT NULL,
    role_ja TEXT NOT NULL,
    salary REAL NOT NULL,
    is_active INTEGER DEFAULT 1,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS products (
    product_id INTEGER PRIMARY KEY AUTOINCREMENT,
    sku TEXT NOT NULL UNIQUE,
    product_name_en TEXT NOT NULL,
    product_name_ja TEXT NOT NULL,
    category_ja TEXT NOT NULL,
    unit_price REAL NOT NULL,
    stock_quantity INTEGER NOT NULL,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS orders (
    order_id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_no TEXT NOT NULL UNIQUE,
    user_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price REAL NOT NULL,
    total_amount REAL NOT NULL,
    payment_status_ja TEXT NOT NULL,
    ordered_at TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(user_id) REFERENCES users(user_id),
    FOREIGN KEY(product_id) REFERENCES products(product_id)
);
"@

Set-Content -Path "F:\NeoDbStudio_Project\Scripts\sqlite_seed\schema.sql" -Value $sqlContent -Encoding UTF8
Write-Host "SQLite schema script written successfully."
