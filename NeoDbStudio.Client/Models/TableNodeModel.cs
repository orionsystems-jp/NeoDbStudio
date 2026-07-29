// ファイル名     : TableNodeModel.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\TableNodeModel.cs
// クラス/概要    : ColumnModel (Class), TableNodeModel (Class), RootDiagramModel (Class)
// 処理概要/目的  : MSAGL ER図モデラーおよび UndoRedoKit 管理下のノード・カラム・ルートダイアグラムのデータモデル定義
// 使用方法/適用先: UndoRedoKit 変更追跡マネージャー (EditContext) にアタッチしてモデリングおよび元に戻す/やり直す機能を提供
// 依存関係       : OrionSystems.UndoRedoKit.ModelObject, OrionSystems.UndoRedoKit.ModelCollection
// 注意事項       : ModelCollection へのアタッチは所有親オブジェクト (this) の参照を渡す必要があります。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using OrionSystems.UndoRedoKit;

namespace NeoDbStudio.Client.Models;

#region ColumnModel Class

/// <summary>
/// ER図ノード内の1カラムを表す UndoRedo 追跡対応モデル。
/// </summary>
public class ColumnModel : ModelObject
{
    #region Fields & Properties

    private string _name = string.Empty; // カラム名バッキングフィールド

    /// <summary>カラム名。</summary>
    public string Name
    {
        get
        {
            return _name; // 名称の取得
        }
        set
        {
            SetValue(ref _name, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// ColumnModel インスタンスの既定コンストラクタです。
    /// </summary>
    public ColumnModel()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ColumnModel.ctor: 開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] ColumnModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ColumnModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// カラム名を指定して ColumnModel インスタンスを初期化します。
    /// </summary>
    /// <param name="name">[パラメータ] カラム名を指定します。</param>
    public ColumnModel(string name)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] ColumnModel.ctor(name): 開始します (name={name})");
            _name = name ?? throw new ArgumentNullException(nameof(name)); // NULL検証（?? 演算子）
            System.Diagnostics.Debug.WriteLine("[INFO] ColumnModel.ctor(name): 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ColumnModel.ctor(name): 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 文字列表現を取得します。
    /// </summary>
    /// <returns>カラム名文字列を返却します。</returns>
    public override string ToString()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ColumnModel.ToString: 開始します");
            var res = Name; // 名称の参照
            System.Diagnostics.Debug.WriteLine("[INFO] ColumnModel.ToString: 正常終了しました");
            return res;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ColumnModel.ToString: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion

#region TableNodeModel Class

/// <summary>
/// ER図キャンバス上の1テーブルノードを表す UndoRedo 追跡対応モデル。
/// </summary>
public class TableNodeModel : ModelObject
{
    #region Fields & Properties

    private string _tableName    = string.Empty; // テーブル名バッキングフィールド
    private double _x;                           // X 座標バッキングフィールド
    private double _y;                           // Y 座標バッキングフィールド
    private string _fillColorHex = "#87CEFA";    // 背景色 Hex バッキングフィールド
    private bool   _isView;                      // ビュー識別フラグバッキングフィールド（true: ビュー由来 / false: 実テーブル）

    /// <summary>テーブル名。</summary>
    public string TableName
    {
        get
        {
            return _tableName; // テーブル名の取得
        }
        set
        {
            SetValue(ref _tableName, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    /// <summary>キャンバス上の X 座標。</summary>
    public double X
    {
        get
        {
            return _x; // X座標の取得
        }
        set
        {
            SetValue(ref _x, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    /// <summary>キャンバス上の Y 座標。</summary>
    public double Y
    {
        get
        {
            return _y; // Y座標の取得
        }
        set
        {
            SetValue(ref _y, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    /// <summary>ノード背景色の Hex 文字列。</summary>
    public string FillColorHex
    {
        get
        {
            return _fillColorHex; // 背景色の取得
        }
        set
        {
            SetValue(ref _fillColorHex, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    /// <summary>ビュー由来のノードかどうか（true: DB上のVIEWから生成 / false: 実テーブルまたは手動作成）。</summary>
    public bool IsView
    {
        get
        {
            return _isView; // ビュー識別フラグの取得
        }
        set
        {
            SetValue(ref _isView, value); // UndoRedo 追跡付き値変更（SetValue）
        }
    }

    /// <summary>所有するカラムモデルコレクション。</summary>
    public ModelCollection<ColumnModel> Columns { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// TableNodeModel インスタンスの既定コンストラクタです。自身を所有者として Columns コレクションを初期化します。
    /// </summary>
    public TableNodeModel()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableNodeModel.ctor: 開始します");

            // 親オブジェクト (this) を指定して UndoRedo 対応コレクションを初期化
            Columns = new ModelCollection<ColumnModel>(this, nameof(Columns));

            System.Diagnostics.Debug.WriteLine("[INFO] TableNodeModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableNodeModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// テーブル名を指定して TableNodeModel インスタンスを初期化します。
    /// </summary>
    /// <param name="name">[パラメータ] テーブル名を指定します。</param>
    public TableNodeModel(string name) : this()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] TableNodeModel.ctor(name): 開始します (name={name})");
            _tableName = name ?? throw new ArgumentNullException(nameof(name)); // NULL検証（?? 演算子）
            System.Diagnostics.Debug.WriteLine("[INFO] TableNodeModel.ctor(name): 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableNodeModel.ctor(name): 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion

#region RootDiagramModel Class

/// <summary>
/// ER図全体のルートを表す UndoRedo 追跡対応親モデル。
/// </summary>
public class RootDiagramModel : ModelObject
{
    #region Properties

    /// <summary>全体のテーブルノードコレクション。</summary>
    public ModelCollection<TableNodeModel> Tables { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// RootDiagramModel インスタンスの既定コンストラクタです。自身を所有者として Tables コレクションを初期化します。
    /// </summary>
    public RootDiagramModel()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] RootDiagramModel.ctor: 開始します");

            // 親オブジェクト (this) を指定して UndoRedo 対応コレクションを初期化
            Tables = new ModelCollection<TableNodeModel>(this, nameof(Tables));

            System.Diagnostics.Debug.WriteLine("[INFO] RootDiagramModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] RootDiagramModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
