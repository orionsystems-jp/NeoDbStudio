// ファイル名     : BlobViewerDialog.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\BlobViewerDialog.xaml.cs
// クラス/概要    : BlobViewerDialog (Class)
// 処理概要/目的  : 結果グリッドでBLOB/CLOBセルをダブルクリックした際に開く、バイナリデータの
//                  テキスト表示・16進ダンプ表示・画像プレビュー（PNG/JPEG/GIF/BMP検出時）を提供するダイアログ
// 使用方法/適用先: MainWindow.xaml.cs の結果グリッドセルダブルクリックハンドラから非モーダル表示
// 依存関係       : System.Windows.Media.Imaging（画像プレビュー）
// 注意事項       : 表示専用（編集不可）。画像判定はファイル先頭マジックバイトによる簡易判定。
// 更新履歴       : 2026/07/29 新規作成（BLOB/CLOBビューア機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NeoDbStudio.Client.Views;

#region BlobViewerDialog Class

/// <summary>
/// BLOB/CLOB バイナリデータ閲覧ダイアログの Code-Behind。
/// </summary>
public partial class BlobViewerDialog : Window
{
    #region Fields

    private readonly byte[] _bytes;

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// 表示対象のバイト配列を指定して初期化し、テキスト・16進・（該当時）画像プレビューを構築します。
    /// </summary>
    /// <param name="bytes">[パラメータ] 表示対象のバイナリデータを指定します。</param>
    public BlobViewerDialog(byte[] bytes)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] BlobViewerDialog.ctor: 開始します");
            InitializeComponent();

            _bytes = bytes ?? Array.Empty<byte>();
            TxtHeader.Text = $"🧬 BLOB / CLOB Viewer — {_bytes.Length:N0} bytes";

            TxtTextView.Text = TryDecodeAsText(_bytes);
            TxtHexView.Text  = BuildHexDump(_bytes);

            if (TryDetectImageFormat(_bytes) != null)
            {
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(_bytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                ImgPreview.Source = bitmap;
                TabImagePreview.Visibility = Visibility.Visible;
            }

            System.Diagnostics.Debug.WriteLine("[INFO] BlobViewerDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] BlobViewerDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// バイト列をUTF-8としてデコードを試み、制御文字が多い（テキストらしくない）場合はその旨を表示します。
    /// </summary>
    private static string TryDecodeAsText(byte[] bytes)
    {
        try
        {
            string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(bytes);

            int printable = 0;
            foreach (char c in text)
            {
                if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                {
                    printable++;
                }
            }
            double printableRatio = text.Length == 0 ? 1.0 : (double)printable / text.Length;

            return printableRatio < 0.85
                ? "(バイナリデータのためテキスト表示には適していません。Hex タブをご確認ください)"
                : text;
        }
        catch (Exception ex)
        {
            return $"(テキストとしてデコードできませんでした: {ex.Message})";
        }
    }

    /// <summary>
    /// バイト列を16進ダンプ形式（オフセット・16進・ASCII対訳）へ整形します。
    /// </summary>
    private static string BuildHexDump(byte[] bytes)
    {
        const int bytesPerLine = 16;
        var sb = new StringBuilder();

        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            sb.Append(offset.ToString("X8")).Append("  ");

            int lineLength = Math.Min(bytesPerLine, bytes.Length - offset);
            for (int i = 0; i < bytesPerLine; i++)
            {
                sb.Append(i < lineLength ? bytes[offset + i].ToString("X2") : "  ").Append(' ');
            }

            sb.Append(" ");
            for (int i = 0; i < lineLength; i++)
            {
                byte b = bytes[offset + i];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        return sb.Length == 0 ? "(データがありません)" : sb.ToString();
    }

    /// <summary>
    /// 先頭マジックバイトから画像形式を簡易判定します（PNG/JPEG/GIF/BMP）。非該当時は null を返却します。
    /// </summary>
    private static string? TryDetectImageFormat(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "PNG";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPEG";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "GIF";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return "BMP";
        return null;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 処理内容     : 「Save As...」ボタンの Click イベントを処理します。
    /// 処理ロジック : 表示中のバイナリデータをそのままファイルへ書き出します。
    /// </summary>
    private void BtnSaveAsFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog { Filter = "All Files (*.*)|*.*", FileName = "blob_data.bin" };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllBytes(dlg.FileName, _bytes);
                MessageBox.Show($"{_bytes.Length:N0} バイトを保存しました。\n{dlg.FileName}", "Save As", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存に失敗しました: {ex.Message}", "Save As", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] BlobViewerDialog.BtnSaveAsFile_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 「Close」ボタンの Click イベントを処理します。
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}

#endregion
