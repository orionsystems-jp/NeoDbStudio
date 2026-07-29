#!/usr/bin/env python3
# ====================================================================
# NeoDB Studio - SQLite サンプル DB 生成スクリプト
# ====================================================================
# ファイル名     : create_db.py
# ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\Scripts\sqlite_seed\create_db.py
# 処理概要/目的  : 英語・日本語 1,000 件超のバイリンガルデータを持つ SQLite DB ファイルを生成する
# 使用方法       : python create_db.py   （Scripts\apply_seed.ps1 からも呼び出される）
# 依存関係       : Python 3 標準ライブラリ sqlite3 のみ（外部パッケージ不要）
# 注意事項       : 生成先は本スクリプトと同一ディレクトリの neodb_sqlite.db。
#                  既存ファイルがある場合は削除して作り直す。
# 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

import os
import sqlite3
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH    = os.path.join(SCRIPT_DIR, "neodb_sqlite.db")

DEPARTMENTS = ["基盤システム開発部", "クラウドインフラ部", "DBA運用統括課", "セキュリティ監査室", "先端AI研究部"]
ROLES       = ["リードアーキテクト", "シニアデータベースエンジニア", "スペシャリスト", "運用マネージャー"]
STATUSES    = ["決済完了 (Paid)", "出荷準備中 (Processing)", "発送済み (Shipped)", "キャンセル (Cancelled)"]

PRODUCTS = [
    ("SKU-1001", "Enterprise Database Cloud Cluster",  "エンタープライズ DB クラウドクラスタ", "データベース基盤", 1280000.0, 50),
    ("SKU-1002", "AI Model Acceleration Processor",    "AI アクセラレータプロセッサ",           "ハードウェア",     450000.0, 120),
    ("SKU-1003", "Zero-Trust Security Gateway Module", "ゼロトラスト セキュリティモジュール",   "ネットワーク",     320000.0, 80),
    ("SKU-1004", "High-Speed NVMe Storage Array",      "超高速 NVMe ストレージアレイ",         "ストレージ",       680000.0, 45),
    ("SKU-1005", "Automated DevOps Pipeline License",  "自動化 DevOps パイプラインライセンス", "ソフトウェア",     150000.0, 500),
]

SCHEMA_SQL = """
CREATE TABLE users (
    user_id       INTEGER PRIMARY KEY AUTOINCREMENT,
    user_code     TEXT NOT NULL UNIQUE,
    username_en   TEXT NOT NULL,
    username_ja   TEXT NOT NULL,
    email         TEXT NOT NULL,
    department_ja TEXT NOT NULL,
    role_ja       TEXT NOT NULL,
    salary        REAL NOT NULL,
    is_active     INTEGER DEFAULT 1,
    created_at    TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE products (
    product_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    sku             TEXT NOT NULL UNIQUE,
    product_name_en TEXT NOT NULL,
    product_name_ja TEXT NOT NULL,
    category_ja     TEXT NOT NULL,
    unit_price      REAL NOT NULL,
    stock_quantity  INTEGER NOT NULL,
    created_at      TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE orders (
    order_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    order_no          TEXT NOT NULL UNIQUE,
    user_id           INTEGER NOT NULL,
    product_id        INTEGER NOT NULL,
    quantity          INTEGER NOT NULL,
    unit_price        REAL NOT NULL,
    total_amount      REAL NOT NULL,
    payment_status_ja TEXT NOT NULL,
    ordered_at        TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id)    REFERENCES users(user_id)       ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
);
"""


def main():
    """SQLite サンプル DB を生成する。"""
    try:
        if os.path.exists(DB_PATH):
            os.remove(DB_PATH)  # 既存ファイルは作り直す

        conn = sqlite3.connect(DB_PATH)
        try:
            conn.executescript(SCHEMA_SQL)

            conn.executemany(
                "INSERT INTO products (sku, product_name_en, product_name_ja, category_ja, unit_price, stock_quantity)"
                " VALUES (?, ?, ?, ?, ?, ?)",
                PRODUCTS,
            )

            users = []
            orders = []
            for i in range(1, 1001):
                users.append((
                    f"USR-{i:05d}",
                    f"Engineer_{i}",
                    f"開発エンジニア_{i} 氏",
                    f"engineer_{i}@orionsystems.jp",
                    DEPARTMENTS[i % 5],
                    ROLES[i % 4],
                    450000.0 + ((i * 3500) % 450000),
                    0 if i % 15 == 0 else 1,
                ))

                product_id = (i % 5) + 1
                quantity   = (i % 8) + 1
                unit_price = PRODUCTS[product_id - 1][4]
                orders.append((
                    f"ORD-DKR-{i:06d}",
                    i,
                    product_id,
                    quantity,
                    unit_price,
                    quantity * unit_price,
                    STATUSES[i % 4],
                ))

            conn.executemany(
                "INSERT INTO users (user_code, username_en, username_ja, email, department_ja, role_ja, salary, is_active)"
                " VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                users,
            )
            conn.executemany(
                "INSERT INTO orders (order_no, user_id, product_id, quantity, unit_price, total_amount, payment_status_ja)"
                " VALUES (?, ?, ?, ?, ?, ?, ?)",
                orders,
            )
            conn.commit()

            counts = {
                name: conn.execute(f"SELECT COUNT(*) FROM {name}").fetchone()[0]
                for name in ("users", "products", "orders")
            }
        finally:
            conn.close()

        print(f"[NeoDB Studio] SQLite DB generated: {DB_PATH}")
        print(f"[NeoDB Studio] rows: {counts}")
        return 0
    except Exception as ex:  # noqa: BLE001 - 生成失敗の理由を利用者へ明示するため広く捕捉する
        print(f"[ERROR] SQLite DB の生成に失敗しました - {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
