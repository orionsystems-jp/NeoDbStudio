// ファイル名     : SqlSnippet.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Models\SqlSnippet.cs
// クラス/概要    : SqlSnippet (Class)
// 処理概要/目的  : ユーザーが登録したSQLスニペット（名前付き再利用可能なSQL文）1件分のデータモデル。
// 使用方法/適用先: MainViewModel.SqlSnippets（%AppData%配下へ暗号化永続化・ListBoxでバインド表示）
// 依存関係       : System.Text.Json.Serialization
// 注意事項       : 全プロバイダー共通の単純な名前付きSQL文であり、DBMS方言別の分類は行わない（MVP範囲）。
// 更新履歴       : 2026/07/30 新規作成（SQLスニペットライブラリ機能）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;

namespace NeoDbStudio.Client.Models;

#region SqlSnippet Class

/// <summary>
/// 名前付きSQLスニペット1件を表すモデル。
/// </summary>
public class SqlSnippet
{
    #region Properties

    /// <summary>スニペット名（一覧表示・検索に使用）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>スニペット本体のSQL文。</summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>登録日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    #endregion
}

#endregion
