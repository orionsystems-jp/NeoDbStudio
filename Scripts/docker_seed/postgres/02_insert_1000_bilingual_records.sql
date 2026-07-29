-- ====================================================================
-- NeoDB Studio - PostgreSQL 1,000件バイリンガルレコード投入 (DML)
-- ====================================================================
-- 目的: 英語・日本語の1,000件以上のリアルリレーションデータの登録
-- 適用先: neodb-postgres コンテナ (docker-entrypoint-initdb.d 経由で初回起動時に自動実行)
-- 注意事項: MySQL 版 (../02_insert_1000_bilingual_records.sql) と同一データを PostgreSQL 方言で生成したもの。
--           MySQL 版は WHILE ループのストアドプロシージャで生成しているが、本ファイルは
--           generate_series による集合演算で同等のデータを生成する。
-- 著作権: Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

-- 1. 商品マスタ (products) 初期データの投入
INSERT INTO products (sku, product_name_en, product_name_ja, category_ja, unit_price, stock_quantity) VALUES
('SKU-1001', 'Enterprise Database Cloud Cluster', 'エンタープライズ DB クラウドクラスタ', 'データベース基盤', 1280000.00, 50),
('SKU-1002', 'AI Model Acceleration Processor',   'AI アクセラレータプロセッサ',           'ハードウェア',     450000.00, 120),
('SKU-1003', 'Zero-Trust Security Gateway Module', 'ゼロトラスト セキュリティモジュール',   'ネットワーク',     320000.00, 80),
('SKU-1004', 'High-Speed NVMe Storage Array',      '超高速 NVMe ストレージアレイ',         'ストレージ',       680000.00, 45),
('SKU-1005', 'Automated DevOps Pipeline License',  '自動化 DevOps パイプラインライセンス', 'ソフトウェア',     150000.00, 500)
ON CONFLICT (sku) DO UPDATE SET stock_quantity = EXCLUDED.stock_quantity;

-- 2. ユーザーデータ (users) 1,000 件の投入
INSERT INTO users (user_code, username_en, username_ja, email, department_ja, role_ja, salary, is_active, created_at)
SELECT
    'USR-' || LPAD(i::text, 5, '0'),
    'Engineer_' || i,
    '開発エンジニア_' || i || ' 氏',
    'engineer_' || i || '@orionsystems.jp',
    (ARRAY['基盤システム開発部', 'クラウドインフラ部', 'DBA運用統括課', 'セキュリティ監査室', '先端AI研究部'])[(i % 5) + 1],
    (ARRAY['リードアーキテクト', 'シニアデータベースエンジニア', 'スペシャリスト', '運用マネージャー'])[(i % 4) + 1],
    450000.00 + ((i * 3500) % 450000),
    CASE WHEN i % 15 = 0 THEN 0 ELSE 1 END,
    CURRENT_TIMESTAMP - (i || ' hours')::interval
FROM generate_series(1, 1000) AS s(i)
ON CONFLICT (user_code) DO UPDATE SET email = EXCLUDED.email;

-- 3. 注文データ (orders) 1,000 件の投入（users / products への外部キーを保持）
INSERT INTO orders (order_no, user_id, product_id, quantity, unit_price, total_amount, payment_status_ja, ordered_at)
SELECT
    'ORD-DKR-' || LPAD(i::text, 6, '0'),
    i                                                             AS user_id,
    (i % 5) + 1                                                   AS product_id,
    (i % 8) + 1                                                   AS quantity,
    (ARRAY[1280000.00, 450000.00, 320000.00, 680000.00, 150000.00])[(i % 5) + 1] AS unit_price,
    ((i % 8) + 1) * (ARRAY[1280000.00, 450000.00, 320000.00, 680000.00, 150000.00])[(i % 5) + 1] AS total_amount,
    (ARRAY['決済完了 (Paid)', '出荷準備中 (Processing)', '発送済み (Shipped)', 'キャンセル (Cancelled)'])[(i % 4) + 1],
    CURRENT_TIMESTAMP - ((i * 20) || ' minutes')::interval
FROM generate_series(1, 1000) AS s(i)
ON CONFLICT (order_no) DO UPDATE SET total_amount = EXCLUDED.total_amount;
