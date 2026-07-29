// ====================================================================
// NeoDB Studio - MongoDB バイリンガルコレクション初期投入スクリプト
// ====================================================================
// 目的       : 英語・日本語の 1,000 件超のドキュメントを持つコレクション (users / products / orders) を構築
// 適用先     : neodb-mongodb コンテナ (docker-entrypoint-initdb.d 経由で初回起動時に自動実行)
// 注意事項   : MONGO_INITDB_DATABASE で指定されたデータベース (neodb) に対して実行される。
//              MongoDB はスキーマレスのため、NeoDB Studio のオブジェクトツリーは
//              各コレクションの先頭ドキュメントからフィールド構成を推定して表示する。
// 著作権     : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

// 1. 商品マスタ (products)
const productSeeds = [
    { _id: 1, sku: 'SKU-1001', product_name_en: 'Enterprise Database Cloud Cluster', product_name_ja: 'エンタープライズ DB クラウドクラスタ', category_ja: 'データベース基盤', unit_price: 1280000.0, stock_quantity: 50 },
    { _id: 2, sku: 'SKU-1002', product_name_en: 'AI Model Acceleration Processor',   product_name_ja: 'AI アクセラレータプロセッサ',           category_ja: 'ハードウェア',     unit_price: 450000.0,  stock_quantity: 120 },
    { _id: 3, sku: 'SKU-1003', product_name_en: 'Zero-Trust Security Gateway Module', product_name_ja: 'ゼロトラスト セキュリティモジュール',   category_ja: 'ネットワーク',     unit_price: 320000.0,  stock_quantity: 80 },
    { _id: 4, sku: 'SKU-1004', product_name_en: 'High-Speed NVMe Storage Array',      product_name_ja: '超高速 NVMe ストレージアレイ',         category_ja: 'ストレージ',       unit_price: 680000.0,  stock_quantity: 45 },
    { _id: 5, sku: 'SKU-1005', product_name_en: 'Automated DevOps Pipeline License',  product_name_ja: '自動化 DevOps パイプラインライセンス', category_ja: 'ソフトウェア',     unit_price: 150000.0,  stock_quantity: 500 }
];

db.products.deleteMany({});
db.products.insertMany(productSeeds);

// 2. ユーザー (users) 1,000 件
const departments = ['基盤システム開発部', 'クラウドインフラ部', 'DBA運用統括課', 'セキュリティ監査室', '先端AI研究部'];
const roles       = ['リードアーキテクト', 'シニアデータベースエンジニア', 'スペシャリスト', '運用マネージャー'];
const statuses    = ['決済完了 (Paid)', '出荷準備中 (Processing)', '発送済み (Shipped)', 'キャンセル (Cancelled)'];

const userDocs  = [];
const orderDocs = [];
const now       = new Date();

for (let i = 1; i <= 1000; i++) {
    userDocs.push({
        _id:           i,
        user_code:     'USR-' + String(i).padStart(5, '0'),
        username_en:   'Engineer_' + i,
        username_ja:   '開発エンジニア_' + i + ' 氏',
        email:         'engineer_' + i + '@orionsystems.jp',
        department_ja: departments[i % 5],
        role_ja:       roles[i % 4],
        salary:        450000.0 + ((i * 3500) % 450000),
        is_active:     (i % 15 === 0) ? 0 : 1,
        created_at:    new Date(now.getTime() - i * 3600 * 1000)
    });

    const productId = (i % 5) + 1;
    const quantity  = (i % 8) + 1;
    const unitPrice = productSeeds[productId - 1].unit_price;

    orderDocs.push({
        _id:               i,
        order_no:          'ORD-DKR-' + String(i).padStart(6, '0'),
        user_id:           i,
        product_id:        productId,
        quantity:          quantity,
        unit_price:        unitPrice,
        total_amount:      quantity * unitPrice,
        payment_status_ja: statuses[i % 4],
        ordered_at:        new Date(now.getTime() - i * 20 * 60 * 1000)
    });
}

db.users.deleteMany({});
db.users.insertMany(userDocs);

db.orders.deleteMany({});
db.orders.insertMany(orderDocs);

// 3. 検索用インデックスの作成
db.users.createIndex({ user_code: 1 }, { unique: true });
db.orders.createIndex({ order_no: 1 }, { unique: true });
db.orders.createIndex({ user_id: 1 });

print('[NeoDB Studio] MongoDB seed completed: users=' + db.users.countDocuments()
      + ', products=' + db.products.countDocuments()
      + ', orders=' + db.orders.countDocuments());
