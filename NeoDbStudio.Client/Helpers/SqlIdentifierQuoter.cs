// ファイル名     : SqlIdentifierQuoter.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SqlIdentifierQuoter.cs
// クラス/概要    : SqlIdentifierQuoter (Static Class)
// 処理概要/目的  : Script as SELECT/INSERT/UPDATE/DELETE/CREATE で使用するテーブル識別子を、
//                 接続中のDBMS方言に応じた引用符（バッククォート/ダブルクォート/角括弧）で
//                 スキーマ修飾名（db.table）のパートごとに囲む。
// 使用方法/適用先: MainViewModel の ScriptSelect/ScriptInsert/ScriptUpdate/ScriptDelete/ScriptCreate
// 依存関係       : なし
// 注意事項       : 識別子にハイフン等の非英数字を含む実DB名（例: carelink_db_ho-mu）で
//                 未クォートのまま生成すると MySQL 側で構文エラー（減算式と誤解釈）になるため必須。
// 更新履歴       : 2026/07/29 新規作成（実DB接続でのクエリ実行失敗を受けて）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;

namespace NeoDbStudio.Client.Helpers;

#region SqlIdentifierQuoter Class

/// <summary>
/// DBMS方言別のSQL識別子クォート処理を提供する静的ヘルパークラス。
/// </summary>
public static class SqlIdentifierQuoter
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// スキーマ修飾名（"db.table" 形式を含む）を、指定プロバイダーの識別子クォート規則に従って
    /// パートごとに引用符で囲みます。
    /// </summary>
    /// <param name="providerType">[パラメータ] 接続中のDBMSプロバイダー名を指定します。</param>
    /// <param name="qualifiedName">[パラメータ] "table" または "schema.table" 形式の識別子を指定します。</param>
    /// <returns>クォート済みの識別子文字列を返却します。</returns>
    public static string Quote(string providerType, string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return qualifiedName;
        }

        (string open, string close) = GetQuoteChars(providerType);

        string[] parts = qualifiedName.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = $"{open}{parts[i]}{close}";
        }

        return string.Join(".", parts);
    }

    #endregion

    #region Private Methods

    private static (string open, string close) GetQuoteChars(string providerType)
    {
        return providerType?.Trim().ToLowerInvariant() switch
        {
            "mysql" or "mariadb" or "sqlite" => ("`", "`"),
            "postgresql" or "oracle" => ("\"", "\""),
            "mssql" => ("[", "]"),
            _ => (string.Empty, string.Empty) // 未対応プロバイダーは無クォートで安全側に通過させる
        };
    }

    #endregion
}

#endregion
