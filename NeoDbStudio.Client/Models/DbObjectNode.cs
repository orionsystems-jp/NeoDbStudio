// ファイル名     : DbObjectNode.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\DbObjectNode.cs
// クラス/概要    : DbObjectNode (Class), DbObjectType (Enum)
// 処理概要/目的  : DBMS オブジェクトエクスプローラー（階層ツリー）に表示するデータベース、スキーマ、テーブル、カラム等のノード要素データモデル
// 使用方法/適用先: MainWindow の DBMS Object Explorer TreeView へデータバインドしてツリー表示
// 依存関係       : System.Collections.ObjectModel.ObservableCollection
// 注意事項       : 階層構造を保持するため Children コレクションを内部で自動生成します。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.ObjectModel;

namespace NeoDbStudio.Client.Models;

#region DbObjectType Enum

/// <summary>
/// DBMS オブジェクトの種別を定義する列挙型。
/// </summary>
public enum DbObjectType
{
    Database,  // データベース
    Schema,    // スキーマ
    Folder,    // フォルダ（分類用）
    Table,     // テーブル
    Column,    // カラム
    View,      // ビュー
    Procedure, // ストアドプロシージャ
    Index      // インデックス
}

#endregion

#region DbObjectNode Class

/// <summary>
/// DBMS オブジェクトエクスプローラーの階層ツリーノードモデル。
/// </summary>
public class DbObjectNode
{
    #region Fields & Properties

    private string           _name         = string.Empty;       // オブジェクト名バッキングフィールド
    private DbObjectType     _objectType   = DbObjectType.Table; // オブジェクト種別バッキングフィールド
    private string           _icon         = "📁";               // ツリー表示用アイコン文字
    private string           _dataType     = string.Empty;       // カラムデータ型
    private bool             _isPrimaryKey;                      // 主キーフラグ
    private bool             _isForeignKey;                      // 外来キーフラグ

    /// <summary>オブジェクト名。</summary>
    public string Name
    {
        get
        {
            return _name; // 名称の取得
        }
        set
        {
            _name = value ?? string.Empty; // NULLガード代入（?? 演算子）
        }
    }

    /// <summary>オブジェクト種別。</summary>
    public DbObjectType ObjectType
    {
        get
        {
            return _objectType; // 種別の取得
        }
        set
        {
            _objectType = value; // 種別の設定
        }
    }

    /// <summary>オブジェクト種別エイリアス (Type)。</summary>
    public DbObjectType Type
    {
        get
        {
            return _objectType; // 種別の取得
        }
        set
        {
            _objectType = value; // 種別の設定
        }
    }

    /// <summary>ツリー表示用アイコン文字。</summary>
    public string Icon
    {
        get
        {
            return _icon; // アイコンの取得
        }
        set
        {
            _icon = value ?? "📁"; // NULLガード代入（?? 演算子）
        }
    }

    /// <summary>カラムデータ型（型名）。</summary>
    public string DataType
    {
        get
        {
            return _dataType; // データ型の取得
        }
        set
        {
            _dataType = value ?? string.Empty; // NULLガード代入（?? 演算子）
        }
    }

    /// <summary>主キーフラグ。</summary>
    public bool IsPrimaryKey
    {
        get
        {
            return _isPrimaryKey; // 主キー判定フラグの取得
        }
        set
        {
            _isPrimaryKey = value; // 主キー判定フラグの設定
        }
    }

    /// <summary>外来キーフラグ。</summary>
    public bool IsForeignKey
    {
        get
        {
            return _isForeignKey; // 外来キー判定フラグの取得
        }
        set
        {
            _isForeignKey = value; // 外来キー判定フラグの設定
        }
    }

    /// <summary>子階層ノード要素コレクション。</summary>
    public ObservableCollection<DbObjectNode> Children { get; } = new();

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// DbObjectNode インスタンスの既定コンストラクタです。
    /// </summary>
    public DbObjectNode()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] DbObjectNode.ctor: 初期化を開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] DbObjectNode.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DbObjectNode.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 名称および種別を指定して DbObjectNode インスタンスを初期化します。
    /// </summary>
    /// <param name="name">[パラメータ] ノード表示名称を指定します。</param>
    /// <param name="type">[パラメータ] オブジェクト種別を指定します。</param>
    /// <param name="icon">[パラメータ] アイコン文字を指定します。</param>
    public DbObjectNode(string name, DbObjectType type, string icon = "📁")
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] DbObjectNode.ctor(name, type): 開始します (name={name})");

            _name       = name ?? throw new ArgumentNullException(nameof(name)); // NULL検証（?? 演算子）
            _objectType = type;                                                  // 種別の適用
            _icon       = icon ?? "📁";                                          // アイコンの適用

            System.Diagnostics.Debug.WriteLine("[INFO] DbObjectNode.ctor(name, type): 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DbObjectNode.ctor(name, type): 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
