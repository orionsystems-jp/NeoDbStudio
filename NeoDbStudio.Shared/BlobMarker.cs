// ファイル名     : BlobMarker.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Shared\BlobMarker.cs
// クラス/概要    : BlobMarker (Static Class)
// 処理概要/目的  : QueryResponse.Row.Values（文字列配列）上でBLOB/CLOB（バイナリ）値を安全に表現するための
//                  共通マーカープレフィックス・エンコード/デコードヘルパー。API・Client の双方から参照する。
// 使用方法/適用先: NeoDbStudio.Api.Services.DbService（送信側エンコード）、
//                  NeoDbStudio.Client（受信側デコード・BLOBビューア）
// 依存関係       : System.Convert (Base64)
// 注意事項       : マーカー文字列は通常のテキスト値と衝突しないよう、印字不能に近い接頭辞を用いる。
// 更新履歴       : 2026/07/29 新規作成（BLOB/CLOBビューア機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;

namespace NeoDbStudio.Shared;

#region BlobMarker Class

/// <summary>
/// BLOB/CLOB（バイナリ）値を文字列配列上で安全に往復させるための共通マーカー・エンコードヘルパー。
/// </summary>
public static class BlobMarker
{
    #region Fields

    /// <summary>BLOBエンコード済み値であることを示す識別プレフィックス。</summary>
    public const string Prefix = "\u0001NEODB_BLOB_B64:";

    #endregion

    #region Public Methods

    /// <summary>
    /// バイト配列をマーカー付きBase64文字列へエンコードします。
    /// </summary>
    /// <param name="bytes">[パラメータ] エンコード対象のバイト配列を指定します。</param>
    /// <returns>マーカー付きBase64文字列を返却します。</returns>
    public static string Encode(byte[] bytes)
    {
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 指定文字列がBLOBマーカー付きの値かどうかを判定します。
    /// </summary>
    /// <param name="value">[パラメータ] 判定対象の文字列を指定します。</param>
    /// <returns>BLOBマーカー付きの場合に true を返却します。</returns>
    public static bool IsBlobValue(string? value)
    {
        return value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// マーカー付きBase64文字列から元のバイト配列を復元します。マーカーが無い場合は null を返却します。
    /// </summary>
    /// <param name="value">[パラメータ] デコード対象の文字列を指定します。</param>
    /// <returns>復元されたバイト配列、またはマーカー非該当時は null を返却します。</returns>
    public static byte[]? Decode(string? value)
    {
        if (!IsBlobValue(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value!.Substring(Prefix.Length));
        }
        catch (FormatException)
        {
            return null; // 破損データ：復元不能
        }
    }

    #endregion
}

#endregion
