// ファイル名     : BlobMarkerConverter.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\BlobMarkerConverter.cs
// クラス/概要    : BlobMarkerConverter (Class)
// 処理概要/目的  : 結果グリッドのセル表示用コンバータ。BlobMarker 付きの値（BLOB/CLOB）を
//                  "[BLOB N bytes] (ダブルクリックで表示)" という人間が読める要約表示へ変換する。
// 使用方法/適用先: MainWindow.xaml.cs の DataGrid.AutoGeneratingColumn イベントで、
//                  自動生成された DataGridTextColumn の Binding へ組み込んで使用
// 依存関係       : NeoDbStudio.Shared.BlobMarker
// 注意事項       : 通常の文字列値（BLOBマーカーが付いていない）はそのまま透過的に返却する。
// 更新履歴       : 2026/07/29 新規作成（BLOB/CLOBビューア機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Globalization;
using System.Windows.Data;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Helpers;

#region BlobMarkerConverter Class

/// <summary>
/// BLOBマーカー付き文字列を、結果グリッド表示用の人間が読める要約文字列へ変換するコンバータ。
/// </summary>
public class BlobMarkerConverter : IValueConverter
{
    #region IValueConverter Implementation

    /// <summary>
    /// セル値をグリッド表示用の文字列へ変換します。BLOBマーカー付きの場合はバイト数要約を返却します。
    /// </summary>
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value?.ToString();
        if (BlobMarker.IsBlobValue(text))
        {
            byte[]? bytes = BlobMarker.Decode(text);
            int length = bytes?.Length ?? 0;
            return $"[BLOB {length:N0} bytes] (ダブルクリックで表示)";
        }
        return text ?? string.Empty;
    }

    /// <summary>
    /// 本コンバータは表示専用のため、逆変換時は入力値をそのまま返却します（BLOBセルの誤編集時にクラッシュさせないための安全策）。
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }

    #endregion
}

#endregion
