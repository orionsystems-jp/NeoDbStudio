// ファイル名     : TableDesignerViewModel.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\ViewModels\TableDesignerViewModel.cs
// クラス/概要    : TableDesignerViewModel (Class)
// 処理概要/目的  : テーブル構造デザイナー画面（タブ）1枚分のビューモデル。テーブル名、カラム一覧 (TableDesignColumn) の動的追加・削除およびログ発火を提供
// 使用方法/適用先: MainViewModel の TableDesigners コレクション要素としてバインド
// 依存関係       : CommunityToolkit.Mvvm.ComponentModel, NeoDbStudio.Client.Models.TableDesignColumn
// 注意事項       : カラムの追加・削除時に親システムログへ通知イベントを発火します。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.ViewModels;

#region TableDesignerViewModel Class

/// <summary>
/// 個別テーブル構造デザイナー用 ViewModel。
/// </summary>
public partial class TableDesignerViewModel : ObservableObject
{
    #region Fields & Properties

    [ObservableProperty]
    private string _tableName = "NewTable"; // テーブル名バッキングフィールド

    [ObservableProperty]
    private string _title = "Table Designer: NewTable"; // タブタイトルバッキングフィールド

    /// <summary>デザイン対象のカラム定義コレクション。</summary>
    public ObservableCollection<TableDesignColumn> Columns { get; } = new();

    /// <summary>操作ログ発生時に通知するイベントデリゲート（ログメッセージ）。</summary>
    public event Action<string>? LogNotification;

    /// <summary>実スキーマ読込時点でのカラム状態スナップショット（ALTER TABLE差分生成の比較基準）。</summary>
    private List<(string Name, string DataType, bool IsPrimaryKey)> _originalColumns = new();

    /// <summary>実スキーマから読み込まれたかどうか（true: 実テーブル / false: 新規作成・サンプル）。</summary>
    public bool IsLoadedFromRealSchema { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// 対象テーブル名を指定して TableDesignerViewModel インスタンスを初期化し、サンプルカラムを設定します。
    /// </summary>
    /// <param name="tableName">[パラメータ] 編集対象のテーブル名を指定します。</param>
    public TableDesignerViewModel(string tableName = "NewTable")
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] TableDesignerViewModel.ctor: 開始します (tableName={tableName})");

            _tableName = tableName ?? "NewTable";          // テーブル名のセット（?? 演算子）
            _title     = $"🛠 Table Designer: {_tableName}"; // タブタイトルの生成（$ 補間）

            // 初期サンプルカラムの設定
            Columns.Add(new TableDesignColumn("id", "INT", true, false));
            Columns.Add(new TableDesignColumn("created_at", "TIMESTAMP", false, true));

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerViewModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Real Schema Loading & ALTER TABLE Generation

    /// <summary>
    /// [1. 処理概要]
    /// 実テーブルのスキーマ（TableSchema）からカラム一覧を読み込み、デザイナーの初期状態とします。
    /// 以後の変更検出（ALTER TABLE 差分生成）は、この時点のカラム構成を基準に行います。
    /// </summary>
    /// <param name="schema">[パラメータ] 実テーブルのスキーマ情報を指定します。</param>
    public void LoadFromSchema(TableSchema schema)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] TableDesignerViewModel.LoadFromSchema: 開始します (table={schema?.Name})");

            if (schema == null) // NULL 検証
            {
                throw new ArgumentNullException(nameof(schema));
            }

            Columns.Clear();
            _originalColumns.Clear();

            foreach (var col in schema.Columns)
            {
                Columns.Add(new TableDesignColumn(col.Name, col.DataType, col.IsPrimaryKey, col.IsNullable));
                _originalColumns.Add((col.Name, col.DataType, col.IsPrimaryKey));
            }

            IsLoadedFromRealSchema = true;

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.LoadFromSchema: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerViewModel.LoadFromSchema: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 実スキーマ読込時点（_originalColumns）と現在の Columns を比較し、追加・削除・型変更カラムを検出して
    /// DBMS方言別の ALTER TABLE スクリプトを生成します。
    ///
    /// [2. 処理フロー]
    /// 1. 現在のカラム名集合と元のカラム名集合の差分から、追加列・削除列を特定します。
    /// 2. 両方に存在するカラムでデータ型が異なるものを型変更列として特定します。
    /// 3. 変更が1件も無ければ null を返却します。
    /// </summary>
    /// <param name="providerType">[パラメータ] 対象 DBMS のプロバイダー種別を指定します。</param>
    /// <returns>生成された ALTER TABLE スクリプト文字列、変更が無い場合は null を返却します。</returns>
    public string? GenerateAlterTableScript(string providerType)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.GenerateAlterTableScript: 開始します");

            var originalByName = _originalColumns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            var currentNames    = new HashSet<string>(Columns.Select(c => c.ColumnName), StringComparer.OrdinalIgnoreCase);

            var added    = Columns.Where(c => !originalByName.ContainsKey(c.ColumnName)).ToList();
            var removed  = _originalColumns.Where(o => !currentNames.Contains(o.Name)).ToList();
            var modified = Columns
                .Where(c => originalByName.TryGetValue(c.ColumnName, out var o) && !string.Equals(o.DataType, c.DataType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (added.Count == 0 && removed.Count == 0 && modified.Count == 0) // 変更なし
            {
                return null;
            }

            string dialect = (providerType ?? string.Empty).ToLowerInvariant();
            var sb = new StringBuilder();
            sb.AppendLine($"-- Generated ALTER TABLE for {TableName} ({providerType})");
            sb.AppendLine("-- 実行前に内容をご確認ください（本ツールは自動実行しません）");
            sb.AppendLine();

            foreach (var col in added)
            {
                sb.AppendLine(BuildAddColumnStatement(dialect, TableName, col));
            }
            foreach (var col in removed)
            {
                sb.AppendLine($"ALTER TABLE {TableName} DROP COLUMN {col.Name};");
            }
            foreach (var col in modified)
            {
                string? stmt = BuildModifyColumnStatement(dialect, TableName, col);
                sb.AppendLine(stmt ?? $"-- [警告] {providerType} はカラム型変更のALTER文を安全に自動生成できません（{col.ColumnName}）。手動での対応を推奨します。");
            }

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.GenerateAlterTableScript: 正常終了しました");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerViewModel.GenerateAlterTableScript: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// DBMS方言別の ADD COLUMN 文を組み立てます（SQL Server / Oracle は COLUMN キーワードを許容しないため分岐）。
    /// </summary>
    public static string BuildAddColumnStatement(string dialect, string tableName, TableDesignColumn col)
    {
        string nullClause = col.AllowNull ? string.Empty : " NOT NULL";
        return dialect switch
        {
            "mssql" or "sqlserver" or "sql server" or "oracle"
                => $"ALTER TABLE {tableName} ADD {col.ColumnName} {col.DataType}{nullClause};",
            _ => $"ALTER TABLE {tableName} ADD COLUMN {col.ColumnName} {col.DataType}{nullClause};"
        };
    }

    /// <summary>
    /// DBMS方言別のカラム型変更文を組み立てます。SQLite は ALTER TABLE によるカラム型変更自体に非対応のため null を返却します。
    /// </summary>
    public static string? BuildModifyColumnStatement(string dialect, string tableName, TableDesignColumn col)
    {
        return dialect switch
        {
            "mysql" or "mariadb"                     => $"ALTER TABLE {tableName} MODIFY COLUMN {col.ColumnName} {col.DataType};",
            "postgresql"                              => $"ALTER TABLE {tableName} ALTER COLUMN {col.ColumnName} TYPE {col.DataType};",
            "mssql" or "sqlserver" or "sql server"    => $"ALTER TABLE {tableName} ALTER COLUMN {col.ColumnName} {col.DataType};",
            "oracle"                                  => $"ALTER TABLE {tableName} MODIFY {col.ColumnName} {col.DataType};",
            _ => null // SQLite 等、ALTER によるカラム型変更が構文上存在しない DBMS
        };
    }

    #endregion

    #region Commands & Internal Logic

    /// <summary>
    /// [1. 処理概要]
    /// カラムコレクションに新しい定義項目（デフォルト型: VARCHAR(255)）を追加し、ログ通知を発火します。
    /// </summary>
    [RelayCommand]
    private void AddColumn()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.AddColumn: 開始します");

            var newCol = new TableDesignColumn($"col_{Columns.Count + 1}", "VARCHAR(255)", false, true); // 新規カラム生成
            Columns.Add(newCol); // コレクションへ追加
            LogNotification?.Invoke($"Added new column to {TableName}."); // ログ発火（? 演算子）

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.AddColumn: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerViewModel.AddColumn: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 指定されたカラム定義項目をコレクションから削除し、ログ通知を発火します。
    /// </summary>
    /// <param name="column">[パラメータ] 削除対象のカラムオブジェクトを指定します。</param>
    [RelayCommand]
    private void RemoveColumn(TableDesignColumn column)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.RemoveColumn: 開始します");

            if (column != null && Columns.Contains(column)) // 存在検証（論理 &&）
            {
                Columns.Remove(column); // コレクションから削除
                LogNotification?.Invoke($"Removed column '{column.ColumnName}' from {TableName}."); // ログ発火（? 演算子）
            }

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerViewModel.RemoveColumn: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerViewModel.RemoveColumn: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
