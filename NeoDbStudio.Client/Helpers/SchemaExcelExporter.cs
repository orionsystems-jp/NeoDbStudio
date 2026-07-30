// ファイル名     : SchemaExcelExporter.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SchemaExcelExporter.cs
// クラス/概要    : SchemaExcelExporter (Static Class)
// 処理概要/目的  : 選択中スキーマ（データベース）のスキーマ情報を、接続情報・テーブル/ビュー/ストアド一覧・
//                 外部キー関係・テーブル単位詳細シートの構成でExcelブック（.xlsx）へ書き出す。
//                 CareLink/CareTransportのDB設計書生成（generate_db_design.py）と同種の構成に合わせている。
// 使用方法/適用先: MainViewModel.ExportSchemaToExcelCommand
// 依存関係       : ClosedXML（MIT License）
// 注意事項       : エクスポート範囲はER図タブで選択中のスキーマ1つに限定する（複数テナントDBで同名テーブルが
//                 大量に重複するため、テーブル単位シート名の一意性を保証するには単一スキーマへの限定が必須）。
//                 Excelシート名は31文字制限・使用禁止文字（\ / ? * [ ] :）があるため必ずサニタイズする。
// 更新履歴       : 2026/07/30 新規作成（テーブル一覧/カラム定義/外部キー関係の3シート構成）
//                 2026/07/30 ユーザー要望によりテーブル単位シート・接続情報シート・オブジェクト種別ごとの
//                            一覧シート構成へ全面改訂
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Helpers;

#region SchemaExcelExporter Class

/// <summary>
/// 選択中スキーマの情報を DB設計書形式（接続情報／オブジェクト種別一覧／外部キー関係／テーブル単位詳細）の
/// Excelブックへ出力する静的ヘルパー。
/// </summary>
public static class SchemaExcelExporter
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// 指定スキーマ（データベース）に属するテーブル・ビュー・外部キー情報を、接続情報シート・
    /// オブジェクト種別ごとの一覧シート・外部キー関係シート・テーブル単位詳細シートの構成で
    /// 指定パスへExcelブックとして保存します。
    /// </summary>
    /// <param name="fullSchema">[パラメータ] 接続先から取得済みの完全なスキーマ情報を指定します。</param>
    /// <param name="selectedSchemaGroup">[パラメータ] エクスポート対象のスキーマ（データベース）名を指定します。</param>
    /// <param name="projectName">[パラメータ] 接続情報シートへ表示するプロジェクト名を指定します。</param>
    /// <param name="providerType">[パラメータ] 接続情報シートへ表示するDBMSプロバイダー名を指定します。</param>
    /// <param name="connectionString">[パラメータ] 接続情報シートへ表示する接続文字列を指定します（パスワードは自動的に伏字化）。</param>
    /// <param name="filePath">[パラメータ] 保存先の .xlsx ファイルパスを指定します。</param>
    public static void Export(
        SchemaResponse fullSchema,
        string selectedSchemaGroup,
        string projectName,
        string providerType,
        string connectionString,
        string filePath)
    {
        var tables = fullSchema.Tables.Where(t => GetSchemaGroup(t.Name) == selectedSchemaGroup).OrderBy(t => t.Name).ToList();
        var views = fullSchema.Views.Where(v => GetSchemaGroup(v.Name) == selectedSchemaGroup).OrderBy(v => v.Name).ToList();
        var foreignKeys = fullSchema.ForeignKeys
            .Where(fk => tables.Any(t => t.Name == fk.PkTable) && tables.Any(t => t.Name == fk.FkTable))
            .OrderBy(fk => fk.FkTable).ThenBy(fk => fk.ConstraintName)
            .ToList();
        var procedures = fullSchema.Procedures.OrderBy(p => p.Name).ToList(); // RoutineSchemaにスキーマ情報が無いため全件表示

        using var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        BuildConnectionInfoSheet(workbook, usedSheetNames, selectedSchemaGroup, projectName, providerType, connectionString, tables.Count, views.Count, procedures.Count, foreignKeys.Count);
        BuildObjectListSheet(workbook, usedSheetNames, "テーブル一覧", tables);
        if (views.Count > 0)
        {
            BuildObjectListSheet(workbook, usedSheetNames, "ビュー一覧", views);
        }
        if (procedures.Count > 0)
        {
            BuildProceduresSheet(workbook, usedSheetNames, procedures);
        }
        BuildForeignKeysSheet(workbook, usedSheetNames, foreignKeys);

        foreach (var t in tables)
        {
            BuildTableDetailSheet(workbook, usedSheetNames, t);
        }

        workbook.SaveAs(filePath);
    }

    #endregion

    #region Private Methods

    private static string GetSchemaGroup(string qualifiedName)
    {
        int dot = qualifiedName.IndexOf('.');
        return dot > 0 ? qualifiedName.Substring(0, dot) : "(既定スキーマ)";
    }

    private static string GetUnqualifiedName(string qualifiedName)
    {
        int dot = qualifiedName.IndexOf('.');
        return dot > 0 ? qualifiedName.Substring(dot + 1) : qualifiedName;
    }

    private static string MaskConnectionString(string connectionString)
    {
        return Regex.Replace(connectionString, @"(?i)\b(pwd|password)\s*=\s*[^;]*", "$1=********");
    }

    /// <summary>
    /// Excelシート名の制約（31文字以内・使用禁止文字 \ / ? * [ ] : ・空文字禁止・重複禁止）に適合するよう
    /// 名前をサニタイズし、同一ブック内で重複しないよう連番を付与します。
    /// </summary>
    private static string SanitizeSheetName(string rawName, HashSet<string> usedNames)
    {
        string sanitized = Regex.Replace(rawName, @"[\\/\?\*\[\]:]", "_");
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Sheet";
        }
        if (sanitized.Length > 31)
        {
            sanitized = sanitized.Substring(0, 31);
        }

        string candidate = sanitized;
        int suffix = 2;
        while (usedNames.Contains(candidate))
        {
            string suffixText = $"_{suffix}";
            int maxBaseLength = 31 - suffixText.Length;
            string basePart = sanitized.Length > maxBaseLength ? sanitized.Substring(0, maxBaseLength) : sanitized;
            candidate = basePart + suffixText;
            suffix++;
        }

        usedNames.Add(candidate);
        return candidate;
    }

    private static void BuildConnectionInfoSheet(
        XLWorkbook workbook, HashSet<string> usedSheetNames, string selectedSchemaGroup,
        string projectName, string providerType, string connectionString,
        int tableCount, int viewCount, int procedureCount, int fkCount)
    {
        var ws = workbook.Worksheets.Add(SanitizeSheetName("接続情報", usedSheetNames));

        void Row(int r, string label, string value)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = value;
        }

        Row(1, "プロジェクト名", projectName);
        Row(2, "DBMSプロバイダー", providerType);
        Row(3, "スキーマ / データベース名", selectedSchemaGroup);
        Row(4, "接続文字列（パスワード伏字）", MaskConnectionString(connectionString));
        Row(5, "エクスポート日時", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Row(6, "テーブル数", tableCount.ToString());
        Row(7, "ビュー数", viewCount.ToString());
        Row(8, "ストアド/関数数", procedureCount.ToString());
        Row(9, "外部キー数", fkCount.ToString());

        ws.Columns().AdjustToContents();
    }

    private static void BuildObjectListSheet(XLWorkbook workbook, HashSet<string> usedSheetNames, string sheetTitle, List<TableSchema> objects)
    {
        var ws = workbook.Worksheets.Add(SanitizeSheetName(sheetTitle, usedSheetNames));
        ws.Cell(1, 1).Value = "名前";
        ws.Cell(1, 2).Value = "カラム数";
        ws.Cell(1, 3).Value = "主キー";
        ws.Range(1, 1, 1, 3).Style.Font.Bold = true;

        int row = 2;
        foreach (var t in objects)
        {
            string name = GetUnqualifiedName(t.Name);
            string pkCols = string.Join(", ", t.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
            ws.Cell(row, 1).Value = name;
            ws.Cell(row, 2).Value = t.Columns.Count;
            ws.Cell(row, 3).Value = pkCols;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void BuildProceduresSheet(XLWorkbook workbook, HashSet<string> usedSheetNames, List<RoutineSchema> procedures)
    {
        var ws = workbook.Worksheets.Add(SanitizeSheetName("ストアド・関数一覧", usedSheetNames));
        ws.Cell(1, 1).Value = "名前";
        ws.Cell(1, 2).Value = "種別";
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;

        int row = 2;
        foreach (var p in procedures)
        {
            ws.Cell(row, 1).Value = p.Name;
            ws.Cell(row, 2).Value = p.RoutineType;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void BuildForeignKeysSheet(XLWorkbook workbook, HashSet<string> usedSheetNames, List<ForeignKeySchema> foreignKeys)
    {
        var ws = workbook.Worksheets.Add(SanitizeSheetName("外部キー関係", usedSheetNames));
        ws.Cell(1, 1).Value = "制約名";
        ws.Cell(1, 2).Value = "親テーブル（参照先）";
        ws.Cell(1, 3).Value = "親カラム";
        ws.Cell(1, 4).Value = "子テーブル（参照元）";
        ws.Cell(1, 5).Value = "子カラム";
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;

        int row = 2;
        foreach (var fk in foreignKeys)
        {
            ws.Cell(row, 1).Value = fk.ConstraintName;
            ws.Cell(row, 2).Value = GetUnqualifiedName(fk.PkTable);
            ws.Cell(row, 3).Value = fk.PkColumn;
            ws.Cell(row, 4).Value = GetUnqualifiedName(fk.FkTable);
            ws.Cell(row, 5).Value = fk.FkColumn;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    /// <summary>
    /// テーブル1件分の詳細（カラム定義＋インデックス）を、テーブル名を付けた専用シートへ出力します。
    /// </summary>
    private static void BuildTableDetailSheet(XLWorkbook workbook, HashSet<string> usedSheetNames, TableSchema table)
    {
        string sheetName = SanitizeSheetName(GetUnqualifiedName(table.Name), usedSheetNames);
        var ws = workbook.Worksheets.Add(sheetName);

        ws.Cell(1, 1).Value = $"テーブル名: {GetUnqualifiedName(table.Name)}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;

        ws.Cell(3, 1).Value = "カラム名";
        ws.Cell(3, 2).Value = "データ型";
        ws.Cell(3, 3).Value = "主キー";
        ws.Cell(3, 4).Value = "NULL許可";
        ws.Range(3, 1, 3, 4).Style.Font.Bold = true;

        int row = 4;
        foreach (var c in table.Columns)
        {
            ws.Cell(row, 1).Value = c.Name;
            ws.Cell(row, 2).Value = c.DataType;
            ws.Cell(row, 3).Value = c.IsPrimaryKey ? "PK" : string.Empty;
            ws.Cell(row, 4).Value = c.IsNullable ? "YES" : "NO";
            row++;
        }

        if (table.Indexes.Count > 0)
        {
            row += 1;
            ws.Cell(row, 1).Value = "インデックス";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;

            ws.Cell(row, 1).Value = "インデックス名";
            ws.Cell(row, 2).Value = "一意性";
            ws.Cell(row, 3).Value = "対象カラム";
            ws.Range(row, 1, row, 3).Style.Font.Bold = true;
            row++;

            foreach (var idx in table.Indexes)
            {
                ws.Cell(row, 1).Value = idx.Name;
                ws.Cell(row, 2).Value = idx.IsUnique ? "UNIQUE" : "NON-UNIQUE";
                ws.Cell(row, 3).Value = string.Join(", ", idx.Columns);
                row++;
            }
        }

        ws.Columns().AdjustToContents();
    }

    #endregion
}

#endregion
