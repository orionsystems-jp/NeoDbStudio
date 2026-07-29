// ファイル名     : TableDesignerDialog.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\TableDesignerDialog.xaml.cs
// クラス/概要    : TableDesignerDialog (Class)
// 処理概要/目的  : テーブル構造デザイナーのコードビハインド。実スキーマ読込済みカラム一覧の表示・編集、
//                  ALTER TABLE スクリプト生成（クエリタブへの出力）を提供
// 使用方法/適用先: MainViewModel.OpenTableDesigner から非モーダルダイアログとして表示
// 依存関係       : NeoDbStudio.Client.ViewModels.TableDesignerViewModel
// 注意事項       : 生成した DDL は自動実行せず、呼び出し元コールバック経由でクエリタブへ出力するのみに留める
//                 （破壊的な ALTER/DROP を確認なしに実行しないため）。
// 更新履歴       : 2026/07/29 新規作成（Table Designer 実スキーマ対応・ALTER TABLE生成機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Client.ViewModels;

namespace NeoDbStudio.Client.Views;

#region TableDesignerDialog Class

/// <summary>
/// テーブル構造デザイナーダイアログの Code-Behind。
/// </summary>
public partial class TableDesignerDialog : Window
{
    #region Fields

    private readonly TableDesignerViewModel _designer;
    private readonly string _providerType;
    private readonly Action<string, string> _onGenerateScript; // (タブタイトル, SQL) を受け取りクエリタブとして開くコールバック

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// 対象の TableDesignerViewModel・DBMSプロバイダー種別・ALTER TABLE生成時のコールバックを指定して初期化します。
    /// </summary>
    /// <param name="designer">[パラメータ] 編集対象のテーブルデザイナー ViewModel を指定します。</param>
    /// <param name="providerType">[パラメータ] ALTER TABLE 生成に用いる DBMS プロバイダー種別を指定します。</param>
    /// <param name="onGenerateScript">[パラメータ] 生成された DDL をクエリタブへ出力するコールバックを指定します。</param>
    public TableDesignerDialog(TableDesignerViewModel designer, string providerType, Action<string, string> onGenerateScript)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerDialog.ctor: 開始します");
            InitializeComponent();

            _designer         = designer ?? throw new ArgumentNullException(nameof(designer));
            _providerType     = string.IsNullOrEmpty(providerType) ? "PostgreSQL" : providerType;
            _onGenerateScript = onGenerateScript ?? throw new ArgumentNullException(nameof(onGenerateScript));

            Title                    = _designer.Title;
            TxtTableName.Text        = _designer.Title;
            ColumnsGrid.ItemsSource  = _designer.Columns;
            BadgeRealSchema.Visibility = _designer.IsLoadedFromRealSchema ? Visibility.Visible : Visibility.Collapsed;

            System.Diagnostics.Debug.WriteLine("[INFO] TableDesignerDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 処理内容     : 「Add Column」ボタンの Click イベントを処理します。
    /// </summary>
    private void BtnAddColumn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _designer.AddColumnCommand.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerDialog.BtnAddColumn_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 「Remove Selected」ボタンの Click イベントを処理します。
    /// 処理ロジック : DataGrid で選択中のカラム行を削除コマンドへ渡します。
    /// </summary>
    private void BtnRemoveColumn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ColumnsGrid.SelectedItem is TableDesignColumn column)
            {
                _designer.RemoveColumnCommand.Execute(column);
            }
            else
            {
                MessageBox.Show("削除するカラムを選択してください。", "Remove Column", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerDialog.BtnRemoveColumn_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 「Generate ALTER TABLE」ボタンの Click イベントを処理します。
    /// 処理ロジック : 実スキーマ読込時点との差分から DDL を生成し、コールバック経由でクエリタブへ出力します。
    ///               自動実行はしません（ユーザーが内容を確認してから任意に実行する運用）。
    /// </summary>
    private void BtnGenerateAlter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? script = _designer.GenerateAlterTableScript(_providerType);
            if (string.IsNullOrEmpty(script))
            {
                MessageBox.Show("変更が検出されませんでした（カラムの追加・削除・型変更がありません）。", "Generate ALTER TABLE", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _onGenerateScript($"Alter_{_designer.TableName}.sql", script);
            TxtStatus.Text = "ALTER TABLE スクリプトをクエリタブへ出力しました。内容を確認のうえ実行してください。";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ALTER TABLE スクリプトの生成に失敗しました: {ex.Message}", "Generate ALTER TABLE", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerDialog.BtnGenerateAlter_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 「Close」ボタンの Click イベントを処理します。
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] TableDesignerDialog.BtnClose_Click: 例外発生 - {ex.Message}");
        }
    }

    #endregion
}

#endregion
