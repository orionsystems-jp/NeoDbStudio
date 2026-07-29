// ファイル名     : SchemaDiffResult.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\SchemaDiffResult.cs
// クラス/概要    : ColumnDiff (Class), TableDiff (Class), SchemaDiffResult (Class)
// 処理概要/目的  : 2つのDBMSスキーマ（ソース・ターゲット）間の比較結果（テーブル・カラム差分）を保持するデータモデル
// 使用方法/適用先: NeoDbStudio.Client.Helpers.SchemaComparer の戻り値として、SchemaDiffDialog の表示に使用
// 依存関係       : System.Collections.Generic
// 更新履歴       : 2026/07/29 新規作成（スキーマ比較機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System.Collections.Generic;

namespace NeoDbStudio.Client.Models;

#region ColumnDiff Class

/// <summary>
/// 1カラム分の差分情報（追加・削除・型変更のいずれか）。
/// </summary>
public class ColumnDiff
{
    /// <summary>カラム名。</summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>差分種別（"Added" / "Removed" / "Modified"）。</summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>ソース側のデータ型（Removed の場合は空）。</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>ターゲット側のデータ型（Added の場合は空）。</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>一覧表示用の要約文字列。</summary>
    public string Summary => ChangeType switch
    {
        "Added"    => $"➕ {ColumnName}  ({SourceType})  — ソースのみに存在",
        "Removed"  => $"➖ {ColumnName}  ({TargetType})  — ターゲットのみに存在",
        "Modified" => $"✏ {ColumnName}  {TargetType} → {SourceType}",
        _          => ColumnName
    };
}

#endregion

#region TableDiff Class

/// <summary>
/// 1テーブル分のカラム差分一覧（両スキーマに共通して存在するテーブルが対象）。
/// </summary>
public class TableDiff
{
    /// <summary>対象テーブル名。</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>カラム差分一覧。</summary>
    public List<ColumnDiff> ColumnDiffs { get; set; } = new();

    /// <summary>差分が1件でも存在するかどうか。</summary>
    public bool HasDifferences => ColumnDiffs.Count > 0;
}

#endregion

#region SchemaDiffResult Class

/// <summary>
/// スキーマ比較の全体結果。
/// </summary>
public class SchemaDiffResult
{
    /// <summary>ソースのみに存在するテーブル名一覧。</summary>
    public List<string> TablesOnlyInSource { get; set; } = new();

    /// <summary>ターゲットのみに存在するテーブル名一覧。</summary>
    public List<string> TablesOnlyInTarget { get; set; } = new();

    /// <summary>両スキーマに共通するが列構成に差分があるテーブル一覧。</summary>
    public List<TableDiff> CommonTableDiffs { get; set; } = new();

    /// <summary>差分が1件も無い（完全一致）かどうか。</summary>
    public bool IsIdentical =>
        TablesOnlyInSource.Count == 0 && TablesOnlyInTarget.Count == 0 && CommonTableDiffs.Count == 0;
}

#endregion
