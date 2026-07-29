// ファイル名     : SchemaComparer.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SchemaComparer.cs
// クラス/概要    : SchemaComparer (Static Class)
// 処理概要/目的  : 2つの SchemaResponse（ソース・ターゲット）を比較し、テーブル・カラムの差分を検出する。
//                  また検出した差分から、ターゲットをソースへ同期するための DDL スクリプトを生成する。
// 使用方法/適用先: SchemaDiffDialog から、2接続間のスキーマ比較・同期スクリプト生成に使用
// 依存関係       : NeoDbStudio.Shared.SchemaResponse, NeoDbStudio.Client.Models.SchemaDiffResult,
//                  NeoDbStudio.Client.ViewModels.TableDesignerViewModel（DDL方言生成ロジックの再利用）
// 注意事項       : 破壊的な DROP 文は誤実行防止のため常にコメントアウトした状態で出力する（自動実行は行わない）。
// 更新履歴       : 2026/07/29 新規作成（スキーマ比較機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Client.ViewModels;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Helpers;

#region SchemaComparer Class

/// <summary>
/// 2つの DBMS スキーマを比較し、差分検出および同期スクリプト生成を行う静的ヘルパー。
/// </summary>
public static class SchemaComparer
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// ソース・ターゲット両スキーマのテーブル・カラム構成を比較し、差分結果を返却します。
    ///
    /// [2. 処理フロー]
    /// 1. テーブル名の集合差分から、ソースのみ・ターゲットのみに存在するテーブルを検出します。
    /// 2. 両方に共通するテーブルについて、カラム名の集合差分（追加・削除）とデータ型差分（変更）を検出します。
    /// </summary>
    /// <param name="source">[パラメータ] 比較元（基準）となるスキーマを指定します。</param>
    /// <param name="target">[パラメータ] 比較先（同期対象）となるスキーマを指定します。</param>
    /// <returns>検出された差分結果を返却します。</returns>
    public static SchemaDiffResult Compare(SchemaResponse source, SchemaResponse target)
    {
        var result = new SchemaDiffResult();

        var sourceByName = source.Tables.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var targetByName = target.Tables.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        result.TablesOnlyInSource = source.Tables.Where(t => !targetByName.ContainsKey(t.Name)).Select(t => t.Name).ToList();
        result.TablesOnlyInTarget = target.Tables.Where(t => !sourceByName.ContainsKey(t.Name)).Select(t => t.Name).ToList();

        foreach (var sTable in source.Tables)
        {
            if (!targetByName.TryGetValue(sTable.Name, out var tTable)) // ソースのみのテーブルは上記で既に記録済み
            {
                continue;
            }

            var tableDiff = new TableDiff { TableName = sTable.Name };
            var sCols = sTable.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            var tCols = tTable.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

            foreach (var sCol in sTable.Columns)
            {
                if (!tCols.TryGetValue(sCol.Name, out var tCol))
                {
                    tableDiff.ColumnDiffs.Add(new ColumnDiff { ColumnName = sCol.Name, ChangeType = "Added", SourceType = sCol.DataType });
                }
                else if (!string.Equals(sCol.DataType, tCol.DataType, StringComparison.OrdinalIgnoreCase))
                {
                    tableDiff.ColumnDiffs.Add(new ColumnDiff { ColumnName = sCol.Name, ChangeType = "Modified", SourceType = sCol.DataType, TargetType = tCol.DataType });
                }
            }

            foreach (var tCol in tTable.Columns)
            {
                if (!sCols.ContainsKey(tCol.Name))
                {
                    tableDiff.ColumnDiffs.Add(new ColumnDiff { ColumnName = tCol.Name, ChangeType = "Removed", TargetType = tCol.DataType });
                }
            }

            if (tableDiff.HasDifferences)
            {
                result.CommonTableDiffs.Add(tableDiff);
            }
        }

        return result;
    }

    /// <summary>
    /// [1. 処理概要]
    /// 検出済みの差分から、ターゲットをソースへ同期するための DDL スクリプトを生成します。
    /// テーブル削除・カラム削除など破壊的な操作は、誤実行を防ぐため常にコメントアウトした状態で出力します。
    /// </summary>
    /// <param name="diff">[パラメータ] SchemaComparer.Compare で検出した差分結果を指定します。</param>
    /// <param name="providerType">[パラメータ] ターゲット側の DBMS プロバイダー種別を指定します。</param>
    /// <returns>生成された同期スクリプト文字列を返却します。</returns>
    public static string GenerateSyncScript(SchemaDiffResult diff, string providerType)
    {
        string dialect = (providerType ?? string.Empty).ToLowerInvariant();
        var sb = new StringBuilder();
        sb.AppendLine($"-- Schema Sync Script (Target を Source へ同期), Provider={providerType}");
        sb.AppendLine("-- 実行前に内容をご確認ください（本ツールは自動実行しません）。破壊的な文は安全のためコメントアウトしています。");
        sb.AppendLine();

        if (diff.TablesOnlyInSource.Count > 0)
        {
            sb.AppendLine("-- ソースのみに存在するテーブル（ターゲットへの新規作成候補・列定義はソース側で確認してください）");
            foreach (var t in diff.TablesOnlyInSource)
            {
                sb.AppendLine($"-- CREATE TABLE {t} ( ... );  -- ソース側の実カラム定義を確認のうえ作成してください");
            }
            sb.AppendLine();
        }

        if (diff.TablesOnlyInTarget.Count > 0)
        {
            sb.AppendLine("-- ターゲットのみに存在するテーブル（削除候補・内容を確認のうえコメントを外してください）");
            foreach (var t in diff.TablesOnlyInTarget)
            {
                sb.AppendLine($"-- DROP TABLE {t};");
            }
            sb.AppendLine();
        }

        foreach (var tableDiff in diff.CommonTableDiffs)
        {
            sb.AppendLine($"-- {tableDiff.TableName}");
            foreach (var col in tableDiff.ColumnDiffs)
            {
                if (col.ChangeType == "Added")
                {
                    var tempCol = new TableDesignColumn(col.ColumnName, col.SourceType);
                    sb.AppendLine(TableDesignerViewModel.BuildAddColumnStatement(dialect, tableDiff.TableName, tempCol));
                }
                else if (col.ChangeType == "Modified")
                {
                    var tempCol = new TableDesignColumn(col.ColumnName, col.SourceType);
                    string? stmt = TableDesignerViewModel.BuildModifyColumnStatement(dialect, tableDiff.TableName, tempCol);
                    sb.AppendLine(stmt ?? $"-- [警告] {providerType} はカラム型変更を安全に自動生成できません（{col.ColumnName}）。手動で対応してください。");
                }
                else if (col.ChangeType == "Removed")
                {
                    sb.AppendLine($"-- ALTER TABLE {tableDiff.TableName} DROP COLUMN {col.ColumnName};  -- 確認のうえコメントを外してください");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion
}

#endregion
