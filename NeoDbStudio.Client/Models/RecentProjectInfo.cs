// ファイル名     : RecentProjectInfo.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\RecentProjectInfo.cs
// クラス/概要    : RecentProjectInfo (Class)
// 処理概要/目的  : ファイルメニューの「最近使ったプロジェクト (Recent Projects)」履歴リストで管理する接続・プロジェクト情報データモデル
// 使用方法/適用先: MainViewModel の RecentProjects コレクション要素として保持し、ファイルメニューサブメニューへ動的バインド
// 依存関係       : System.Text.Json.Serialization
// 注意事項       : プロジェクト名、プロバイダー種別、接続文字列、ファイルパス、最終利用日時を保持します。
// 更新履歴       : 2026/07/28 プロジェクト接続履歴機能の新規作成およびコーディング規約適用
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;

namespace NeoDbStudio.Client.Models;

#region RecentProjectInfo Class

/// <summary>
/// 最近使ったプロジェクト / 接続履歴モデルクラス。
/// </summary>
public class RecentProjectInfo
{
    #region Properties

    /// <summary>プロジェクト表示名。</summary>
    public string ProjectName { get; set; } = "Untitled Project";

    /// <summary>対象 DBMS プロバイダー名。</summary>
    public string ProviderType { get; set; } = "PostgreSQL";

    /// <summary>接続文字列。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>プロジェクトファイルパス（.neodb の絶対パス。未保存の場合空文字）。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>最終利用・接続日時。</summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

    /// <summary>SSHトンネル経由で接続するかどうか。</summary>
    public bool SshEnabled { get; set; } = false;

    /// <summary>SSH 踏み台サーバーのホスト名/IP。</summary>
    public string SshHost { get; set; } = string.Empty;

    /// <summary>SSH ポート番号（既定 22）。</summary>
    public int SshPort { get; set; } = 22;

    /// <summary>SSH ログインユーザー名。</summary>
    public string SshUsername { get; set; } = string.Empty;

    /// <summary>SSH 認証方式（"password" または "key"）。</summary>
    public string SshAuthType { get; set; } = "password";

    /// <summary>SSH パスワード（認証方式が password の場合に使用）。</summary>
    public string SshPassword { get; set; } = string.Empty;

    /// <summary>SSH 秘密鍵ファイルパス（認証方式が key の場合に使用）。</summary>
    public string SshPrivateKeyPath { get; set; } = string.Empty;

    /// <summary>SSH 秘密鍵のパスフレーズ。</summary>
    public string SshPassphrase { get; set; } = string.Empty;

    /// <summary>SSHサーバーから見た転送先 DB ホスト（例: 127.0.0.1）。</summary>
    public string SshRemoteHost { get; set; } = "127.0.0.1";

    /// <summary>SSHサーバーから見た転送先 DB ポート。</summary>
    public int SshRemotePort { get; set; } = 3306;

    /// <summary>メニュー表示用のフォーマット済み文字列。</summary>
    public string DisplayText
    {
        get
        {
            return $"[{ProviderType}] {ProjectName} ({LastAccessedAt:yyyy/MM/dd HH:mm})";
        }
    }

    /// <summary>
    /// 画面表示専用：接続文字列中のパスワードを伏字化した文字列（Pwd=/Password= の値を ******** に置換）。
    /// 実際の接続処理には ConnectionString（平文）を使用し、UI表示にのみ本プロパティを使用する。
    /// </summary>
    public string MaskedConnectionString
    {
        get
        {
            if (string.IsNullOrEmpty(ConnectionString))
            {
                return string.Empty;
            }

            return System.Text.RegularExpressions.Regex.Replace(
                ConnectionString,
                @"(?i)\b(pwd|password)\s*=\s*[^;]*",
                "$1=********");
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// RecentProjectInfo インスタンスの既定コンストラクタです。
    /// </summary>
    public RecentProjectInfo()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] RecentProjectInfo.ctor: 初期化を開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] RecentProjectInfo.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] RecentProjectInfo.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// プロジェクト名、プロバイダー、接続文字列、ファイルパスを指定して RecentProjectInfo インスタンスを初期化します。
    /// </summary>
    public RecentProjectInfo(string name, string provider, string connStr, string filePath = "")
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] RecentProjectInfo.ctor(params): 開始します ({name})");

            ProjectName      = name ?? "Untitled Project";
            ProviderType     = provider ?? "PostgreSQL";
            ConnectionString = connStr ?? string.Empty;
            FilePath         = filePath ?? string.Empty;
            LastAccessedAt   = DateTime.Now;

            System.Diagnostics.Debug.WriteLine("[INFO] RecentProjectInfo.ctor(params): 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] RecentProjectInfo.ctor(params): 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
