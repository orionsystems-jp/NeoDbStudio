-- ====================================================================
-- NeoDB Studio - Docker DBMS 1,000+ Bilingual Records Seed (DML)
-- ====================================================================
-- 目的: 英語・日本語の1,000件以上のリアルリレーションデータの登録スクリプト
-- 著作権: Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

-- 1. 商品マスタ (products) 初期データの投入
INSERT INTO products (sku, product_name_en, product_name_ja, category_ja, unit_price, stock_quantity) VALUES
('SKU-1001', 'Enterprise Database Cloud Cluster', 'エンタープライズ DB クラウドクラスタ', 'データベース基盤', 1280000.00, 50),
('SKU-1002', 'AI Model Acceleration Processor', 'AI アクセラレータプロセッサ', 'ハードウェア', 450000.00, 120),
('SKU-1003', 'Zero-Trust Security Gateway Module', 'ゼロトラスト セキュリティモジュール', 'ネットワーク', 320000.00, 80),
('SKU-1004', 'High-Speed NVMe Storage Array', '超高速 NVMe ストレージアレイ', 'ストレージ', 680000.00, 45),
('SKU-1005', 'Automated DevOps Pipeline License', '自動化 DevOps パイプラインライセンス', 'ソフトウェア', 150000.00, 500)
ON DUPLICATE KEY UPDATE stock_quantity = VALUES(stock_quantity);

-- 2. 1,000件のユーザーデータ (users) 投入用手続き的データのバッチ登録
-- 再実行時にプロシージャ定義を確実に最新化するため、既存定義は一旦削除してから再作成する
DROP PROCEDURE IF EXISTS SeedBilingualUsersAndOrders;
DELIMITER //
CREATE PROCEDURE SeedBilingualUsersAndOrders()
BEGIN
    DECLARE i INT DEFAULT 1;
    DECLARE u_id INT;
    DECLARE p_id INT;
    DECLARE p_price DECIMAL(10,2);
    DECLARE qty INT;
    DECLARE total DECIMAL(12,2);

    -- ユーザー 1,000 件の登録ループ
    WHILE i <= 1000 DO
        INSERT INTO users (
            user_code, 
            username_en, 
            username_ja, 
            email, 
            department_ja, 
            role_ja, 
            salary, 
            is_active, 
            created_at
        ) VALUES (
            CONCAT('USR-', LPAD(i, 5, '0')),
            CONCAT('Engineer_', i),
            CONCAT('開発エンジニア_', i, ' 氏'),
            CONCAT('engineer_', i, '@orionsystems.jp'),
            ELT((i % 5) + 1, '基盤システム開発部', 'クラウドインフラ部', 'DBA運用統括課', 'セキュリティ監査室', '先端AI研究部'),
            ELT((i % 4) + 1, 'リードアーキテクト', 'シニアデータベースエンジニア', 'スペシャリスト', '運用マネージャー'),
            450000.00 + ((i * 3500) % 450000),
            IF(i % 15 = 0, 0, 1),
            DATE_SUB(NOW(), INTERVAL i HOUR)
        )
        ON DUPLICATE KEY UPDATE email = VALUES(email);

        -- 対応する注文データ (orders) 1,000 件の登録
        -- AUTO_INCREMENT はエンジン/バージョンによりギャップが生じ得るため、ループカウンタ i を
        -- user_id とみなさず、直前に INSERT したユーザーの実際の user_id を user_code で引き直す
        -- （MariaDB で外部キー制約違反が発生した根本原因: i=user_id という前提が常に成立するとは限らない）
        SELECT user_id INTO u_id FROM users WHERE user_code = CONCAT('USR-', LPAD(i, 5, '0'));
        SET p_id = (i % 5) + 1;
        SET qty = (i % 8) + 1;
        SET p_price = ELT(p_id, 1280000.00, 450000.00, 320000.00, 680000.00, 150000.00);
        SET total = qty * p_price;

        INSERT INTO orders (
            order_no, 
            user_id, 
            product_id, 
            quantity, 
            unit_price, 
            total_amount, 
            payment_status_ja, 
            ordered_at
        ) VALUES (
            CONCAT('ORD-DKR-', LPAD(i, 6, '0')),
            u_id,
            p_id,
            qty,
            p_price,
            total,
            ELT((i % 4) + 1, '決済完了 (Paid)', '出荷準備中 (Processing)', '発送済み (Shipped)', 'キャンセル (Cancelled)'),
            DATE_SUB(NOW(), INTERVAL (i * 20) MINUTE)
        )
        ON DUPLICATE KEY UPDATE total_amount = VALUES(total_amount);

        SET i = i + 1;
    END WHILE;
END //
DELIMITER ;

-- プロシージャの実行してデータ生成
CALL SeedBilingualUsersAndOrders();
