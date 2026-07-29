// ファイル名     : TableDesignColumn.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\TableDesignColumn.cs
// クラス/概要    : TableDesignColumn (Class)
// 処理概要/目的  : テーブル構造デザイナー画面において、1つのカラム定義情報（名前、型、PK、Null可、デフォルト値、コメント）を管理・通知するモデル
// 使用方法/適用先: TableDesignerViewModel の Columns コレクション要素としてバインド
// 依存関係       : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
// 注意事項       : プロパティ変更時に ViewModel 経由で UI へ動的同期通知します。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NeoDbStudio.Client.Models;

#region TableDesignColumn Class

/// <summary>
/// テーブル構造設計画面用カラムモデル。
/// </summary>
public partial class TableDesignColumn : ObservableObject
{
    #region Fields & Properties

    [ObservableProperty]
    private string _columnName = "new_column"; // カラム名バッキングフィールド

    [ObservableProperty]
    private string _dataType = "VARCHAR(255)"; // データ型バッキングフィールド

    [ObservableProperty]
    private bool _isPrimaryKey; // 主キーフラグバッキングフィールド

    [ObservableProperty]
    private bool _allowNull = true; // Null許可フラグバッキングフィールド

    [ObservableProperty]
    private string _defaultValue = string.Empty; // 初期値バッキングフィールド

    [ObservableProperty]
    private string _comment = string.Empty; // コメントバッキングフィールド

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// TableDesignColumn インスタンスの既定コンストラクタです。
    /// </summary>
    public TableDesignColumn()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignColumn.ctor: 初期化を開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignColumn.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignColumn.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// カラム名、型、PKフラグ、Null許可を指定して TableDesignColumn インスタンスを初期化します。
    /// </summary>
    /// <param name="name">[パラメータ] カラム名を指定します。</param>
    /// <param name="type">[パラメータ] データ型を指定します。</param>
    /// <param name="isPk">[パラメータ] 主キーフラグを指定します。</param>
    /// <param name="allowNull">[パラメータ] Null許可フラグを指定します。</param>
    public TableDesignColumn(string name, string type, bool isPk = false, bool allowNull = true)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] TableDesignColumn.ctor(params): 開始します (name={name})");

            _columnName   = name ?? throw new ArgumentNullException(nameof(name)); // NULL検証（?? 演算子）
            _dataType     = type ?? "VARCHAR(255)";                                // データ型の設定（?? 演算子）
            _isPrimaryKey = isPk;                                                 // 主キーフラグの設定
            _allowNull    = allowNull;                                            // Null許可フラグの設定

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignColumn.ctor(params): 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignColumn.ctor(params): 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
