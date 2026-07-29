// ファイル名     : CredentialProtector.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\CredentialProtector.cs
// クラス/概要    : CredentialProtector (Static Class)
// 処理概要/目的  : Windows DPAPI (CurrentUser スコープ) を用いて、接続文字列や SSH パスワード等の機密情報をディスク保存前に暗号化・復号する
// 使用方法/適用先: MainViewModel の接続履歴(.json)・プロジェクトファイル(.neodb)の保存/読込処理から利用
// 依存関係       : System.Security.Cryptography.ProtectedData
// 注意事項       : 暗号化データは現在ログオン中の Windows ユーザーアカウントに紐付く（別ユーザー/別マシンでは復号不可）。
//                 旧バージョンで保存された平文値は Unprotect 時にそのまま通過させ、次回保存時に自動的に暗号化へ移行する（後方互換）。
// 更新履歴       : 2026/07/29 新規作成（認証情報の平文保存対策）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace NeoDbStudio.Client.Helpers;

#region CredentialProtector Class

/// <summary>
/// DPAPI (CurrentUser スコープ) による機密文字列の暗号化・復号ヘルパー。
/// </summary>
public static class CredentialProtector
{
    #region Fields

    /// <summary>暗号化済み値であることを示す識別プレフィックス。</summary>
    private const string Prefix = "ENC1:";

    /// <summary>DPAPI のエントロピー（アプリケーション固有の追加ソルト）。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NeoDbStudio.CredentialProtector.v1");

    #endregion

    #region Public Methods

    /// <summary>
    /// 平文文字列を現在の Windows ユーザーアカウントに紐付けて DPAPI 暗号化し、Base64 文字列として返却します。
    /// </summary>
    public static string Protect(string? plainText)
    {
        try
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return plainText ?? string.Empty;
            }

            byte[] plainBytes     = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] CredentialProtector.Protect: 例外発生 - {ex.Message}");
            return plainText ?? string.Empty; // 暗号化失敗時は平文のまま返却（保存自体は継続させる）
        }
    }

    /// <summary>
    /// Protect で暗号化された文字列を復号します。プレフィックスの無い旧形式（平文）はそのまま返却します（後方互換）。
    /// </summary>
    public static string Unprotect(string? storedValue)
    {
        try
        {
            if (string.IsNullOrEmpty(storedValue))
            {
                return storedValue ?? string.Empty;
            }

            if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return storedValue; // 旧バージョンの平文値：そのまま利用し、次回保存時に自動で暗号化へ移行する
            }

            byte[] encryptedBytes = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
            byte[] plainBytes     = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            // 別ユーザー/別マシンで暗号化された値、または破損データ：復号不能のため空を返す（例外で処理全体を止めない）
            System.Diagnostics.Debug.WriteLine($"[WARNING] CredentialProtector.Unprotect: 復号に失敗しました - {ex.Message}");
            return string.Empty;
        }
    }

    #endregion
}

#endregion
