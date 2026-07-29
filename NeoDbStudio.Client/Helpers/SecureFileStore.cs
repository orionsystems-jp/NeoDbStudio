// ファイル名     : SecureFileStore.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SecureFileStore.cs
// クラス/概要    : SecureFileStore (Static Class)
// 処理概要/目的  : 接続履歴(.json)およびプロジェクトファイル(.neodb)を、Windows DPAPI (CurrentUser スコープ) で
//                  ファイル全体を暗号化した状態でディスクへ保存・読込する。
// 使用方法/適用先: MainViewModel の履歴保存/読込・プロジェクトファイル保存/読込処理から利用
// 依存関係       : System.Security.Cryptography.ProtectedData
// 注意事項       : 暗号化データは現在ログオン中の Windows ユーザーアカウントに紐付く（別ユーザー/別マシンでは復号不可）。
//                  旧バージョンで保存された平文JSONファイルは ReadFileContent 時にそのまま通過させ、
//                  次回 WriteEncryptedFile 保存時に自動的に暗号化コンテナ形式へ移行する（後方互換）。
//                  リポジトリに同梱するサンプルプロジェクト・履歴テンプレート（Projects\*.neodb 等）は、
//                  他の開発者・他マシンでも利用できるよう意図的に本暗号化の対象外（平文のまま）としている。
// 更新履歴       : 2026/07/29 新規作成（プロジェクトファイル・履歴ファイルのファイル全体暗号化対応）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NeoDbStudio.Client.Helpers;

#region SecureFileStore Class

/// <summary>
/// DPAPI (CurrentUser スコープ) によるファイル全体の暗号化保存・復号読込ヘルパー。
/// </summary>
public static class SecureFileStore
{
    #region Fields

    /// <summary>暗号化済みファイルであることを示す識別プレフィックス（先頭行）。</summary>
    private const string Magic = "NEODBFILEENC1:";

    /// <summary>DPAPI のエントロピー（ファイル全体暗号化専用の追加ソルト。フィールド単位暗号化とは別値）。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NeoDbStudio.SecureFileStore.v1");

    #endregion

    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// 平文テキスト（JSON 文字列全体）を現在の Windows ユーザーアカウントに紐付けて DPAPI 暗号化し、
    /// 暗号化コンテナ形式としてファイルへ書き込みます。
    /// </summary>
    /// <param name="filePath">[パラメータ] 書き込み先ファイルパスを指定します。</param>
    /// <param name="plainText">[パラメータ] 暗号化対象の平文テキストを指定します。</param>
    public static void WriteEncryptedFile(string filePath, string plainText)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath)) // NULL・空文字ガード
            {
                throw new ArgumentException("書き込み先ファイルパスが指定されていません。", nameof(filePath));
            }

            byte[] plainBytes     = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            string container      = Magic + Convert.ToBase64String(encryptedBytes);

            File.WriteAllText(filePath, container, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] SecureFileStore.WriteEncryptedFile: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// ファイルを読み込み、暗号化コンテナ形式（先頭が Magic プレフィックス）であれば DPAPI 復号して
    /// 平文テキストを返却します。旧バージョンの平文JSON（プレフィックス無し）はそのまま返却します（後方互換）。
    /// </summary>
    /// <param name="filePath">[パラメータ] 読込対象ファイルパスを指定します。</param>
    /// <returns>復号済み、または元々平文であった JSON 文字列を返却します。</returns>
    public static string ReadFileContent(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) // 未指定・未存在ガード
            {
                throw new FileNotFoundException("読込対象のファイルが見つかりません。", filePath);
            }

            string rawContent = File.ReadAllText(filePath);

            if (!rawContent.StartsWith(Magic, StringComparison.Ordinal))
            {
                return rawContent; // 旧バージョンの平文JSON・同梱サンプルテンプレート：そのまま返却する
            }

            byte[] encryptedBytes = Convert.FromBase64String(rawContent.Substring(Magic.Length));
            byte[] plainBytes     = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            // 別ユーザー/別マシンで暗号化されたファイル、または破損データ：復号不能のため明確な例外として呼び出し元へ伝える
            System.Diagnostics.Debug.WriteLine($"[ERROR] SecureFileStore.ReadFileContent: 復号に失敗しました - {ex.Message}");
            throw new InvalidOperationException(
                $"このファイルは別のWindowsユーザーアカウントまたは別のPCで暗号化されたため復号できません: {filePath}", ex);
        }
    }

    #endregion
}

#endregion
