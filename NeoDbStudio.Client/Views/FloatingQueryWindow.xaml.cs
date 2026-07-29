// ファイル名     : FloatingQueryWindow.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\FloatingQueryWindow.xaml.cs
// クラス/概要    : FloatingQueryWindow (Partial Class)
// 処理概要/目的  : クエリタブをメインウィンドウのドッキング領域から分離（Undock / Float）して独立したフローティングサブウィンドウとして表示・操作するコントロール
// 使用方法/適用先: クエリタブヘッダーの「分離」ボタン押下時にインスタンス化され、QueryTabViewModel を DataContext としてバインド
// 依存関係       : QueryTabViewModel, CustomSqlHighlighting
// 注意事項       : ウィンドウが閉じられた際も DataContext の状態は維持され、双方向同期が保たれます。
// 更新履歴       : 2026/07/29 新規作成・クエリタブ分離フローティングウィンドウ機能搭載
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Runtime.Versioning;
using System.Windows;
using ICSharpCode.AvalonEdit;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Client.ViewModels;

namespace NeoDbStudio.Client.Views;

#region FloatingQueryWindow Class

/// <summary>
/// クエリタブ分離フローティングウィンドウコードビハインドクラス。
/// </summary>
[SupportedOSPlatform("windows7.0")]
public partial class FloatingQueryWindow : Window
{
    #region Constructors

    /// <summary>
    /// 処理内容     : FloatingQueryWindow クラスの新しいインスタンスを初期化します。
    /// 処理ロジック : 指定された QueryTabViewModel を DataContext へバインドし、エディターのセットアップを行います。
    /// </summary>
    /// <param name="tabVm">分離表示する対象の QueryTabViewModel インスタンス</param>
    public FloatingQueryWindow(QueryTabViewModel tabVm)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] FloatingQueryWindow.ctor: 分離ウィンドウの初期化を開始します");
            InitializeComponent();

            DataContext = tabVm ?? throw new ArgumentNullException(nameof(tabVm));
            Loaded += FloatingQueryWindow_Loaded;

            System.Diagnostics.Debug.WriteLine("[INFO] FloatingQueryWindow.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] FloatingQueryWindow.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 処理内容     : ウィンドウ Loaded イベントを処理します。
    /// 処理ロジック : AvalonEdit エディター構文ハイライトの設定およびテキスト同期を行います。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void FloatingQueryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] FloatingQueryWindow.FloatingQueryWindow_Loaded: エディター同期を開始します");

            if (DataContext is QueryTabViewModel vm)
            {
                FloatingSqlEditor.SyntaxHighlighting = CustomSqlHighlighting.GetDarkThemeSqlHighlighting();
                FloatingSqlEditor.Text               = vm.SqlScript ?? string.Empty;

                FloatingSqlEditor.TextChanged += (s, args) =>
                {
                    try
                    {
                        vm.SqlScript = FloatingSqlEditor.Text;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WARNING] FloatingQueryWindow.TextChanged: {ex.Message}");
                    }
                };
            }

            System.Diagnostics.Debug.WriteLine("[INFO] FloatingQueryWindow.FloatingQueryWindow_Loaded: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] FloatingQueryWindow.FloatingQueryWindow_Loaded: 例外発生 - {ex.Message}");
        }
    }

    #endregion
}

#endregion
