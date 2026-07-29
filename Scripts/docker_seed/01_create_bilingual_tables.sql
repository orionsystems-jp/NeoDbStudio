-- ====================================================================
-- NeoDB Studio - Docker DBMS Multi-Language Relational Schema (DDL)
-- ====================================================================
-- 目的: 英語・日本語の多言語データ（UTF-8）に対応したリレーションシップ保持テーブルの構築
-- 著作権: Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

-- 1. ユーザーマスタテーブル (users)
CREATE TABLE IF NOT EXISTS users (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    user_code VARCHAR(20) NOT NULL UNIQUE,
    username_en VARCHAR(100) NOT NULL,
    username_ja VARCHAR(100) NOT NULL,
    email VARCHAR(255) NOT NULL,
    department_ja VARCHAR(100) NOT NULL,
    role_ja VARCHAR(100) NOT NULL,
    salary DECIMAL(12, 2) NOT NULL,
    is_active TINYINT(1) DEFAULT 1,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. 商品マスタテーブル (products)
CREATE TABLE IF NOT EXISTS products (
    product_id INT AUTO_INCREMENT PRIMARY KEY,
    sku VARCHAR(50) NOT NULL UNIQUE,
    product_name_en VARCHAR(200) NOT NULL,
    product_name_ja VARCHAR(200) NOT NULL,
    category_ja VARCHAR(100) NOT NULL,
    unit_price DECIMAL(10, 2) NOT NULL,
    stock_quantity INT NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. 注文トランザクションテーブル (orders)
CREATE TABLE IF NOT EXISTS orders (
    order_id INT AUTO_INCREMENT PRIMARY KEY,
    order_no VARCHAR(30) NOT NULL UNIQUE,
    user_id INT NOT NULL,
    product_id INT NOT NULL,
    quantity INT NOT NULL,
    unit_price DECIMAL(10, 2) NOT NULL,
    total_amount DECIMAL(12, 2) NOT NULL,
    payment_status_ja VARCHAR(50) NOT NULL,
    ordered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_orders_user FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    CONSTRAINT fk_orders_product FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
