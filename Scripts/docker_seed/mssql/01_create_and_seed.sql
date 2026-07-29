-- ====================================================================
-- NeoDB Studio - SQL Server 多言語スキーマ構築＋1,000件データ投入
-- ====================================================================
-- 目的     : 英語・日本語の 1,000 件超のリレーションデータを持つ neodb データベースを構築
-- 適用先   : neodb-mssql コンテナ
-- 注意事項 : SQL Server の公式イメージには docker-entrypoint-initdb.d 相当の仕組みが無いため、
--            本ファイルは Scripts\apply_seed.ps1 から sqlcmd 経由で適用する。
--            日本語を格納するため文字列型は必ず NVARCHAR（N プレフィックス付きリテラル）を使用する。
-- 著作権   : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

IF DB_ID('neodb') IS NULL
BEGIN
    CREATE DATABASE neodb;
END
GO

USE neodb;
GO

-- 1. ユーザーマスタテーブル (users)
IF OBJECT_ID('dbo.orders', 'U')   IS NOT NULL DROP TABLE dbo.orders;
IF OBJECT_ID('dbo.users', 'U')    IS NOT NULL DROP TABLE dbo.users;
IF OBJECT_ID('dbo.products', 'U') IS NOT NULL DROP TABLE dbo.products;
GO

CREATE TABLE dbo.users (
    user_id       INT IDENTITY(1,1) PRIMARY KEY,   -- ユーザーID (自動採番)
    user_code     NVARCHAR(20)  NOT NULL UNIQUE,   -- ユーザーコード
    username_en   NVARCHAR(100) NOT NULL,          -- 氏名 (英語)
    username_ja   NVARCHAR(100) NOT NULL,          -- 氏名 (日本語)
    email         NVARCHAR(255) NOT NULL,          -- メールアドレス
    department_ja NVARCHAR(100) NOT NULL,          -- 所属部署 (日本語)
    role_ja       NVARCHAR(100) NOT NULL,          -- 役職 (日本語)
    salary        DECIMAL(12, 2) NOT NULL,         -- 給与額
    is_active     TINYINT DEFAULT 1,               -- 有効フラグ (1:有効 / 0:無効)
    created_at    DATETIME2 DEFAULT SYSDATETIME()  -- 登録日時
);
GO

-- 2. 商品マスタテーブル (products)
CREATE TABLE dbo.products (
    product_id      INT IDENTITY(1,1) PRIMARY KEY,   -- 商品ID (自動採番)
    sku             NVARCHAR(50)  NOT NULL UNIQUE,   -- 商品コード (SKU)
    product_name_en NVARCHAR(200) NOT NULL,          -- 商品名 (英語)
    product_name_ja NVARCHAR(200) NOT NULL,          -- 商品名 (日本語)
    category_ja     NVARCHAR(100) NOT NULL,          -- カテゴリ (日本語)
    unit_price      DECIMAL(10, 2) NOT NULL,         -- 単価
    stock_quantity  INT NOT NULL,                    -- 在庫数
    created_at      DATETIME2 DEFAULT SYSDATETIME()  -- 登録日時
);
GO

-- 3. 注文トランザクションテーブル (orders)
CREATE TABLE dbo.orders (
    order_id          INT IDENTITY(1,1) PRIMARY KEY,   -- 注文ID (自動採番)
    order_no          NVARCHAR(30) NOT NULL UNIQUE,    -- 注文番号
    user_id           INT NOT NULL,                    -- ユーザーID (users.user_id)
    product_id        INT NOT NULL,                    -- 商品ID (products.product_id)
    quantity          INT NOT NULL,                    -- 数量
    unit_price        DECIMAL(10, 2) NOT NULL,         -- 単価
    total_amount      DECIMAL(12, 2) NOT NULL,         -- 合計金額
    payment_status_ja NVARCHAR(50) NOT NULL,           -- 決済ステータス (日本語)
    ordered_at        DATETIME2 DEFAULT SYSDATETIME(), -- 注文日時
    CONSTRAINT fk_orders_user    FOREIGN KEY (user_id)    REFERENCES dbo.users(user_id)       ON DELETE CASCADE,
    CONSTRAINT fk_orders_product FOREIGN KEY (product_id) REFERENCES dbo.products(product_id) ON DELETE CASCADE
);
GO

-- 4. 商品マスタ初期データの投入
INSERT INTO dbo.products (sku, product_name_en, product_name_ja, category_ja, unit_price, stock_quantity) VALUES
('SKU-1001', N'Enterprise Database Cloud Cluster',  N'エンタープライズ DB クラウドクラスタ', N'データベース基盤', 1280000.00, 50),
('SKU-1002', N'AI Model Acceleration Processor',    N'AI アクセラレータプロセッサ',           N'ハードウェア',     450000.00, 120),
('SKU-1003', N'Zero-Trust Security Gateway Module', N'ゼロトラスト セキュリティモジュール',   N'ネットワーク',     320000.00, 80),
('SKU-1004', N'High-Speed NVMe Storage Array',      N'超高速 NVMe ストレージアレイ',         N'ストレージ',       680000.00, 45),
('SKU-1005', N'Automated DevOps Pipeline License',  N'自動化 DevOps パイプラインライセンス', N'ソフトウェア',     150000.00, 500);
GO

-- 5. ユーザー 1,000 件の投入（連番生成 CTE を使用）
WITH numbers AS (
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.users (user_code, username_en, username_ja, email, department_ja, role_ja, salary, is_active, created_at)
SELECT
    'USR-' + RIGHT('00000' + CAST(i AS NVARCHAR(10)), 5),
    'Engineer_' + CAST(i AS NVARCHAR(10)),
    N'開発エンジニア_' + CAST(i AS NVARCHAR(10)) + N' 氏',
    'engineer_' + CAST(i AS NVARCHAR(10)) + '@orionsystems.jp',
    CHOOSE((i % 5) + 1, N'基盤システム開発部', N'クラウドインフラ部', N'DBA運用統括課', N'セキュリティ監査室', N'先端AI研究部'),
    CHOOSE((i % 4) + 1, N'リードアーキテクト', N'シニアデータベースエンジニア', N'スペシャリスト', N'運用マネージャー'),
    450000.00 + ((i * 3500) % 450000),
    CASE WHEN i % 15 = 0 THEN 0 ELSE 1 END,
    DATEADD(HOUR, -i, SYSDATETIME())
FROM numbers;
GO

-- 6. 注文 1,000 件の投入（users / products への外部キーを保持）
WITH numbers AS (
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.orders (order_no, user_id, product_id, quantity, unit_price, total_amount, payment_status_ja, ordered_at)
SELECT
    'ORD-DKR-' + RIGHT('000000' + CAST(i AS NVARCHAR(10)), 6),
    i,
    (i % 5) + 1,
    (i % 8) + 1,
    CHOOSE((i % 5) + 1, 1280000.00, 450000.00, 320000.00, 680000.00, 150000.00),
    ((i % 8) + 1) * CHOOSE((i % 5) + 1, 1280000.00, 450000.00, 320000.00, 680000.00, 150000.00),
    CHOOSE((i % 4) + 1, N'決済完了 (Paid)', N'出荷準備中 (Processing)', N'発送済み (Shipped)', N'キャンセル (Cancelled)'),
    DATEADD(MINUTE, -(i * 20), SYSDATETIME())
FROM numbers;
GO

SELECT 'users' AS table_name, COUNT(*) AS row_count FROM dbo.users
UNION ALL SELECT 'products', COUNT(*) FROM dbo.products
UNION ALL SELECT 'orders',   COUNT(*) FROM dbo.orders;
GO
