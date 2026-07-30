// ファイル名     : InputBoxDialog.xaml.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Views\InputBoxDialog.xaml.cs
// クラス/概要    : InputBoxDialog (Partial Class)
// 処理概要/目的  : 単一行のテキスト入力を受け取る汎用モーダルダイアログ。SQLスニペット保存時の名前入力等で使用。
// 使用方法/適用先: InputBoxDialog.Show(owner, title, prompt, defaultValue) の静的ヘルパー経由で呼び出す。
// 依存関係       : System.Windows
// 注意事項       : Enterキーで確定・Escapeキーでキャンセルに対応。
// 更新履歴       : 2026/07/30 新規作成（SQLスニペットライブラリ機能）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows;
using System.Windows.Input;

namespace NeoDbStudio.Client.Views;

#region InputBoxDialog Class

/// <summary>
/// 単一行テキスト入力用の汎用モーダルダイアログ。
/// </summary>
public partial class InputBoxDialog : Window
{
    #region Properties

    /// <summary>ユーザーが入力した値（キャンセル時は反映されない）。</summary>
    public string InputValue { get; private set; } = string.Empty;

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// InputBoxDialog インスタンスを初期化します。
    /// </summary>
    public InputBoxDialog(string title, string prompt, string defaultValue = "")
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] InputBoxDialog.ctor: 初期化を開始します");

            InitializeComponent();

            Title = title;
            PromptText.Text = prompt;
            InputTextBox.Text = defaultValue;
            Loaded += (_, _) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };

            System.Diagnostics.Debug.WriteLine("[INFO] InputBoxDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InputBoxDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// InputBoxDialog をモーダル表示し、ユーザーがOKで確定した入力値を返却します。キャンセル時は null を返却します。
    /// </summary>
    public static string? Show(Window? owner, string title, string prompt, string defaultValue = "")
    {
        var dlg = new InputBoxDialog(title, prompt, defaultValue) { Owner = owner };
        bool? result = dlg.ShowDialog();
        return result == true ? dlg.InputValue : null;
    }

    #endregion

    #region Event Handlers

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InputValue = InputTextBox.Text?.Trim() ?? string.Empty;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InputBoxDialog.OkButton_Click: 例外発生 - {ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }

    #endregion
}

#endregion
