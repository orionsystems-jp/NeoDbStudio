// ファイル名     : LicenseViewerDialog.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\LicenseViewerDialog.xaml.cs
// クラス/概要    : LicenseViewerDialog (Partial Class)
// 処理概要/目的  : サードパーティおよびオープンソースソフトウェア（AvalonEdit, MSAGL, gRPC, ModernWpfUI 等）のライセンス情報を表示するダイアログウィンドウコードビハインド
// 使用方法/適用先: メインウィンドウの [ヘルプ(H)] -> [サードパーティライセンス情報] メニュー選択時に ShowDialog 経由で表示
// 依存関係       : System.Windows.Window
// 注意事項       : ダイアログの閉じるボタン押下時に DialogResult を設定してクローズします。
// 更新履歴       : 2026/07/28 ライセンス表示ダイアログの新規作成およびコーディング規約全適用
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows;

namespace NeoDbStudio.Client.Views;

#region LicenseViewerDialog Class

/// <summary>
/// オープンソースライセンス情報表示ダイアログクラス。
/// </summary>
public partial class LicenseViewerDialog : Window
{
    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// LicenseViewerDialog インスタンスの初期化を行います。
    /// </summary>
    public LicenseViewerDialog()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] LicenseViewerDialog.ctor: 初期化を開始します");
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("[INFO] LicenseViewerDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] LicenseViewerDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 閉じるボタンのクリックイベントハンドラー。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] LicenseViewerDialog.CloseButton_Click: 閉じるボタンが押されました");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] LicenseViewerDialog.CloseButton_Click: 例外発生 - {ex.Message}");
        }
    }

    #endregion
}

#endregion
