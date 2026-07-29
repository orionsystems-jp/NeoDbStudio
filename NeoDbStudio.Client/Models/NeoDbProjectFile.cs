// ファイル名     : NeoDbProjectFile.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\NeoDbProjectFile.cs
// クラス/概要    : TableNodeSaveData (Class), NeoDbProjectFile (Class)
// 処理概要/目的  : NeoDB Studio のプロジェクト保存形式（.neodb JSON ファイル）の構造シリアライズ用データモデルの定義
// 使用方法/適用先: JsonSerializer によるプロジェクトファイルの保存・読み込みに使用
// 依存関係       : System.Text.Json.Serialization
// 注意事項       : ER図ノードの座標、背景色、テーブル名およびカラム一覧を JSON に保持します。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;

namespace NeoDbStudio.Client.Models;

#region TableNodeSaveData Class

/// <summary>
/// ER図テーブルノードの保存シリアライズデータモデル。
/// </summary>
public class TableNodeSaveData
{
    #region Properties

    /// <summary>テーブル名。</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>キャンバス上の X 座標。</summary>
    public double X { get; set; }

    /// <summary>キャンバス上の Y 座標。</summary>
    public double Y { get; set; }

    /// <summary>背景色 Hex 文字列。</summary>
    public string FillColorHex { get; set; } = "#87CEFA";

    /// <summary>ビュー由来のノードかどうか。</summary>
    public bool IsView { get; set; } = false;

    /// <summary>カラム名一覧。</summary>
    public List<string> Columns { get; set; } = new();

    #endregion

    #region Constructors

    /// <summary>
    /// TableNodeSaveData インスタンスの既定コンストラクタです。
    /// </summary>
    public TableNodeSaveData()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableNodeSaveData.ctor: 開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] TableNodeSaveData.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableNodeSaveData.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion

#region NeoDbProjectFile Class

/// <summary>
/// .neodb プロジェクトルートファイルデータのシリアライズ用モデル。
/// </summary>
public class NeoDbProjectFile
{
    #region Properties

    /// <summary>プロジェクト名。</summary>
    public string ProjectName { get; set; } = "Untitled Project";

    /// <summary>プロバイダー種別。</summary>
    public string ProviderType { get; set; } = "PostgreSQL";

    /// <summary>接続文字列。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>メイン SQL スクリプト。</summary>
    public string SqlScript { get; set; } = string.Empty;

    /// <summary>最終保存日時。</summary>
    public DateTime LastSavedAt { get; set; } = DateTime.Now;

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

    /// <summary>保存されたテーブルノードデータ一覧。</summary>
    public List<TableNodeSaveData> TableNodes { get; set; } = new();

    #endregion

    #region Constructors

    /// <summary>
    /// NeoDbProjectFile インスタンスの既定コンストラクタです。
    /// </summary>
    public NeoDbProjectFile()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] NeoDbProjectFile.ctor: 開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] NeoDbProjectFile.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] NeoDbProjectFile.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
