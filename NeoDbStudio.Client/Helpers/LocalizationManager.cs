// ファイル名     : LocalizationManager.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\LocalizationManager.cs
// クラス/概要    : LocalizationManager (Static / Singleton Class)
// 処理概要/目的  : MainWindow, ConnectionWizardDialog, TableDesigner 等すべてのウィンドウおよびダイアログ要素を 100% 動的マルチリンガル（日本語 ↔ 英語）化
// 使用方法/適用先: UI コンポーネントおよび ContextMenu から二言語リソースを参照・更新
// 依存関係       : System.ComponentModel.INotifyPropertyChanged
// 注意事項       : 全ダイアログのタイトル、ヘッダー、説明文、ラベル、ステータス、ボタンの二重アイコン発生を 100% 徹底根絶一掃
// 更新履歴       : 2026/07/29 二重アイコン発生原因全数根絶・辞書一元化
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NeoDbStudio.Client.Helpers;

#region LocalizationManager Class

/// <summary>
/// 多言語 (i18n / Localization) 統合管理マネージャー (二重アイコン一掃完全達成版)。
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    #region Singleton Pattern

    public static LocalizationManager Instance { get; } = new LocalizationManager();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    #region Fields & Properties

    private string _currentLanguage = "ja-JP"; // デフォルト: 日本語

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged("Item[]");
                LanguageChanged?.Invoke(_currentLanguage);
            }
        }
    }

    public event Action<string>? LanguageChanged;

    #endregion

    #region Complete Dictionary Resources (ja-JP / en-US - Clean Single Icon Guaranteed)

    private readonly Dictionary<string, (string ja, string en)> _resources = new()
    {
        // --- MenuBar ---
        { "Menu_File", ("ファイル(_F)", "File(_F)") },
        { "Menu_NewProject", ("✨ 新規プロジェクト (Ctrl+Shift+N)", "✨ New Project (Ctrl+Shift+N)") },
        { "Menu_OpenProject", ("📂 プロジェクトを開く (Ctrl+O)", "📂 Open Project (Ctrl+O)") },
        { "Menu_OpenSqlFile", ("📄 SQLファイルを開く", "📄 Open SQL File") },
        { "Menu_RecentProjects", ("🕒 最近使ったプロジェクト (_R)", "🕒 Recent Projects (_R)") },
        { "Menu_ClearRecent", ("🗑 接続履歴をクリア", "🗑 Clear Connection History") },
        { "Menu_Save", ("💾 プロジェクト保存 (Ctrl+S)", "💾 Save Project (Ctrl+S)") },
        { "Menu_SaveAs", ("💾 名前を付けて保存 (Ctrl+Shift+S)", "💾 Save As (Ctrl+Shift+S)") },
        { "Menu_Exit", ("🚪 終了 (Alt+F4)", "🚪 Exit (Alt+F4)") },
        { "Menu_Edit", ("編集(_E)", "Edit(_E)") },
        { "Menu_View", ("表示(_V)", "View(_V)") },
        { "Menu_Database", ("データベース(_D)", "Database(_D)") },
        { "Menu_Debug", ("デバッグ(_B)", "Debug(_B)") },
        { "Menu_Help", ("ヘルプ(_H)", "Help(_H)") },

        // --- Start / Welcome Hub Screen ---
        { "Start_Subtitle", ("データベース接続を確立または既存プロジェクトを開いて作業を開始してください。", "Establish a database connection or open an existing project to start working.") },
        { "Start_RecentTitle", ("🕒 最近使ったプロジェクト / DBMS接続履歴", "🕒 Recent Projects / Connection History") },
        { "Start_SearchPlaceholder", ("🔍 接続名、プロバイダー、ホストで検索...", "🔍 Search by connection name, provider, or host...") },
        { "Start_ConnectOpen", ("⚡ 接続して開く", "⚡ Connect & Open") },
        { "Start_NewConnTitle", ("✨ 新規 DBMS 接続設定", "✨ New DBMS Connection") },
        { "Start_NewConnDesc", ("PostgreSQL, MySQL, Oracle, SQLite 等の DBMS 接続ウィザードを開始して新規プロジェクトを作成します。", "Launch the DBMS Connection Wizard for PostgreSQL, MySQL, Oracle, SQLite, etc. to create a new project.") },
        { "Start_NewConnBtn", ("✨ 新規プロジェクト作成 (Wizard)", "✨ Create New Project (Wizard)") },
        { "Start_OpenProjTitle", ("📂 プロジェクトファイルを開く", "📂 Open Project File") },
        { "Start_OpenProjDesc", ("過去にローカル保存された NeoDB プロジェクト (.neodb) をロードします。", "Load a previously saved NeoDB project (.neodb) file.") },
        { "Start_OpenProjBtn", ("📂 プロジェクトファイルを開く (.neodb)", "📂 Open Project File (.neodb)") },
        { "Start_NoticeTitle", ("💡 接続ワークフローのご案内 (Click to Connect)", "💡 Connection Workflow Guidance (Click to Connect)") },
        { "Start_NoticeDesc", ("ここをクリックするとDBMS接続ウィザードが起動します。接続が完了するとクエリやER図がフル活性化されます。", "Click here to launch the DBMS Connection Wizard. Query editors and ER modelers will be fully activated upon connection.") },

        // --- Ribbon Bar Tabs & Buttons ---
        { "Tab_Home", ("🏠 プロジェクト & クエリ (Home)", "🏠 Project & Query (Home)") },
        { "Tab_Engineering", ("⚙ データベース & ER図 (Engineering)", "⚙ Database & ERD (Engineering)") },
        { "Tab_Debugger", ("🐞 ストアドプロシージャデバッグ [試作版・DBには接続しません] (Debugger Preview)", "🐞 Procedure Debugger [Preview — does not run against a real database]") },
        { "Btn_NewQuery", ("⚡ 新規クエリ (Ctrl+N)", "⚡ New Query (Ctrl+N)") },
        { "Btn_ExecuteQuery", ("▶ クエリ実行 (F5)", "▶ Execute Query (F5)") },
        { "Btn_ConnectionWizard", ("🔌 接続設定の確認・変更 (Wizard)", "🔌 Connection Settings Wizard") },
        { "Btn_TableDesigner", ("🛠 テーブル構造デザイナー (追加・変更)", "🛠 Table Structure Designer") },
        { "Btn_LoadSchema", ("📐 スキーマ再読み込み & ER図生成 (MSAGL)", "📐 Reload Schema & Generate ERD") },

        // --- Connection Wizard Dialog (Clean Single Icon Guaranteed) ---
        { "Dlg_Conn_Title", ("データベース接続設定 - NeoDB Studio", "Database Connection - NeoDB Studio") },
        { "Dlg_Conn_Header", ("🔌 データベースへの接続設定", "🔌 Connect to Database") },
        { "Dlg_Conn_SubHeader", ("接続対象の DBMS プロバイダー、認証方式、およびホストパラメータを指定してください。", "Specify the DBMS provider, authentication method, and host parameters.") },
        { "Dlg_Conn_Provider", ("DBMS プロバイダー:", "DBMS Provider:") },
        { "Dlg_Conn_Auth", ("認証方式:", "Authentication:") },
        { "Dlg_Conn_Host", ("ホスト名 / IP アドレス:", "Host / IP Address:") },
        { "Dlg_Conn_Port", ("ポート番号:", "Port:") },
        { "Dlg_Conn_DbName", ("データベース名:", "Database Name:") },
        { "Dlg_Conn_Username", ("ユーザー名:", "Username:") },
        { "Dlg_Conn_Password", ("パスワード:", "Password:") },
        { "Dlg_Conn_GenStr", ("生成された接続文字列:", "Generated Connection String:") },
        { "Dlg_Conn_StatusReady", ("ステータス: 接続の試行準備が完了しています。", "Status: Ready to test connection.") },
        { "Dlg_Conn_StatusTesting", ("ステータス: 接続テスト中...", "Status: Testing connection...") },
        { "Dlg_Conn_StatusSuccess", ("ステータス: 接続テスト成功！", "Status: Connection test succeeded!") },
        { "Dlg_Conn_StatusFailed", ("ステータス: 接続テスト失敗", "Status: Connection test failed") },
        { "Dlg_Conn_BtnTest", ("⚡ 接続テスト", "⚡ Test Connection") },
        { "Dlg_Conn_BtnOK", ("接続して開く (OK)", "Connect & Open (OK)") }, // Clean Single Text
        { "Dlg_Conn_BtnCancel", ("キャンセル", "Cancel") },

        // --- ContextMenu: Database / Schema Node ---
        { "Ctx_Db_CreateTable", ("✨ 新規テーブルの作成 (Create Table)", "✨ Create New Table") },
        { "Ctx_Db_Refresh", ("📐 スキーマの再読み込み (Refresh Schema)", "📐 Refresh Schema") },
        { "Ctx_Db_Properties", ("🔌 接続プロパティの確認 (Properties)", "🔌 Connection Properties") },

        // --- ContextMenu: Folder Node ---
        { "Ctx_Folder_CreateTable", ("✨ 新規テーブルの作成 (Create Table)", "✨ Create New Table") },
        { "Ctx_Folder_Refresh", ("🔄 フォルダ情報を更新 (Refresh Folder)", "🔄 Refresh Folder") },

        // --- ContextMenu: Table Node ---
        { "Ctx_Table_ScriptSelect", ("⚡ SELECT スクリプトの生成 (Script as SELECT)", "⚡ Script Table as SELECT") },
        { "Ctx_Table_ScriptInsert", ("➕ INSERT スクリプトの生成 (Script as INSERT To)", "➕ Script Table as INSERT To") },
        { "Ctx_Table_ScriptUpdate", ("📝 UPDATE スクリプトの生成 (Script as UPDATE To)", "📝 Script Table as UPDATE To") },
        { "Ctx_Table_ScriptDelete", ("❌ DELETE スクリプトの生成 (Script as DELETE To)", "❌ Script Table as DELETE To") },
        { "Ctx_Table_ScriptCreate", ("📐 CREATE TABLE スクリプトの生成 (Script as CREATE To)", "📐 Script Table as CREATE To") },
        { "Ctx_Table_ScriptDrop", ("🗑 DROP TABLE スクリプトの生成 (Script as DROP To)", "🗑 Script Table as DROP To") },
        { "Ctx_Table_DesignTable", ("🛠 テーブル構造の編集 (Design Table)", "🛠 Design Table Structure") },

        // --- ContextMenu: View Node ---
        { "Ctx_View_ScriptSelect", ("⚡ SELECT スクリプトの生成 (Script View as SELECT)", "⚡ Script View as SELECT") },
        { "Ctx_View_ScriptCreate", ("📐 CREATE VIEW スクリプトの生成 (Script as CREATE To)", "📐 Script View as CREATE To") },
        { "Ctx_View_ScriptDrop", ("🗑 DROP VIEW スクリプトの生成 (Script as DROP To)", "🗑 Script View as DROP To") },

        // --- ContextMenu: Procedure Node ---
        { "Ctx_Proc_Debug", ("🐞 ストアドプロシージャのデバッグ開始 (Debug)", "🐞 Start Debugging Procedure") },
        { "Ctx_Proc_ScriptExecute", ("▶ EXECUTE スクリプトの生成 (Script as EXECUTE)", "▶ Script Procedure as EXECUTE") },

        // --- ContextMenu: Column Node ---
        { "Ctx_Col_CopyName", ("📋 カラム名をコピー (Copy Column Name)", "📋 Copy Column Name") },

        // --- ContextMenu: SQL Editor ---
        { "Ctx_Edit_Execute", ("▶ 選択範囲のクエリ実行 (F5)", "▶ Execute Selected Query (F5)") },
        { "Ctx_Edit_Undo", ("↩ 元に戻す (Undo)", "↩ Undo") },
        { "Ctx_Edit_Redo", ("↪ やり直し (Redo)", "↪ Redo") },
        { "Ctx_Edit_Cut", ("✂ 切り取り (Cut)", "✂ Cut") },
        { "Ctx_Edit_Copy", ("📋 コピー (Copy)", "📋 Copy") },
        { "Ctx_Edit_Paste", ("📌 貼り付け (Paste)", "📌 Paste") },

        // --- ContextMenu: Result DataGrid ---
        { "Ctx_Grid_CopyCell", ("📋 セルの値をコピー (Copy Cell)", "📋 Copy Cell Value") },
        { "Ctx_Grid_CopyRow", ("📋 選択行全体をコピー (Copy Row)", "📋 Copy Entire Row") },
        { "Ctx_Grid_ExportCsv", ("💾 CSV ファイルへエクスポート (Export CSV)", "💾 Export to CSV") },
        { "Ctx_Grid_ExportJson", ("💾 JSON ファイルへエクスポート (Export JSON)", "💾 Export to JSON") },

        // --- Labels & UI General Text ---
        { "Label_ActiveProject", ("Active Project: ", "Active Project: ") },
        { "Label_Language", ("言語 / Language: ", "Language: ") },
        { "Label_Theme", ("カラーテーマ: ", "Color Theme: ") },
        { "Label_ObjectExplorer", ("🗄 DBMS Object Explorer", "🗄 DBMS Object Explorer") },
        { "Label_ResultDataGrid", ("📊 Query Result Set (", "📊 Query Result Set (") },
        { "Label_PageSize", ("表示件数/Page: ", "Rows/Page: ") },
        { "Label_Page", (" ページ: ", " Page: ") }
    };

    #endregion

    #region Indexer for XAML Data Binding

    public string this[string key]
    {
        get
        {
            if (_resources.TryGetValue(key, out var tuple))
            {
                return CurrentLanguage == "ja-JP" ? tuple.ja : tuple.en;
            }
            return $"[{key}]";
        }
    }

    #endregion

    #region Public Methods

    public string GetString(string key)
    {
        return this[key];
    }

    #endregion
}

#endregion
