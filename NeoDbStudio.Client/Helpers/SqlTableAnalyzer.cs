// ファイル名     : SqlTableAnalyzer.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SqlTableAnalyzer.cs
// クラス/概要    : SqlTableAnalyzer (Static Class)
// 処理概要/目的  : SELECT文から「単一テーブルへのインライン編集が安全に行えるか」を判定し、対象テーブル名を抽出する
// 使用方法/適用先: 結果グリッドのインライン編集（RowEditEnding）で、編集内容をUPDATE文として反映可能かの事前判定に使用
// 依存関係       : System.Text.RegularExpressions
// 注意事項       : JOIN・カンマ区切り複数テーブル・サブクエリ等を検出した場合は安全側で対象外（null）とする。
//                 誤ってUPDATE対象を誤認するリスクを避けるため、判定は意図的に保守的にしている。
// 更新履歴       : 2026/07/29 新規作成（結果グリッドのインライン編集機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System.Text.RegularExpressions;

namespace NeoDbStudio.Client.Helpers;

#region SqlTableAnalyzer Class

/// <summary>
/// SQL文の解析により、インライン編集（UPDATE自動生成）の対象となる単一テーブル名を判定する静的ヘルパー。
/// </summary>
public static class SqlTableAnalyzer
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// SQL文が単一テーブルへの単純な SELECT 文であるかを判定し、対象テーブル名を抽出します。
    /// JOIN・複数テーブル・非SELECT・複数ステートメントを含む場合は、誤ったテーブルへUPDATEを
    /// 発行するリスクを避けるため安全側に倒して null を返却します。
    /// </summary>
    /// <param name="sql">[パラメータ] 判定対象の SQL 文を指定します。</param>
    /// <returns>単一テーブル名（インライン編集可能）、または対象外の場合は null を返却します。</returns>
    public static string? ExtractSingleSourceTableName(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        string trimmed = sql.Trim();
        if (trimmed.EndsWith(";", System.StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
        }

        if (trimmed.Contains(';')) // 複数ステートメントの可能性は安全側で対象外
        {
            return null;
        }

        if (!Regex.IsMatch(trimmed, @"^\s*SELECT\b", RegexOptions.IgnoreCase)) // SELECT文以外は対象外
        {
            return null;
        }

        if (Regex.IsMatch(trimmed, @"\b(JOIN|UNION)\b", RegexOptions.IgnoreCase)) // 結合・集合演算を含む場合は対象外
        {
            return null;
        }

        var match = Regex.Match(trimmed, @"\bFROM\s+([A-Za-z0-9_\.""`\[\]]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string tableToken = match.Groups[1].Value;

        // FROM句の直後がカンマ（複数テーブル指定）の場合は対象外
        int afterMatchIndex = match.Index + match.Length;
        string remainder = trimmed.Substring(afterMatchIndex).TrimStart();
        if (remainder.StartsWith(",", System.StringComparison.Ordinal))
        {
            return null;
        }

        // クォート・角括弧・バッククォートを除去してテーブル名のみへ正規化
        string cleaned = tableToken.Trim('"', '`', '[', ']');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    #endregion
}

#endregion
