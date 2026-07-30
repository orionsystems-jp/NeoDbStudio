// ファイル名     : SchemaExcelExporter.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SchemaExcelExporter.cs
// クラス/概要    : SchemaExcelExporter (Static Class)
// 処理概要/目的  : 直近取得済みスキーマ（SchemaResponse）を「テーブル一覧」「カラム定義」「外部キー関係」の
//                 3シート構成でExcelブック（.xlsx）へ書き出す。CareLink/CareTransportのDB設計書生成
//                 （generate_db_design.py）と同種の構成に合わせている。
// 使用方法/適用先: MainViewModel.ExportSchemaToExcelCommand
// 依存関係       : ClosedXML（MIT License）
// 注意事項       : スキーマ修飾名（"db.table"形式）は Schema/TableName の2列へ分割して出力する。
// 更新履歴       : 2026/07/30 新規作成（ユーザー要望：ER図情報のExcel表現）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Linq;
using ClosedXML.Excel;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Helpers;

#region SchemaExcelExporter Class

/// <summary>
/// SchemaResponse を DB設計書形式（テーブル一覧／カラム定義／外部キー関係）のExcelブックへ出力する静的ヘルパー。
/// </summary>
public static class SchemaExcelExporter
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// SchemaResponse の内容を、テーブル一覧・カラム定義・外部キー関係の3シート構成で
    /// 指定パスへExcelブックとして保存します。
    /// </summary>
    /// <param name="schema">[パラメータ] エクスポート対象のスキーマ情報を指定します。</param>
    /// <param name="filePath">[パラメータ] 保存先の .xlsx ファイルパスを指定します。</param>
    public static void Export(SchemaResponse schema, string filePath)
    {
        using var workbook = new XLWorkbook();

        BuildTablesSheet(workbook, schema);
        BuildColumnsSheet(workbook, schema);
        BuildForeignKeysSheet(workbook, schema);

        workbook.SaveAs(filePath);
    }

    #endregion

    #region Private Methods

    private static (string schema, string table) SplitQualifiedName(string qualifiedName)
    {
        int dot = qualifiedName.IndexOf('.');
        return dot > 0
            ? (qualifiedName.Substring(0, dot), qualifiedName.Substring(dot + 1))
            : ("(既定スキーマ)", qualifiedName);
    }

    private static void BuildTablesSheet(XLWorkbook workbook, SchemaResponse schema)
    {
        var ws = workbook.Worksheets.Add("テーブル一覧");
        ws.Cell(1, 1).Value = "スキーマ";
        ws.Cell(1, 2).Value = "テーブル名";
        ws.Cell(1, 3).Value = "種別";
        ws.Cell(1, 4).Value = "カラム数";
        ws.Cell(1, 5).Value = "主キー";
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;

        int row = 2;
        foreach (var t in schema.Tables.OrderBy(x => x.Name))
        {
            var (schemaName, tableName) = SplitQualifiedName(t.Name);
            string pkCols = string.Join(", ", t.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
            ws.Cell(row, 1).Value = schemaName;
            ws.Cell(row, 2).Value = tableName;
            ws.Cell(row, 3).Value = "TABLE";
            ws.Cell(row, 4).Value = t.Columns.Count;
            ws.Cell(row, 5).Value = pkCols;
            row++;
        }
        foreach (var v in schema.Views.OrderBy(x => x.Name))
        {
            var (schemaName, tableName) = SplitQualifiedName(v.Name);
            ws.Cell(row, 1).Value = schemaName;
            ws.Cell(row, 2).Value = tableName;
            ws.Cell(row, 3).Value = "VIEW";
            ws.Cell(row, 4).Value = v.Columns.Count;
            ws.Cell(row, 5).Value = string.Empty;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void BuildColumnsSheet(XLWorkbook workbook, SchemaResponse schema)
    {
        var ws = workbook.Worksheets.Add("カラム定義");
        ws.Cell(1, 1).Value = "スキーマ";
        ws.Cell(1, 2).Value = "テーブル名";
        ws.Cell(1, 3).Value = "カラム名";
        ws.Cell(1, 4).Value = "データ型";
        ws.Cell(1, 5).Value = "主キー";
        ws.Cell(1, 6).Value = "NULL許可";
        ws.Range(1, 1, 1, 6).Style.Font.Bold = true;

        int row = 2;
        foreach (var t in schema.Tables.Concat(schema.Views).OrderBy(x => x.Name))
        {
            var (schemaName, tableName) = SplitQualifiedName(t.Name);
            foreach (var c in t.Columns)
            {
                ws.Cell(row, 1).Value = schemaName;
                ws.Cell(row, 2).Value = tableName;
                ws.Cell(row, 3).Value = c.Name;
                ws.Cell(row, 4).Value = c.DataType;
                ws.Cell(row, 5).Value = c.IsPrimaryKey ? "PK" : string.Empty;
                ws.Cell(row, 6).Value = c.IsNullable ? "YES" : "NO";
                row++;
            }
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void BuildForeignKeysSheet(XLWorkbook workbook, SchemaResponse schema)
    {
        var ws = workbook.Worksheets.Add("外部キー関係");
        ws.Cell(1, 1).Value = "制約名";
        ws.Cell(1, 2).Value = "親テーブル（参照先）";
        ws.Cell(1, 3).Value = "親カラム";
        ws.Cell(1, 4).Value = "子テーブル（参照元）";
        ws.Cell(1, 5).Value = "子カラム";
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;

        int row = 2;
        foreach (var fk in schema.ForeignKeys.OrderBy(x => x.FkTable).ThenBy(x => x.ConstraintName))
        {
            ws.Cell(row, 1).Value = fk.ConstraintName;
            ws.Cell(row, 2).Value = fk.PkTable;
            ws.Cell(row, 3).Value = fk.PkColumn;
            ws.Cell(row, 4).Value = fk.FkTable;
            ws.Cell(row, 5).Value = fk.FkColumn;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    #endregion
}

#endregion
