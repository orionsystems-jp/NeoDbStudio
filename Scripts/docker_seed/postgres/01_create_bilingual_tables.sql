-- ====================================================================
-- NeoDB Studio - PostgreSQL 多言語リレーショナルスキーマ (DDL)
-- ====================================================================
-- 目的: 英語・日本語の多言語データ（UTF-8）に対応したリレーションシップ保持テーブルの構築
-- 適用先: neodb-postgres コンテナ (docker-entrypoint-initdb.d 経由で初回起動時に自動実行)
-- 注意事項: MySQL 版 (../01_create_bilingual_tables.sql) と同一構造を PostgreSQL 方言で定義したもの
-- 著作権: Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

-- 1. ユーザーマスタテーブル (users)
CREATE TABLE IF NOT EXISTS users (
    user_id       SERIAL PRIMARY KEY,                          -- ユーザーID (自動採番)
    user_code     VARCHAR(20)  NOT NULL UNIQUE,                -- ユーザーコード
    username_en   VARCHAR(100) NOT NULL,                       -- 氏名 (英語)
    username_ja   VARCHAR(100) NOT NULL,                       -- 氏名 (日本語)
    email         VARCHAR(255) NOT NULL,                       -- メールアドレス
    department_ja VARCHAR(100) NOT NULL,                       -- 所属部署 (日本語)
    role_ja       VARCHAR(100) NOT NULL,                       -- 役職 (日本語)
    salary        DECIMAL(12, 2) NOT NULL,                     -- 給与額
    is_active     SMALLINT DEFAULT 1,                          -- 有効フラグ (1:有効 / 0:無効)
    created_at    TIMESTAMP DEFAULT CURRENT_TIMESTAMP          -- 登録日時
);

COMMENT ON TABLE  users               IS 'ユーザーマスタ';
COMMENT ON COLUMN users.user_id       IS 'ユーザーID (自動採番)';
COMMENT ON COLUMN users.user_code     IS 'ユーザーコード';
COMMENT ON COLUMN users.username_en   IS '氏名 (英語)';
COMMENT ON COLUMN users.username_ja   IS '氏名 (日本語)';
COMMENT ON COLUMN users.email         IS 'メールアドレス';
COMMENT ON COLUMN users.department_ja IS '所属部署 (日本語)';
COMMENT ON COLUMN users.role_ja       IS '役職 (日本語)';
COMMENT ON COLUMN users.salary        IS '給与額';
COMMENT ON COLUMN users.is_active     IS '有効フラグ (1:有効 / 0:無効)';
COMMENT ON COLUMN users.created_at    IS '登録日時';

-- 2. 商品マスタテーブル (products)
CREATE TABLE IF NOT EXISTS products (
    product_id      SERIAL PRIMARY KEY,                        -- 商品ID (自動採番)
    sku             VARCHAR(50)  NOT NULL UNIQUE,              -- 商品コード (SKU)
    product_name_en VARCHAR(200) NOT NULL,                     -- 商品名 (英語)
    product_name_ja VARCHAR(200) NOT NULL,                     -- 商品名 (日本語)
    category_ja     VARCHAR(100) NOT NULL,                     -- カテゴリ (日本語)
    unit_price      DECIMAL(10, 2) NOT NULL,                   -- 単価
    stock_quantity  INTEGER NOT NULL,                          -- 在庫数
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP        -- 登録日時
);

COMMENT ON TABLE  products                 IS '商品マスタ';
COMMENT ON COLUMN products.product_id      IS '商品ID (自動採番)';
COMMENT ON COLUMN products.sku             IS '商品コード (SKU)';
COMMENT ON COLUMN products.product_name_en IS '商品名 (英語)';
COMMENT ON COLUMN products.product_name_ja IS '商品名 (日本語)';
COMMENT ON COLUMN products.category_ja     IS 'カテゴリ (日本語)';
COMMENT ON COLUMN products.unit_price      IS '単価';
COMMENT ON COLUMN products.stock_quantity  IS '在庫数';
COMMENT ON COLUMN products.created_at      IS '登録日時';

-- 3. 注文トランザクションテーブル (orders)
CREATE TABLE IF NOT EXISTS orders (
    order_id          SERIAL PRIMARY KEY,                      -- 注文ID (自動採番)
    order_no          VARCHAR(30) NOT NULL UNIQUE,             -- 注文番号
    user_id           INTEGER NOT NULL,                        -- ユーザーID (users.user_id)
    product_id        INTEGER NOT NULL,                        -- 商品ID (products.product_id)
    quantity          INTEGER NOT NULL,                        -- 数量
    unit_price        DECIMAL(10, 2) NOT NULL,                 -- 単価
    total_amount      DECIMAL(12, 2) NOT NULL,                 -- 合計金額
    payment_status_ja VARCHAR(50) NOT NULL,                    -- 決済ステータス (日本語)
    ordered_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,     -- 注文日時
    CONSTRAINT fk_orders_user    FOREIGN KEY (user_id)    REFERENCES users(user_id)       ON DELETE CASCADE,
    CONSTRAINT fk_orders_product FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
);

COMMENT ON TABLE  orders                   IS '注文トランザクション';
COMMENT ON COLUMN orders.order_id          IS '注文ID (自動採番)';
COMMENT ON COLUMN orders.order_no          IS '注文番号';
COMMENT ON COLUMN orders.user_id           IS 'ユーザーID (users.user_id)';
COMMENT ON COLUMN orders.product_id        IS '商品ID (products.product_id)';
COMMENT ON COLUMN orders.quantity          IS '数量';
COMMENT ON COLUMN orders.unit_price        IS '単価';
COMMENT ON COLUMN orders.total_amount      IS '合計金額';
COMMENT ON COLUMN orders.payment_status_ja IS '決済ステータス (日本語)';
COMMENT ON COLUMN orders.ordered_at        IS '注文日時';
