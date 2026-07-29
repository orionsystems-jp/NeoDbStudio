// ファイル名     : MainWindow.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\MainWindow.xaml.cs
// クラス/概要    : MainWindow (Partial Class)
// 処理概要/目的  : NeoDB Studio メインウィンドウコードビハインド。MSAGL ER図描画、AvalonEdit SQLエディタ高コントラスト構文ハイライト、全10領域マルチカラーテーマダイナミック連動、多言語切り替え、直感コンテキストメニュー制御を統合
// 使用方法/適用先: MainViewModel とデータバインディングおよびテーマ・コントロール相互作用を実施
// 依存関係       : NeoDbStudio.Client.ViewModels.MainViewModel, ModernWpf.ThemeManager, ICSharpCode.AvalonEdit
// 注意事項       : DataTemplate 内部エレメントへのアクセスは FindName および安全なガード記述にて CS0103 を防止します。
// 更新履歴       : 2026/01/01 新規作成
//                 2026/07/29 DataTemplate 移行に伴う CS0103 エラー解消・安全エレメント検索構造強化
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Msagl.WpfGraphControl;
using Microsoft.Win32;
using ModernWpf;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Client.ViewModels;
using NeoDbStudio.Client.Views;

namespace NeoDbStudio.Client;

#region MainWindow Class

/// <summary>
/// NeoDB Studio メインウィンドウコードビハインドクラス。
/// </summary>
[SupportedOSPlatform("windows7.0")]
public partial class MainWindow : Window
{
    #region Private Fields

    private GraphViewer? _gViewer; // MSAGL WPF グラフビューアコントロールインスタンス
    private CompletionWindow? _sqlCompletionWindow; // SQL補完ウィンドウ（タブ毎のエディタインスタンスで共有・多重生成防止用）

    #endregion

    #region Constructors

    /// <summary>
    /// 処理内容     : MainWindow クラスの新しいインスタンスを初期化します。
    /// 処理ロジック : コンポーネントを初期化し、Loaded イベントハンドラーを登録します。
    /// </summary>
    public MainWindow()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.ctor: 初期化を開始します");
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers & Initialization

    /// <summary>
    /// 処理内容     : メインウィンドウの Loaded イベントを処理します。
    /// 処理ロジック : DataContext の自動補完、MSAGL ビューア初期化、ViewModel イベントの購読、多言語テキスト適用および SQL エディターのセットアップを行います。
    /// </summary>
    /// <param name="sender">イベント発生源オブジェクト</param>
    /// <param name="e">イベント引数</param>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.MainWindow_Loaded: コントロール初期化を実行します");

            // DataContext が未注入の場合のフォールバック保証処理
            if (DataContext == null)
            {
                if (App.Services != null)
                {
                    DataContext = App.Services.GetService<MainViewModel>();
                }
            }

            InitMsaglGraphViewer();

            if (DataContext is MainViewModel vm)
            {
                vm.GraphUpdated     += OnGraphUpdated;     // グラフ更新イベントの購読
                vm.DebugLineChanged += OnDebugLineChanged; // デバッグ行変更イベントの購読
                vm.ThemeChanged     += ApplyTheme;         // テーマ変更イベントの購読

                // ActiveQueryTab 変更時の SQL 同期処理
                vm.PropertyChanged += (s, args) =>
                {
                    try
                    {
                        if (args.PropertyName == nameof(MainViewModel.ActiveQueryTab))
                        {
                            SyncActiveTabSqlToEditor(vm);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.ActiveQueryTab PropertyChanged: {ex.Message}");
                    }
                };

                if (vm.ActiveQueryTab != null)
                {
                    // ActiveQueryTab 内の SqlScript 変更時のエディター同期
                    vm.ActiveQueryTab.PropertyChanged += (s, args) =>
                    {
                        try
                        {
                            if (args.PropertyName == nameof(QueryTabViewModel.SqlScript))
                            {
                                SyncActiveTabSqlToEditor(vm);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.SqlScript PropertyChanged: {ex.Message}");
                        }
                    };
                }

                if (vm.SelectedTheme != null)
                {
                    ApplyTheme(vm.SelectedTheme);
                }
            }

            // 言語切替イベントフック処理
            LocalizationManager.Instance.LanguageChanged += (lang) =>
            {
                try
                {
                    ApplyLocalizedTexts();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.LanguageChanged: {ex.Message}");
                }
            };

            SetupActiveTabSqlEditor();

            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.MainWindow_Loaded: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MainWindow_Loaded: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : MSAGL ER図グラフビューアを初期化し、パネルへバインドします。
    /// 処理ロジック : GraphViewer インスタンスを生成し、ホストパネルに登録後に ViewModel のグラフを適用します。
    /// </summary>
    private void InitMsaglGraphViewer()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.InitMsaglGraphViewer: MSAGL ビューアを初期化します");

            var hostGrid = FindName("GViewerHost") as Grid;
            if (hostGrid != null)
            {
                _gViewer = new GraphViewer();
                _gViewer.BindToPanel(hostGrid);

                if (DataContext is MainViewModel vm && vm.Graph != null)
                {
                    _gViewer.Graph = vm.Graph;
                }
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.InitMsaglGraphViewer: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.InitMsaglGraphViewer: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ViewModel からの ER 図更新通知を処理します。
    /// 処理ロジック : ビューアに最新の Graph インスタンスを再設定して再描画を行います。
    /// </summary>
    private void OnGraphUpdated()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.OnGraphUpdated: ER図グラフを再描画します");

            if (_gViewer != null && DataContext is MainViewModel vm && vm.Graph != null)
            {
                _gViewer.Graph = vm.Graph;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.OnGraphUpdated: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : デバッガーの実行行変更通知を処理します。
    /// 処理ロジック : AvalonEdit SQL エディターの指定行へハイライトマーカーをセットします。
    /// </summary>
    /// <param name="lineNumber">ハイライト対象の行番号 (1-indexed)</param>
    private void OnDebugLineChanged(int lineNumber)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainWindow.OnDebugLineChanged: 行ハイライト更新 ({lineNumber})");

            var editor = FindName("SqlEditor") as TextEditor;
            if (editor != null)
            {
                AvalonEditLineHighlighter.SetHighlightLine(editor, lineNumber);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.OnDebugLineChanged: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : アプリケーション全体に多言語ローカライズテキストを一斉更新・適用します。
    /// 処理ロジック : LocalizationManager の辞書から各コントロールの Header / Text プロパティへ設定します。
    /// </summary>
    private void ApplyLocalizedTexts()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainWindow.ApplyLocalizedTexts: UI 表示テキストを切り替えます ({LocalizationManager.Instance.CurrentLanguage})");

            var loc = LocalizationManager.Instance;

            // 1. メニューバー項目の更新
            if (MainMenu != null && MainMenu.Items.Count >= 6)
            {
                if (MainMenu.Items[0] is MenuItem mFile) { mFile.Header = loc["Menu_File"]; }
                if (MainMenu.Items[1] is MenuItem mEdit) { mEdit.Header = loc["Menu_Edit"]; }
                if (MainMenu.Items[2] is MenuItem mView) { mView.Header = loc["Menu_View"]; }
                if (MainMenu.Items[3] is MenuItem mDb)   { mDb.Header   = loc["Menu_Database"]; }
                if (MainMenu.Items[4] is MenuItem mDbg)  { mDbg.Header  = loc["Menu_Debug"]; }
                if (MainMenu.Items[5] is MenuItem mHlp)  { mHlp.Header  = loc["Menu_Help"]; }
            }

            // 2. ヘッダーおよび全般ラベルの更新
            if (LblLanguage != null) { LblLanguage.Text = loc["Label_Language"]; }
            if (LblTheme != null)    { LblTheme.Text    = loc["Label_Theme"]; }

            // 3. リボンバータブおよびボタンテキストの更新
            if (TabHome != null)            { TabHome.Header            = loc["Tab_Home"]; }
            if (TabEngineering != null)     { TabEngineering.Header     = loc["Tab_Engineering"]; }
            if (TabDebugger != null)        { TabDebugger.Header        = loc["Tab_Debugger"]; }
            if (TxtBtnNewQuery != null)     { TxtBtnNewQuery.Text     = loc["Btn_NewQuery"]; }
            if (TxtBtnExecuteQuery != null) { TxtBtnExecuteQuery.Text = loc["Btn_ExecuteQuery"]; }

            // 4. スタート画面テキストの更新
            if (TxtStartSubtitle != null)     { TxtStartSubtitle.Text     = loc["Start_Subtitle"]; }
            if (TxtStartRecentTitle != null)  { TxtStartRecentTitle.Text  = loc["Start_RecentTitle"]; }
            if (TxtStartNewConnTitle != null) { TxtStartNewConnTitle.Text = loc["Start_NewConnTitle"]; }
            if (TxtStartNewConnDesc != null)  { TxtStartNewConnDesc.Text  = loc["Start_NewConnDesc"]; }
            if (TxtStartNewConnBtn != null)   { TxtStartNewConnBtn.Text   = loc["Start_NewConnBtn"]; }
            if (TxtStartOpenProjTitle != null){ TxtStartOpenProjTitle.Text= loc["Start_OpenProjTitle"]; }
            if (TxtStartOpenProjDesc != null) { TxtStartOpenProjDesc.Text = loc["Start_OpenProjDesc"]; }
            if (TxtStartOpenProjBtn != null)  { TxtStartOpenProjBtn.Text  = loc["Start_OpenProjBtn"]; }
            if (TxtStartNoticeTitle != null)  { TxtStartNoticeTitle.Text  = loc["Start_NoticeTitle"]; }
            if (TxtStartNoticeDesc != null)   { TxtStartNoticeDesc.Text   = loc["Start_NoticeDesc"]; }

            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.ApplyLocalizedTexts: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ApplyLocalizedTexts: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : AvalonEdit SQL エディターの Loaded イベントを処理します。
    /// 処理ロジック : DataContext (QueryTabViewModel) の SqlScript をエディターへ無条件に即座セットし、構文ハイライトルールをロード適用します。
    /// </summary>
    /// <param name="sender">イベント発生源 TextEditor</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void SqlEditor_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is TextEditor editor && editor.DataContext is QueryTabViewModel tabVm)
            {
                editor.SyntaxHighlighting = CustomSqlHighlighting.GetDarkThemeSqlHighlighting();
                editor.Text               = tabVm.SqlScript ?? string.Empty; // 確実な即座流し込み

                editor.TextArea.TextEntered -= SqlEditor_TextEntered; // 二重購読防止
                editor.TextArea.TextEntered += SqlEditor_TextEntered;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.SqlEditor_Loaded: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : AvalonEdit SQL エディターの TextEntered イベントを処理し、テーブル名・カラム名・SQLキーワードの補完ウィンドウを表示します。
    /// 処理ロジック : 英字またはアンダースコアが単語の先頭として入力された時点で補完ウィンドウを開き、
    ///               接続中スキーマ（DbObjectTree）から実テーブル名・カラム名の候補一覧を構築します。
    /// </summary>
    /// <param name="sender">イベント発生源 TextArea</param>
    /// <param name="e">テキスト入力イベント引数</param>
    private void SqlEditor_TextEntered(object sender, TextCompositionEventArgs e)
    {
        try
        {
            if (sender is not TextArea textArea || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            char lastChar = e.Text[e.Text.Length - 1];
            if (!char.IsLetter(lastChar) && lastChar != '_') // 補完対象は識別子構成文字（英字・アンダースコア）のみ
            {
                return;
            }

            int caretOffset = textArea.Caret.Offset;
            if (caretOffset >= 2) // 単語の2文字目以降は新規ウィンドウを開かない（AvalonEdit側が既存ウィンドウを自動継続フィルタする）
            {
                char prevChar = textArea.Document.GetCharAt(caretOffset - 2);
                if (char.IsLetterOrDigit(prevChar) || prevChar == '_')
                {
                    return;
                }
            }

            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            var candidates = SqlCompletionProvider.BuildCandidates(vm.DbObjectTree);
            if (candidates.Count == 0)
            {
                return;
            }

            _sqlCompletionWindow = new CompletionWindow(textArea)
            {
                StartOffset = caretOffset - 1 // 既に入力済みの先頭1文字も置換対象へ含める
            };
            foreach (var candidate in candidates)
            {
                _sqlCompletionWindow.CompletionList.CompletionData.Add(candidate);
            }
            _sqlCompletionWindow.Show();
            _sqlCompletionWindow.Closed += (_, _) => _sqlCompletionWindow = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.SqlEditor_TextEntered: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : AvalonEdit SQL エディターの DataContextChanged イベントを処理します。
    /// 処理ロジック : タブ切替等で DataContext が切り替わった瞬間に、対象タブの SqlScript をエディターへセットします。
    /// </summary>
    /// <param name="sender">イベント発生源 TextEditor</param>
    /// <param name="e">依存関係プロパティ変更イベント引数</param>
    private void SqlEditor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (sender is TextEditor editor && editor.DataContext is QueryTabViewModel tabVm)
            {
                editor.SyntaxHighlighting = CustomSqlHighlighting.GetDarkThemeSqlHighlighting();
                editor.Text               = tabVm.SqlScript ?? string.Empty; // DataContext 変更時も確実にセット
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.SqlEditor_DataContextChanged: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : AvalonEdit SQL エディターの TextChanged イベントを処理します。
    /// 処理ロジック : 編集された SQL テキストを DataContext (QueryTabViewModel) の SqlScript へリアルタイム同期します。
    /// </summary>
    /// <param name="sender">イベント発生源 TextEditor</param>
    /// <param name="e">イベント引数</param>
    private void SqlEditor_TextChanged(object sender, EventArgs e)
    {
        try
        {
            if (sender is TextEditor editor && editor.DataContext is QueryTabViewModel tabVm)
            {
                if (tabVm.SqlScript != editor.Text)
                {
                    tabVm.SqlScript = editor.Text;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.SqlEditor_TextChanged: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ViewModel のアクティブクエリタブの SQL テキストを AvalonEdit エディターへ同期します。
    /// 処理ロジック : エディター内容と ViewModel の差異を検知し、一致していない場合のみ更新します。
    /// </summary>
    /// <param name="vm">MainViewModel インスタンス</param>
    private void SyncActiveTabSqlToEditor(MainViewModel vm)
    {
        try
        {
            var editor = FindName("SqlEditor") as TextEditor;
            if (vm.ActiveQueryTab != null && editor != null)
            {
                if (editor.Text != vm.ActiveQueryTab.SqlScript)
                {
                    editor.Text = vm.ActiveQueryTab.SqlScript ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.SyncActiveTabSqlToEditor: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : AvalonEdit SQL エディター構文ハイライトと初期前景色をセットアップします。
    /// 処理ロジック : ダークテーマ用構文ハイライトルールおよびフォントサイズを適用します。
    /// </summary>
    private void SetupActiveTabSqlEditor()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.SetupActiveTabSqlEditor: SQL エディターをセットアップします");

            var editor = FindName("SqlEditor") as TextEditor;
            if (editor != null)
            {
                editor.SyntaxHighlighting = CustomSqlHighlighting.GetDarkThemeSqlHighlighting();
                editor.FontSize           = 14;
                editor.Foreground         = new SolidColorBrush(Color.FromRgb(86, 156, 214)); // #569CD6 (VS Code シアン)
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.SetupActiveTabSqlEditor: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : TreeView のダブルクリックイベントを処理します。
    /// 処理ロジック : テーブルまたはビューノードがダブルクリックされた場合、SELECT スクリプト生成コマンドを自動実行します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">マウスボタンイベント引数</param>
    private void TvDbObjects_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            if (TvDbObjects.SelectedItem is DbObjectNode node && (node.Type == DbObjectType.Table || node.Type == DbObjectType.View))
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.ScriptSelectCommand.Execute(node);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.TvDbObjects_MouseDoubleClick: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 接続履歴 ScrollViewer のプレビュースクロールイベントを処理します。
    /// 処理ロジック : マウスホイールのスクロールイベントを捕捉し、垂直方向へスクロール処理を行います。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">マウスホイールイベント引数</param>
    private void HistoryScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        try
        {
            if (sender is ScrollViewer scroller)
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.HistoryScrollViewer_PreviewMouseWheel: {ex.Message}");
        }
    }

    #region TreeView ContextMenu Direct Click Event Handlers

    /// <summary>
    /// 処理内容     : クエリタブ「🗔 分離 (Float Window)」ボタンの Click イベントを処理します。
    /// 処理ロジック : クリックされたクエリタブをドッキング領域から分離し、独立した FloatingQueryWindow を生成してオープン表示します。
    /// </summary>
    /// <param name="sender">イベント発生源 Button</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void BtnFloatTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.DataContext is QueryTabViewModel tabVm)
            {
                System.Diagnostics.Debug.WriteLine($"[INFO] MainWindow.BtnFloatTab_Click: クエリタブ [{tabVm.Title}] をフローティング分離表示します");
                var floatWin = new NeoDbStudio.Client.Views.FloatingQueryWindow(tabVm);
                floatWin.Show();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.BtnFloatTab_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : クエリタブ「✕」ボタンの Click イベントを処理します。
    /// 処理ロジック : 対象の QueryTabViewModel を QueryTabs コレクションから削除します。
    /// </summary>
    /// <param name="sender">イベント発生源 Button</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.DataContext is QueryTabViewModel tabVm)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.CloseQueryTabCommand.Execute(tabVm);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.BtnCloseTab_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu から右クリック対象の DbObjectNode を抽出取得します。
    /// 処理ロジック : PlacementTarget または TreeView.SelectedItem からノードを取得します。
    /// </summary>
    /// <param name="sender">イベント発生源 MenuItem</param>
    /// <returns>取得された DbObjectNode または null</returns>
    private DbObjectNode? GetSelectedNodeFromContextMenu(object sender)
    {
        try
        {
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is FrameworkElement element && element.DataContext is DbObjectNode node)
                {
                    return node;
                }
            }
            return TvDbObjects.SelectedItem as DbObjectNode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.GetSelectedNodeFromContextMenu: {ex.Message}");
            return TvDbObjects.SelectedItem as DbObjectNode;
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「SELECT スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの SELECT スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptSelect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptSelectCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptSelect_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「INSERT スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの INSERT スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptInsert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptInsertCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptInsert_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「UPDATE スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの UPDATE スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptUpdateCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptUpdate_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「DELETE スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの DELETE スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptDeleteCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptDelete_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「CREATE TABLE スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの CREATE DDL スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptCreate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptCreateCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptCreate_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「DROP TABLE スクリプトの生成」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードの DROP DDL スクリプト生成コマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_ScriptDrop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (DataContext is MainViewModel vm)
            {
                vm.ScriptDropCommand.Execute(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_ScriptDrop_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「テーブル構造の編集 (Design Table)」の Click イベントを処理します。
    /// 処理ロジック : 対象ノードのテーブル名を取得し、テーブル構造デザイナー画面オープンコマンドを発火します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_DesignTable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var node = GetSelectedNodeFromContextMenu(sender);
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            if (DataContext is MainViewModel vm)
            {
                vm.OpenTableDesignerCommand.Execute(tableName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_DesignTable_Click: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region ER Diagram Export

    /// <summary>
    /// 処理内容     : ER図の「Reset View」ボタンの Click イベントを処理します。
    /// 処理ロジック : GraphViewer へ Graph を再設定し、パネルに合わせて自動的に再フィットさせます。
    /// </summary>
    private void BtnResetErView_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_gViewer != null && DataContext is MainViewModel vm && vm.Graph != null)
            {
                _gViewer.Graph = vm.Graph; // 再代入により GraphViewer が既定のフィット表示へ戻す
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.BtnResetErView_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ER図の「Export as PNG」ボタンの Click イベントを処理します。
    /// 処理ロジック : GViewerHost パネルの現在の描画内容を RenderTargetBitmap でキャプチャし、PNG ファイルへ保存します。
    /// </summary>
    private void BtnExportErPng_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hostGrid = FindName("GViewerHost") as FrameworkElement;
            if (hostGrid == null || _gViewer == null)
            {
                MessageBox.Show("ER図が初期化されていません。先にスキーマを読み込んでください。", "Export ER Diagram", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hostGrid.ActualWidth < 1 || hostGrid.ActualHeight < 1)
            {
                MessageBox.Show("ER図の表示領域が確定していません。ER Diagramタブを開いた状態で再度お試しください。", "Export ER Diagram", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                FileName = "ER_Diagram.png"
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            hostGrid.UpdateLayout(); // レイアウト確定を保証してからキャプチャする

            int width  = (int)Math.Ceiling(hostGrid.ActualWidth);
            int height = (int)Math.Ceiling(hostGrid.ActualHeight);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(hostGrid);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var stream = System.IO.File.Create(dlg.FileName))
            {
                encoder.Save(stream);
            }

            MessageBox.Show($"ER図をPNGへエクスポートしました。\n{dlg.FileName}", "Export ER Diagram", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ER図のエクスポートに失敗しました: {ex.Message}", "Export ER Diagram", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.BtnExportErPng_Click: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Result DataGrid BLOB/CLOB Viewer

    private readonly BlobMarkerConverter _blobMarkerConverter = new();

    /// <summary>
    /// 処理内容     : 結果グリッドの列自動生成（AutoGeneratingColumn）イベントを処理します。
    /// 処理ロジック : 生成された各列の Binding へ BlobMarkerConverter を適用し、
    ///               BLOB/CLOB値が「[BLOB N bytes]」の要約表示になるようにします。
    /// </summary>
    private void ResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        try
        {
            if (e.Column is DataGridTextColumn textColumn && textColumn.Binding is System.Windows.Data.Binding binding)
            {
                binding.Converter = _blobMarkerConverter;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ResultGrid_AutoGeneratingColumn: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 結果グリッドのセルダブルクリック（PreviewMouseDoubleClick）イベントを処理します。
    /// 処理ロジック : クリックされたセルの実データ（DataRowView の生値）がBLOBマーカー付きの場合、
    ///               標準の編集モード開始を抑止して BlobViewerDialog を表示します。BLOB以外は通常どおり編集させます。
    /// </summary>
    private void ResultGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell?.Column is not DataGridBoundColumn boundColumn || cell.DataContext is not System.Data.DataRowView rowView)
            {
                return;
            }
            if (boundColumn.Binding is not System.Windows.Data.Binding binding || binding.Path == null)
            {
                return;
            }

            string columnName = binding.Path.Path;
            if (!rowView.Row.Table.Columns.Contains(columnName))
            {
                return;
            }

            string? rawValue = rowView.Row[columnName]?.ToString();
            byte[]? bytes = NeoDbStudio.Shared.BlobMarker.Decode(rawValue);
            if (bytes == null)
            {
                return; // BLOB以外のセルは通常のインライン編集へ委ねる
            }

            e.Handled = true; // 標準の編集モード開始を抑止する
            var viewer = new BlobViewerDialog(bytes) { Owner = this };
            viewer.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ResultGrid_PreviewMouseDoubleClick: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>指定型の最も近い祖先ビジュアル要素を検索します。</summary>
    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed)
            {
                return typed;
            }
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    #endregion

    #region Result DataGrid Inline Edit (UPDATE Auto-generation)

    /// <summary>
    /// 処理内容     : 結果グリッドの行編集完了（RowEditEnding）イベントを処理します。
    /// 処理ロジック : 単一テーブルの単純な SELECT かつ主キーが特定できる場合に限り、
    ///               編集差分から UPDATE 文を自動生成して実DBへ反映します。対象外・失敗時は変更を破棄します。
    /// 注意事項     : WPF DataGrid の仕様上、RowEditEnding 発火時点ではまだセル編集値が
    ///               バインディング先（DataRow）へ確定していないため、Dispatcher で1ティック遅延させてから処理する。
    /// </summary>
    /// <param name="sender">イベント発生源 DataGrid</param>
    /// <param name="e">行編集終了イベント引数</param>
    private void ResultGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        try
        {
            if (e.EditAction != DataGridEditAction.Commit) // キャンセル時は何もしない（DataGridが自動的に元へ戻す）
            {
                return;
            }

            if (sender is not DataGrid grid || grid.DataContext is not QueryTabViewModel tabVm)
            {
                return;
            }
            if (DataContext is not MainViewModel vm)
            {
                return;
            }
            if (e.Row.Item is not System.Data.DataRowView rowView)
            {
                return;
            }

            var row = rowView.Row;

            // RowEditEnding 発火時点ではまだセル値がDataRowへ反映されていないため、
            // ディスパッチャキューの処理が一巡した後（バインディング確定後）に非同期で処理する
            Dispatcher.BeginInvoke(new Action(async () => await ApplyInlineRowEditAsync(vm, tabVm, row)), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ResultGrid_RowEditEnding: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 編集された DataRow から UPDATE 文を生成し、実DBへ反映します。
    ///
    /// [2. 処理フロー]
    /// 1. 実行済みSQLから対象テーブル名を抽出します（JOIN等の複数テーブルは対象外）。
    /// 2. 直近取得済みスキーマから主キー列を取得します。
    /// 3. Original/Current の差分から UPDATE 文を組み立て、DBMS へ実行します。
    /// 4. 成功時は AcceptChanges、失敗時・対象外時は RejectChanges で編集内容を破棄します。
    /// </summary>
    /// <param name="vm">[パラメータ] MainViewModel インスタンスを指定します。</param>
    /// <param name="tabVm">[パラメータ] 対象クエリタブの ViewModel を指定します。</param>
    /// <param name="row">[パラメータ] 編集された DataRow を指定します。</param>
    private async Task ApplyInlineRowEditAsync(MainViewModel vm, QueryTabViewModel tabVm, System.Data.DataRow row)
    {
        try
        {
            string? tableName = SqlTableAnalyzer.ExtractSingleSourceTableName(tabVm.SqlScript);
            if (tableName == null)
            {
                MessageBox.Show("複数テーブルにまたがる結果、または単一テーブルを特定できないクエリはインライン編集に対応していません。", "Inline Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
                row.RejectChanges();
                return;
            }

            if (!vm.TryGetPrimaryKeyColumns(tableName, out var pkColumns) || pkColumns.Count == 0)
            {
                MessageBox.Show($"テーブル '{tableName}' の主キーが特定できないため編集を反映できません（オブジェクトツリーからスキーマを一度読み込んでください）。", "Inline Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
                row.RejectChanges();
                return;
            }

            string? updateSql = QueryTabViewModel.BuildInlineUpdateSql(tableName, row, pkColumns, out string? buildError);
            if (updateSql == null)
            {
                if (buildError != null) // 実質的な変更なし（null かつ buildError なし）の場合はメッセージを出さない
                {
                    MessageBox.Show(buildError, "Inline Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    row.RejectChanges();
                }
                return;
            }

            await tabVm.ExecuteRawStatementAsync(updateSql);

            row.AcceptChanges(); // 成功時のみ確定（Original値を更新し次回編集の基準にする）
            vm.AddLogEntry($"Inline edit applied: {updateSql}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"編集の反映に失敗しました。変更を元に戻します。\n{ex.Message}", "Inline Edit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            row.RejectChanges();
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ApplyInlineRowEditAsync: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Result DataGrid Context Menu (Copy / Export)

    /// <summary>
    /// ContextMenu の PlacementTarget から対象 DataGrid を取得します。
    /// </summary>
    /// <param name="sender">イベント発生源 MenuItem</param>
    /// <returns>取得された DataGrid または null</returns>
    private static DataGrid? GetResultGridFromContextMenu(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu
            && contextMenu.PlacementTarget is DataGrid grid)
        {
            return grid;
        }
        return null;
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「セルをコピー (Copy Cell)」の Click イベントを処理します。
    /// 処理ロジック : 現在選択中のセルの表示テキストをクリップボードへコピーします。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_GridCopyCell_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = GetResultGridFromContextMenu(sender);
            if (grid?.CurrentCell.Column == null || grid.CurrentCell.Item == null) return;

            var content = grid.CurrentCell.Column.GetCellContent(grid.CurrentCell.Item) as TextBlock;
            Clipboard.SetText(content?.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_GridCopyCell_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「行をコピー (Copy Row)」の Click イベントを処理します。
    /// 処理ロジック : 現在選択中の行の全カラム値をタブ区切りでクリップボードへコピーします。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private void MenuItem_GridCopyRow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = GetResultGridFromContextMenu(sender);
            if (grid?.SelectedItem is not DataRowView rowView) return;

            string line = string.Join("\t", rowView.Row.ItemArray.Select(v => v?.ToString() ?? string.Empty));
            Clipboard.SetText(line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_GridCopyRow_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「CSVへエクスポート (Export CSV)」の Click イベントを処理します。
    /// 処理ロジック : 表示中ページに限らずクエリ結果全件を CSV 形式でファイルへ書き出します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private async void MenuItem_GridExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = GetResultGridFromContextMenu(sender);
            if (grid?.DataContext is not QueryTabViewModel tabVm) return;

            // サーバー側ページング中は表示ページ分だけでなく全件を対象とするため非同期版で再取得する
            var table = await tabVm.GetFullResultTableAsync();
            if (table.Rows.Count == 0)
            {
                MessageBox.Show("エクスポートするデータがありません。先にクエリを実行してください。", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*", FileName = $"{SanitizeFileName(tabVm.Title)}.csv" };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => EscapeCsvField(c.ColumnName))));
            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine(string.Join(",", row.ItemArray.Select(v => EscapeCsvField(v?.ToString() ?? string.Empty))));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // BOM付与でExcelでの文字化けを回避
            MessageBox.Show($"{table.Rows.Count} 行を CSV へエクスポートしました。\n{dlg.FileName}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_GridExportCsv_Click: 例外発生 - {ex.Message}");
            MessageBox.Show($"CSVエクスポートに失敗しました: {ex.Message}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 処理内容     : ContextMenu 「JSONへエクスポート (Export JSON)」の Click イベントを処理します。
    /// 処理ロジック : 表示中ページに限らずクエリ結果全件を JSON 配列形式でファイルへ書き出します。
    /// </summary>
    /// <param name="sender">イベント発生源</param>
    /// <param name="e">ルーティングイベント引数</param>
    private async void MenuItem_GridExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = GetResultGridFromContextMenu(sender);
            if (grid?.DataContext is not QueryTabViewModel tabVm) return;

            // サーバー側ページング中は表示ページ分だけでなく全件を対象とするため非同期版で再取得する
            var table = await tabVm.GetFullResultTableAsync();
            if (table.Rows.Count == 0)
            {
                MessageBox.Show("エクスポートするデータがありません。先にクエリを実行してください。", "Export JSON", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*", FileName = $"{SanitizeFileName(tabVm.Title)}.json" };
            if (dlg.ShowDialog() != true) return;

            var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object?>>();
            foreach (DataRow row in table.Rows)
            {
                var obj = new System.Collections.Generic.Dictionary<string, object?>();
                foreach (DataColumn col in table.Columns)
                {
                    obj[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                }
                rows.Add(obj);
            }

            string json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            MessageBox.Show($"{table.Rows.Count} 行を JSON へエクスポートしました。\n{dlg.FileName}", "Export JSON", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.MenuItem_GridExportJson_Click: 例外発生 - {ex.Message}");
            MessageBox.Show($"JSONエクスポートに失敗しました: {ex.Message}", "Export JSON", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// CSV フィールド値をエスケープします（カンマ・二重引用符・改行を含む場合のみ引用符で囲みます）。
    /// </summary>
    /// <param name="value">[パラメータ] エスケープ対象の値を指定します。</param>
    /// <returns>CSV フィールドとして安全な文字列を返却します。</returns>
    private static string EscapeCsvField(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    /// <summary>
    /// エクスポート時の既定ファイル名に使用できないよう、タブタイトルからファイルシステム禁止文字を除去します。
    /// </summary>
    /// <param name="title">[パラメータ] 元となるタブタイトルを指定します。</param>
    /// <returns>ファイル名として使用可能な文字列を返却します。</returns>
    private static string SanitizeFileName(string title)
    {
        string sanitized = title;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(sanitized) ? "export" : sanitized;
    }

    #endregion

    #endregion

    #region Multi-Color Theme Engine (High Contrast Readability Guaranteed)

    /// <summary>
    /// 処理内容     : 選択されたカラーテーマに基づき、背景色と文字色の最適なコントラストを保証しながらアプリ内全UIエレメントの色を一括更新します。
    /// 処理ロジック : AppTheme の設定値を読み取り、各コントロールの Background / Foreground / BorderBrush プロパティおよび AvalonEdit の高コントラスト前景色を再設定します。
    /// </summary>
    /// <param name="theme">適用する AppTheme インスタンス</param>
    private void ApplyTheme(AppTheme theme)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainWindow.ApplyTheme: {theme?.Name} を全UI領域へ適用します");

            if (theme == null) { return; }

            // 1. ModernWpf アプリ全体テーマ設定
            ThemeManager.Current.ApplicationTheme = theme.Mode == ThemeMode.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;

            // 各種 SolidColorBrush インスタンスの構築
            var bgBrush       = new SolidColorBrush(theme.BackgroundColor);
            var fgBrush       = new SolidColorBrush(theme.ForegroundColor);
            var secFgBrush    = new SolidColorBrush(theme.SecondaryForegroundColor);
            var cardBgBrush   = new SolidColorBrush(theme.CardBackgroundColor);
            var headerBgBrush = new SolidColorBrush(theme.HeaderBackgroundColor);
            var borderBrush   = new SolidColorBrush(theme.BorderColor);
            var accentBrush   = new SolidColorBrush(theme.AccentColor);
            var editorBg      = new SolidColorBrush(theme.EditorBackgroundColor);
            var editorLine    = new SolidColorBrush(theme.EditorLineNumberColor);

            // 2. メインウィンドウ全体の背景色・前景色
            if (RootGrid != null)
            {
                RootGrid.Background = bgBrush;
            }
            Foreground = fgBrush;

            // 3. ヘッダー背景色および境界線
            if (HeaderBorder != null)
            {
                HeaderBorder.Background  = headerBgBrush;
                HeaderBorder.BorderBrush = borderBrush;
            }

            // 4. Classic メニューバー前景色
            if (MainMenu != null)
            {
                MainMenu.Foreground = fgBrush;
            }

            // 5. スタート画面の背景色
            if (StartPageScrollViewer != null)
            {
                StartPageScrollViewer.Background = bgBrush;
            }

            // 6. スタート画面の全カード背景色・境界線
            if (CardBorder1 != null)       { CardBorder1.Background       = cardBgBrush; CardBorder1.BorderBrush       = borderBrush; }
            if (CardBorder2 != null)       { CardBorder2.Background       = cardBgBrush; CardBorder2.BorderBrush       = borderBrush; }
            if (CardBorderHistory != null) { CardBorderHistory.Background = cardBgBrush; CardBorderHistory.BorderBrush = borderBrush; }

            // スタート画面内の全 TextBlock 文字色を動的リフレッシュ
            if (StartPageScrollViewer != null)
            {
                UpdateTextBlockColors(StartPageScrollViewer, fgBrush, secFgBrush);
            }

            // 7. ステータスバー背景色
            if (MainStatusBar != null)
            {
                MainStatusBar.Background = accentBrush;
            }

            // 8. ワークスペースパネル境界線
            if (ExplorerBorder != null) { ExplorerBorder.BorderBrush = borderBrush; }
            if (CenterBorder != null)   { CenterBorder.BorderBrush   = borderBrush; }
            if (RightBorder != null)    { RightBorder.BorderBrush    = borderBrush; }
            if (BottomBorder != null)   { BottomBorder.BorderBrush   = borderBrush; }

            // 9. AvalonEdit SQL エディターの背景色・文字色・行番号色（高コントラスト視認性）
            var editor = FindName("SqlEditor") as TextEditor;
            if (editor != null)
            {
                editor.Background            = editorBg;
                editor.Foreground            = theme.Mode == ThemeMode.Light ? new SolidColorBrush(Color.FromRgb(17, 24, 39)) : new SolidColorBrush(Color.FromRgb(86, 156, 214)); // VS Code シアンブルー
                editor.LineNumbersForeground = editorLine;
                editor.FontSize              = 14;
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainWindow.ApplyTheme: 高コントラストテーマ適用が正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainWindow.ApplyTheme: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : ビジュアルツリーを再帰的に走査し、TextBlock コントロールの文字色をテーマカラーへ更新します。
    /// 処理ロジック : 子要素を走査し、SolidColorBrush の色判定に基づいてテーマの前景色を再設定します。
    /// </summary>
    /// <param name="parent">親 DependencyObject</param>
    /// <param name="mainFg">メイン前景色 Brush</param>
    /// <param name="secFg">サブ前景色 Brush</param>
    private void UpdateTextBlockColors(DependencyObject parent, Brush mainFg, Brush secFg)
    {
        try
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock tb)
                {
                    if (tb.Foreground is SolidColorBrush scb)
                    {
                        if (scb.Color == Colors.White || scb.Color == Color.FromRgb(255, 255, 255))
                        {
                            tb.Foreground = mainFg;
                        }
                        else if (scb.Color == Color.FromRgb(170, 170, 170) || scb.Color == Color.FromRgb(136, 136, 136))
                        {
                            tb.Foreground = secFg;
                        }
                    }
                }

                UpdateTextBlockColors(child, mainFg, secFg);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainWindow.UpdateTextBlockColors: {ex.Message}");
        }
    }

    #endregion
}

#endregion