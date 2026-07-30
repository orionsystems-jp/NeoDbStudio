// ファイル名     : MainViewModel.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\ViewModels\MainViewModel.cs
// クラス/概要    : MainViewModel (Class)
// 処理概要/目的  : NeoDB Studio メインウィンドウのコア ViewModel。テーマ初期化・プロジェクト未選択時のスタート画面制御・接続履歴・オンデマンドAPI起動を統合
// 使用方法/適用先: MainWindow の DataContext としてバインド
// 依存関係       : NeoDbStudio.Client.Helpers.ApiProcessManager, NeoDbStudio.Client.Models.RecentProjectInfo, OrionSystems.UndoRedoKit
// 注意事項       : 特記事項なし
// 更新履歴       : 2026/07/29 構造完全修正
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Microsoft.Msagl.Drawing;
using Microsoft.Win32;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Client.Views;
using NeoDbStudio.Shared;
using OrionSystems.UndoRedoKit;

using MediaColor = System.Windows.Media.Color;
using MsaglColor = Microsoft.Msagl.Drawing.Color;

namespace NeoDbStudio.Client.ViewModels;

#region MainViewModel Class

/// <summary>
/// NeoDB Studio のメインアプリケーション ViewModel。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    #region Fields & Dependencies

    private readonly ApiProcessManager        _apiManager;
    private readonly IUndoManager             _undoManager;
    private readonly IClipboardManager        _clipboardManager;
    private readonly IEditContext             _editContext;

    private AsyncDuplexStreamingCall<DebugCommand, DebugEvent>? _debugCall;
    private readonly string _historyFilePath;
    private readonly string _queryHistoryFilePath; // クエリ実行履歴の永続化先（%AppData% 配下・暗号化保存）
    private readonly string _sqlSnippetsFilePath; // SQLスニペットライブラリの永続化先（%AppData% 配下・暗号化保存）
    private const int MaxQueryHistoryEntries = 200; // 永続化するクエリ履歴の上限件数（無制限肥大化を防止）

    #endregion

    #region Observable Properties

    [ObservableProperty]
    private bool _hasActiveProject; // 初期値 false

    [ObservableProperty]
    private string _projectName = "No Active Project";

    [ObservableProperty]
    private string? _currentProjectPath;

    [ObservableProperty]
    private string _selectedProvider = string.Empty;

    [ObservableProperty]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    private SshTunnelConfig _sshTunnel = new SshTunnelConfig();

    [ObservableProperty]
    private Graph _graph = new Graph("ER Diagram");

    [ObservableProperty]
    private ObservableCollection<VariableInfo> _debugVariables = new();

    [ObservableProperty]
    private bool _isDebugging;

    [ObservableProperty]
    private int _currentDebugLine = -1;

    [ObservableProperty]
    private string _statusMessage = "Ready - Please open or create a project to start.";

    [ObservableProperty]
    private AppTheme? _selectedTheme;

    public ObservableCollection<string> AvailableLanguages { get; } = new()
    {
        "🇯🇵 日本語 (Japanese)",
        "🇺🇸 English"
    };

    [ObservableProperty]
    private string _selectedLanguage = "🇯🇵 日本語 (Japanese)";

    partial void OnSelectedLanguageChanged(string value)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.OnSelectedLanguageChanged: {value}");
            if (value.Contains("English"))
            {
                LocalizationManager.Instance.CurrentLanguage = "en-US";
                StatusMessage = "Applied Language: English (en-US)";
            }
            else
            {
                LocalizationManager.Instance.CurrentLanguage = "ja-JP";
                StatusMessage = "適用言語: 日本語 (ja-JP)";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OnSelectedLanguageChanged: {ex.Message}");
        }
    }

    [ObservableProperty]
    private QueryTabViewModel? _activeQueryTab;

    #endregion

    #region Collections & Events

    public ObservableCollection<RecentProjectInfo> RecentProjects { get; } = new();
    public ObservableCollection<RecentProjectInfo> FilteredRecentProjects { get; } = new();

    [ObservableProperty]
    private string _historySearchText = string.Empty;

    partial void OnHistorySearchTextChanged(string value)
    {
        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        try
        {
            FilteredRecentProjects.Clear();
            string query = HistorySearchText.Trim().ToLower();

            var matches = string.IsNullOrEmpty(query)
                ? RecentProjects
                : RecentProjects.Where(x => 
                    x.ProjectName.ToLower().Contains(query) || 
                    x.ProviderType.ToLower().Contains(query) || 
                    x.ConnectionString.ToLower().Contains(query));

            foreach (var item in matches)
            {
                FilteredRecentProjects.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.ApplyHistoryFilter: {ex.Message}");
        }
    }
    public ObservableCollection<AppTheme> AvailableThemes { get; } = new();
    public ObservableCollection<QueryTabViewModel> QueryTabs { get; } = new();
    public ObservableCollection<TableDesignerViewModel> TableDesigners { get; } = new();

    /// <summary>直近の LoadSchemaAsync で取得した実テーブルスキーマのテーブル名索引（Table Designer の実データ読込用）。</summary>
    private readonly Dictionary<string, TableSchema> _lastSchemaTablesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 直近の LoadSchemaAsync で取得した完全なスキーマ応答（全テーブル・全ビュー・全FK）。
    /// ER図はこの完全データからスキーマ選択・テーブル絞り込みに応じて都度再構築する
    /// （大規模DB＝実データ規模で1枚のER図では視認不能になる問題への対応）。Excelエクスポートにも使用。
    /// </summary>
    private SchemaResponse? _lastFullSchemaResponse;

    /// <summary>
    /// 直近取得済みスキーマから、指定テーブルの主キー列名一覧を取得します（結果グリッドのインライン編集で使用）。
    /// </summary>
    /// <param name="tableName">[パラメータ] 対象テーブル名を指定します。</param>
    /// <param name="primaryKeyColumns">[出力パラメータ] 主キー列名一覧を返却します（未該当時は空リスト）。</param>
    /// <returns>テーブルがスキーマに存在する場合に true を返却します。</returns>
    public bool TryGetPrimaryKeyColumns(string tableName, out List<string> primaryKeyColumns)
    {
        if (_lastSchemaTablesByName.TryGetValue(tableName, out var table))
        {
            primaryKeyColumns = table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
            return true;
        }
        primaryKeyColumns = new List<string>();
        return false;
    }
    public ObservableCollection<DbObjectNode> DbObjectTree { get; } = new();
    public ObservableCollection<string> QueryHistory { get; } = new();

    /// <summary>登録済みSQLスニペット一覧（%AppData%配下へ暗号化永続化）。</summary>
    public ObservableCollection<SqlSnippet> SqlSnippets { get; } = new();

    /// <summary>SQLスニペット一覧で現在選択中の項目。</summary>
    [ObservableProperty]
    private SqlSnippet? _selectedSqlSnippet;
    public ObservableCollection<string> ExecutionLogs { get; } = new();

    /// <summary>ER図タブのスキーマ（データベース）選択肢一覧。テーブル修飾名の先頭ドット区切り部分から抽出。</summary>
    public ObservableCollection<string> ErDiagramSchemas { get; } = new();

    /// <summary>ER図タブで現在選択中のスキーマ。変更時に自動でテーブル選択肢とER図を再構築する。</summary>
    [ObservableProperty]
    private string? _selectedErDiagramSchema;

    /// <summary>選択中スキーマ内のテーブル絞り込みチェックボックス一覧（ER図表示専用フィルタ）。</summary>
    public ObservableCollection<ErDiagramTableChoice> ErDiagramTableChoices { get; } = new();

    public ObservableCollection<string> Providers { get; } = new()
    {
        "PostgreSQL", "MySQL", "MariaDB", "MSSQL", "Oracle", "SQLite", "IBM DB2", "Firebird",
        "MongoDB", "Redis", "Couchbase", "DynamoDB", "Cosmos DB",
        "ClickHouse", "Snowflake", "DuckDB", "BigQuery", "Cassandra",
        "TimescaleDB", "InfluxDB", "Neo4j", "Milvus",
        "ODBC", "OLE DB"
    };

    public RootDiagramModel RootModel { get; } = new();

    public ModelCollection<TableNodeModel> TableModels
    {
        get
        {
            return RootModel.Tables;
        }
    }

    public event Action<int>? DebugLineChanged;
    public event Action? GraphUpdated;
    public event Action<AppTheme>? ThemeChanged;

    #endregion

    #region ER Diagram Schema Splitting & Excel Export

    /// <summary>
    /// スキーマ修飾名（"db.table"形式）からスキーマ（データベース）部分のみを抽出します。
    /// ドットを含まない場合は既定スキーマとして扱います。
    /// </summary>
    private static string GetErDiagramSchemaGroup(string qualifiedName)
    {
        int dot = qualifiedName.IndexOf('.');
        return dot > 0 ? qualifiedName.Substring(0, dot) : "(既定スキーマ)";
    }

    partial void OnSelectedErDiagramSchemaChanged(string? value)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.OnSelectedErDiagramSchemaChanged: {value}");
            RebuildErDiagramTableChoices();
            RebuildErDiagramGraph();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OnSelectedErDiagramSchemaChanged: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 選択中スキーマ内の全テーブル・ビュー名から ErDiagramTableChoices を再構築します（既定は全選択状態）。
    /// </summary>
    private void RebuildErDiagramTableChoices()
    {
        ErDiagramTableChoices.Clear();

        if (_lastFullSchemaResponse == null || string.IsNullOrEmpty(SelectedErDiagramSchema))
        {
            return;
        }

        var names = _lastFullSchemaResponse.Tables
            .Where(t => GetErDiagramSchemaGroup(t.Name) == SelectedErDiagramSchema)
            .Select(t => t.Name)
            .Concat(_lastFullSchemaResponse.Views
                .Where(v => GetErDiagramSchemaGroup(v.Name) == SelectedErDiagramSchema)
                .Select(v => v.Name))
            .OrderBy(n => n);

        foreach (var name in names)
        {
            ErDiagramTableChoices.Add(new ErDiagramTableChoice(name));
        }
    }

    /// <summary>
    /// 選択中スキーマ・選択中テーブルのみを対象に MSAGL Graph を再構築し、ER図タブへ反映します。
    /// 大規模DB（実データ規模で数百テーブル）でも1回のER図が視認可能なサイズに収まるよう、
    /// 全テーブルを1枚に描画していた従来方式からスキーマ単位＋手動選択の絞り込み方式へ変更。
    /// </summary>
    private void RebuildErDiagramGraph()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.RebuildErDiagramGraph: 開始します");

            if (_lastFullSchemaResponse == null || string.IsNullOrEmpty(SelectedErDiagramSchema))
            {
                return;
            }

            var selectedNames = new HashSet<string>(
                ErDiagramTableChoices.Where(c => c.IsSelected).Select(c => c.Name),
                StringComparer.Ordinal);

            var tables = _lastFullSchemaResponse.Tables
                .Where(t => GetErDiagramSchemaGroup(t.Name) == SelectedErDiagramSchema && selectedNames.Contains(t.Name))
                .ToList();
            var views = _lastFullSchemaResponse.Views
                .Where(v => GetErDiagramSchemaGroup(v.Name) == SelectedErDiagramSchema && selectedNames.Contains(v.Name))
                .ToList();

            var g = new Graph("Reverse Engineered ER Diagram");
            foreach (var t in tables)
            {
                var node = g.AddNode(t.Name);
                var colList = t.Columns.Select(c => $"{c.Name}: {c.DataType}{(c.IsPrimaryKey ? " [PK]" : "")}");
                node.LabelText = $"{t.Name}\n" + string.Join("\n", colList);
                node.Attr.Shape = Shape.Box;
                node.Attr.FillColor = MsaglColor.Azure;
            }
            foreach (var v in views)
            {
                var node = g.AddNode(v.Name);
                var colList = v.Columns.Select(c => $"{c.Name}: {c.DataType}");
                node.LabelText = $"👁 {v.Name}\n" + string.Join("\n", colList);
                node.Attr.Shape = Shape.Box;
                node.Attr.FillColor = MsaglColor.Lavender;
            }
            foreach (var fk in _lastFullSchemaResponse.ForeignKeys)
            {
                if (tables.Any(x => x.Name == fk.PkTable) && tables.Any(x => x.Name == fk.FkTable))
                {
                    var edge = g.AddEdge(fk.PkTable, fk.FkTable);
                    edge.LabelText = fk.ConstraintName;
                    edge.Attr.LineWidth = 2;
                    edge.Attr.Color = MsaglColor.Blue;
                }
            }

            Graph = g;
            GraphUpdated?.Invoke();
            StatusMessage = $"ER Diagram [{SelectedErDiagramSchema}]: {tables.Count} tables, {views.Count} views displayed.";

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.RebuildErDiagramGraph: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.RebuildErDiagramGraph: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectAllErDiagramTables()
    {
        foreach (var choice in ErDiagramTableChoices)
        {
            choice.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllErDiagramTables()
    {
        foreach (var choice in ErDiagramTableChoices)
        {
            choice.IsSelected = false;
        }
    }

    [RelayCommand]
    private void ApplyErDiagramFilter()
    {
        RebuildErDiagramGraph();
    }

    [RelayCommand]
    private void ExportSchemaToExcel()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ExportSchemaToExcel: 開始します");

            if (_lastFullSchemaResponse == null || string.IsNullOrEmpty(SelectedErDiagramSchema))
            {
                MessageBox.Show("エクスポート対象のスキーマがありません。先にDBへ接続してください。", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"Schema_{SelectedErDiagramSchema}.xlsx".Replace(' ', '_')
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            // エクスポート範囲はER図タブで選択中のスキーマ1つに限定する（テーブル単位シート名の一意性を
            // 保証するため。複数テナントDBを1ブックへまとめると同名テーブルのシート名が衝突するため）
            SchemaExcelExporter.Export(_lastFullSchemaResponse, SelectedErDiagramSchema, ProjectName, SelectedProvider, ConnectionString, dlg.FileName);

            StatusMessage = $"Schema exported to Excel: {dlg.FileName}";
            AddLogEntry($"Schema exported to Excel: {dlg.FileName}");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ExportSchemaToExcel: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ExportSchemaToExcel: 例外発生 - {ex.Message}");
            MessageBox.Show($"Excelエクスポートに失敗しました: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Constructors

    public MainViewModel(
        ApiProcessManager apiManager,
        IEditContextFactory editContextFactory)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ctor: 初期化処理を開始します");

            _apiManager         = apiManager ?? throw new ArgumentNullException(nameof(apiManager));
            var factory         = editContextFactory ?? throw new ArgumentNullException(nameof(editContextFactory));

            _editContext        = factory.Create();
            _undoManager       = _editContext.UndoManager;
            _clipboardManager   = _editContext.Clipboard;

            _editContext.Attach(RootModel);

            // 接続履歴はドライブ／設置場所に依存しない %AppData% 配下へ保存する（ハードコード絶対パス回避）
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeoDbStudio");
            Directory.CreateDirectory(appDataDir);
            _historyFilePath = Path.Combine(appDataDir, "recent_history.json");

            if (!File.Exists(_historyFilePath))
            {
                // 初回起動時のみ、実行フォルダから相対的に辿れる同梱の初期接続履歴があれば取り込む
                // （同梱シード自体が暗号化コンテナ形式・旧形式の平文JSONのいずれであっても読込可能）。
                // 取り込み時点で暗号化コンテナ形式へ変換し、ユーザー個別の履歴ファイルは初回から暗号化済みで始まる。
                string seedPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Projects", "recent_history.json"));
                if (File.Exists(seedPath))
                {
                    string seedJson = SecureFileStore.ReadFileContent(seedPath);
                    SecureFileStore.WriteEncryptedFile(_historyFilePath, seedJson);
                }
            }

            LoadRecentProjectsHistory();

            // クエリ実行履歴も接続履歴と同様に %AppData% 配下へ暗号化保存し、次回起動時も復元する
            _queryHistoryFilePath = Path.Combine(appDataDir, "query_history.json");
            LoadQueryHistory();

            // SQLスニペットライブラリも同様に %AppData% 配下へ暗号化保存し、次回起動時も復元する
            _sqlSnippetsFilePath = Path.Combine(appDataDir, "sql_snippets.json");
            LoadSqlSnippets();

            InitThemes();
            SelectedTheme = AvailableThemes[0];

            HasActiveProject = false;
            ProjectName      = "No Active Project";
            SelectedProvider = string.Empty;
            ConnectionString = string.Empty;

            AddLogEntry("NeoDB Studio initialized. Welcome to Start Page.");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region History Management

    private void LoadRecentProjectsHistory()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.LoadRecentProjectsHistory: 保存済み接続履歴ファイルを読込ます");

            RecentProjects.Clear();

            if (File.Exists(_historyFilePath))
            {
                // ファイル全体が DPAPI 暗号化コンテナの場合は復号し、旧形式（平文JSON）はそのまま読込む（後方互換）
                string json = SecureFileStore.ReadFileContent(_historyFilePath);
                var list    = JsonSerializer.Deserialize<List<RecentProjectInfo>>(json);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list.OrderByDescending(x => x.LastAccessedAt).Take(15))
                    {
                        // 旧バージョン（フィールド単位DPAPI暗号化）で保存された値のみ該当し、そのまま通過する（後方互換）
                        item.ConnectionString = CredentialProtector.Unprotect(item.ConnectionString);
                        item.SshPassword      = CredentialProtector.Unprotect(item.SshPassword);
                        item.SshPassphrase    = CredentialProtector.Unprotect(item.SshPassphrase);
                        RecentProjects.Add(item);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.LoadRecentProjectsHistory: 読込完了 ({RecentProjects.Count} 件)");
            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.LoadRecentProjectsHistory: 読込警告 - {ex.Message}");
        }
    }

    private void SaveRecentProjectsHistory()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveRecentProjectsHistory: 履歴ファイルを保存します");

            // ファイル全体を DPAPI 暗号化して保存するため、個々のフィールドは平文のままシリアライズしてよい
            var list = RecentProjects.OrderByDescending(x => x.LastAccessedAt).Take(10).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            SecureFileStore.WriteEncryptedFile(_historyFilePath, json);

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveRecentProjectsHistory: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.SaveRecentProjectsHistory: 保存警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// 永続化済みのクエリ実行履歴（%AppData% 配下・暗号化保存）を読み込み、QueryHistory へ復元します。
    /// </summary>
    private void LoadQueryHistory()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.LoadQueryHistory: 開始します");

            if (File.Exists(_queryHistoryFilePath))
            {
                string json = SecureFileStore.ReadFileContent(_queryHistoryFilePath);
                var list    = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    foreach (var entry in list)
                    {
                        QueryHistory.Add(entry);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.LoadQueryHistory: 読込完了 ({QueryHistory.Count} 件)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.LoadQueryHistory: 読込警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// 現在の QueryHistory を暗号化コンテナ形式で永続化します。
    /// </summary>
    private void SaveQueryHistory()
    {
        try
        {
            string json = JsonSerializer.Serialize(QueryHistory.ToList(), new JsonSerializerOptions { WriteIndented = true });
            SecureFileStore.WriteEncryptedFile(_queryHistoryFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.SaveQueryHistory: 保存警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// クエリ実行履歴へ1件追加し、上限件数（<see cref="MaxQueryHistoryEntries"/>）を超えた古い履歴を切り詰めたうえで永続化します。
    /// </summary>
    /// <param name="message">[パラメータ] 追加する履歴メッセージを指定します。</param>
    private void AddQueryHistoryEntry(string message)
    {
        try
        {
            QueryHistory.Insert(0, message);
            while (QueryHistory.Count > MaxQueryHistoryEntries)
            {
                QueryHistory.RemoveAt(QueryHistory.Count - 1);
            }
            SaveQueryHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.AddQueryHistoryEntry: 保存警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// クエリ実行履歴を全件削除し、永続化ファイルも更新します。
    /// </summary>
    [RelayCommand]
    private void ClearQueryHistory()
    {
        try
        {
            QueryHistory.Clear();
            SaveQueryHistory();
            StatusMessage = "Query History cleared.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.ClearQueryHistory: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 永続化済みのSQLスニペット一覧（%AppData% 配下・暗号化保存）を読み込み、SqlSnippets へ復元します。
    /// </summary>
    private void LoadSqlSnippets()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.LoadSqlSnippets: 開始します");

            if (File.Exists(_sqlSnippetsFilePath))
            {
                string json = SecureFileStore.ReadFileContent(_sqlSnippetsFilePath);
                var list = JsonSerializer.Deserialize<List<SqlSnippet>>(json);
                if (list != null)
                {
                    foreach (var entry in list.OrderBy(s => s.Name))
                    {
                        SqlSnippets.Add(entry);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.LoadSqlSnippets: 読込完了 ({SqlSnippets.Count} 件)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.LoadSqlSnippets: 読込警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// 現在の SqlSnippets を暗号化コンテナ形式で永続化します。
    /// </summary>
    private void SaveSqlSnippets()
    {
        try
        {
            string json = JsonSerializer.Serialize(SqlSnippets.ToList(), new JsonSerializerOptions { WriteIndented = true });
            SecureFileStore.WriteEncryptedFile(_sqlSnippetsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] MainViewModel.SaveSqlSnippets: 保存警告 - {ex.Message}");
        }
    }

    /// <summary>
    /// 現在アクティブなクエリタブのSQL全文を、名前を付けてスニペットライブラリへ保存します。
    /// </summary>
    [RelayCommand]
    private void SaveCurrentQueryAsSnippet()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveCurrentQueryAsSnippet: 開始します");

            if (ActiveQueryTab == null || string.IsNullOrWhiteSpace(ActiveQueryTab.SqlScript))
            {
                MessageBox.Show("保存するSQLがありません。クエリタブにSQLを入力してください。", "Save as Snippet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? name = Views.InputBoxDialog.Show(Application.Current?.MainWindow, "SQLスニペットとして保存", "スニペット名を入力してください:");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var existing = SqlSnippets.FirstOrDefault(s => s.Name == name);
            if (existing != null)
            {
                SqlSnippets.Remove(existing);
            }

            var snippet = new SqlSnippet { Name = name, Sql = ActiveQueryTab.SqlScript, CreatedAt = DateTime.Now };
            SqlSnippets.Add(snippet);
            SaveSqlSnippets();

            StatusMessage = $"SQL snippet saved: {name}";
            AddLogEntry($"SQL snippet saved: {name}");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveCurrentQueryAsSnippet: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.SaveCurrentQueryAsSnippet: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 選択中のSQLスニペットを、現在アクティブなクエリタブへ挿入します（既存内容がある場合は末尾に追記）。
    /// </summary>
    [RelayCommand]
    private void InsertSelectedSnippet()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InsertSelectedSnippet: 開始します");

            if (SelectedSqlSnippet == null)
            {
                MessageBox.Show("挿入するスニペットを選択してください。", "Insert Snippet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ActiveQueryTab == null)
            {
                MessageBox.Show("挿入先のクエリタブがありません。先にクエリタブを開いてください。", "Insert Snippet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActiveQueryTab.SqlScript = string.IsNullOrWhiteSpace(ActiveQueryTab.SqlScript)
                ? SelectedSqlSnippet.Sql
                : ActiveQueryTab.SqlScript.TrimEnd() + "\n\n" + SelectedSqlSnippet.Sql;

            StatusMessage = $"SQL snippet inserted: {SelectedSqlSnippet.Name}";
            AddLogEntry($"SQL snippet inserted: {SelectedSqlSnippet.Name}");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InsertSelectedSnippet: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.InsertSelectedSnippet: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 選択中のSQLスニペットを確認ダイアログの上で削除します。
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedSnippet()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.DeleteSelectedSnippet: 開始します");

            if (SelectedSqlSnippet == null)
            {
                return;
            }

            var result = MessageBox.Show($"スニペット「{SelectedSqlSnippet.Name}」を削除しますか？", "Delete Snippet", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            string deletedName = SelectedSqlSnippet.Name;
            SqlSnippets.Remove(SelectedSqlSnippet);
            SelectedSqlSnippet = null;
            SaveSqlSnippets();

            StatusMessage = $"SQL snippet deleted: {deletedName}";
            AddLogEntry($"SQL snippet deleted: {deletedName}");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.DeleteSelectedSnippet: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.DeleteSelectedSnippet: 例外発生 - {ex.Message}");
        }
    }

    public void AddRecentProject(string name, string provider, string connStr, string filePath = "", SshTunnelConfig? sshTunnel = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.AddRecentProject: 履歴を追加します ({name})");

            var existing = RecentProjects.FirstOrDefault(x => x.ProjectName == name || (!string.IsNullOrEmpty(filePath) && x.FilePath == filePath));
            if (existing != null)
            {
                RecentProjects.Remove(existing);
            }

            var info = new RecentProjectInfo(name, provider, connStr, filePath);
            if (sshTunnel != null)
            {
                info.SshEnabled        = sshTunnel.Enabled;
                info.SshHost           = sshTunnel.Host;
                info.SshPort           = sshTunnel.Port;
                info.SshUsername       = sshTunnel.Username;
                info.SshAuthType       = sshTunnel.AuthType;
                info.SshPassword       = sshTunnel.Password;
                info.SshPrivateKeyPath = sshTunnel.PrivateKeyPath;
                info.SshPassphrase     = sshTunnel.Passphrase;
                info.SshRemoteHost     = sshTunnel.RemoteHost;
                info.SshRemotePort     = sshTunnel.RemotePort;
            }
            RecentProjects.Insert(0, info);

            while (RecentProjects.Count > 10)
            {
                RecentProjects.RemoveAt(RecentProjects.Count - 1);
            }

            SaveRecentProjectsHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.AddRecentProject: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>RecentProjectInfo の SSH 各プロパティから SshTunnelConfig メッセージを構築します。</summary>
    private static SshTunnelConfig BuildSshTunnelConfig(RecentProjectInfo info) => new SshTunnelConfig
    {
        Enabled        = info.SshEnabled,
        Host           = info.SshHost,
        Port           = info.SshPort,
        Username       = info.SshUsername,
        AuthType       = info.SshAuthType,
        Password       = info.SshPassword,
        PrivateKeyPath = info.SshPrivateKeyPath,
        Passphrase     = info.SshPassphrase,
        RemoteHost     = info.SshRemoteHost,
        RemotePort     = info.SshRemotePort
    };

    /// <summary>NeoDbProjectFile の SSH 各プロパティから SshTunnelConfig メッセージを構築します。</summary>
    private static SshTunnelConfig BuildSshTunnelConfig(NeoDbProjectFile proj) => new SshTunnelConfig
    {
        Enabled        = proj.SshEnabled,
        Host           = proj.SshHost,
        Port           = proj.SshPort,
        Username       = proj.SshUsername,
        AuthType       = proj.SshAuthType,
        Password       = proj.SshPassword,
        PrivateKeyPath = proj.SshPrivateKeyPath,
        Passphrase     = proj.SshPassphrase,
        RemoteHost     = proj.SshRemoteHost,
        RemotePort     = proj.SshRemotePort
    };

    /// <summary>ConnectionWizardDialog の公開SSHプロパティから SshTunnelConfig メッセージを構築します。</summary>
    private static SshTunnelConfig BuildSshTunnelConfig(ConnectionWizardDialog dlg) => new SshTunnelConfig
    {
        Enabled        = dlg.SshEnabled,
        Host           = dlg.SshHost,
        Port           = dlg.SshPort,
        Username       = dlg.SshUsername,
        AuthType       = dlg.SshAuthType,
        Password       = dlg.SshPassword,
        PrivateKeyPath = dlg.SshPrivateKeyPath,
        Passphrase     = dlg.SshPassphrase,
        RemoteHost     = dlg.SshRemoteHost,
        RemotePort     = dlg.SshRemotePort
    };

    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentProjectInfo info)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.OpenRecentProjectAsync: 履歴から接続を開きます ({info?.ProjectName})");

            if (info == null) return;

            // 接続情報は経路によらず必ず設定する。
            // （プロジェクトファイル付き履歴で設定を省略すると、空の接続文字列のまま
            //   LoadSchemaAsync が実行され、オブジェクト情報の取得が必ず失敗するため）
            ProjectName        = info.ProjectName;
            SelectedProvider   = info.ProviderType;
            ConnectionString   = info.ConnectionString;
            SshTunnel          = BuildSshTunnelConfig(info);
            CurrentProjectPath = null;

            if (!string.IsNullOrEmpty(info.FilePath) && File.Exists(info.FilePath))
            {
                // ファイル全体が DPAPI 暗号化コンテナの場合は復号し、旧形式（平文JSON）はそのまま読込む（後方互換）
                string json = SecureFileStore.ReadFileContent(info.FilePath);
                var proj    = JsonSerializer.Deserialize<NeoDbProjectFile>(json);
                if (proj != null)
                {
                    // 旧バージョン（フィールド単位DPAPI暗号化）で保存された値のみ該当し、そのまま通過する（後方互換）
                    proj.ConnectionString = CredentialProtector.Unprotect(proj.ConnectionString);
                    proj.SshPassword      = CredentialProtector.Unprotect(proj.SshPassword);
                    proj.SshPassphrase    = CredentialProtector.Unprotect(proj.SshPassphrase);

                    // プロジェクトファイル側に値がある項目のみ履歴の値より優先して採用する
                    if (!string.IsNullOrWhiteSpace(proj.ProjectName))
                    {
                        ProjectName = proj.ProjectName;
                    }

                    if (!string.IsNullOrWhiteSpace(proj.ProviderType))
                    {
                        SelectedProvider = proj.ProviderType;
                    }

                    if (!string.IsNullOrWhiteSpace(proj.ConnectionString))
                    {
                        ConnectionString = proj.ConnectionString;
                        SshTunnel        = BuildSshTunnelConfig(proj);
                    }

                    CurrentProjectPath = info.FilePath; // 上書き保存先を開いたファイルへ正しく引き継ぐ

                    if (!string.IsNullOrWhiteSpace(proj.SqlScript))
                    {
                        AddQueryTab("Query.sql", proj.SqlScript);
                    }
                }
            }

            HasActiveProject    = true;
            info.LastAccessedAt = DateTime.Now;
            AddRecentProject(info.ProjectName, info.ProviderType, info.ConnectionString, info.FilePath, SshTunnel);

            StatusMessage = $"Connecting to {info.ProviderType} via History: {info.ProjectName}...";
            AddLogEntry($"Opened project connection from History: [{info.ProviderType}] {info.ProjectName}");

            await LoadSchemaAsync();

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenRecentProjectAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            HasActiveProject = true; // 画面切替は維持
            StatusMessage    = $"Connected with Warning: {ex.Message}";
            AddLogEntry($"Connection Warning: {ex.Message}");
            MessageBox.Show($"DBMS Connection warning: {ex.Message}\n\nProject was opened in offline/mock mode.", "Connection Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenRecentProjectAsync: 例外安全処理 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ClearRecentProjects: 履歴をクリアします");

            RecentProjects.Clear();
            SaveRecentProjectsHistory();
            StatusMessage = "Recent Projects History cleared.";
            AddLogEntry("Recent Projects History cleared.");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ClearRecentProjects: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ClearRecentProjects: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Theme Initialization

    private void InitThemes()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InitThemes: テーマ一覧を初期構築します");

            AvailableThemes.Clear();

            AvailableThemes.Add(new AppTheme
            {
                Name                      = "Dark (VS Code)",
                Mode                      = ThemeMode.Dark,
                Icon                      = "🌙",
                BackgroundColor           = MediaColor.FromRgb(30, 30, 30),
                ForegroundColor           = MediaColor.FromRgb(240, 240, 240),
                SecondaryForegroundColor  = MediaColor.FromRgb(170, 170, 170),
                CardBackgroundColor       = MediaColor.FromRgb(37, 37, 38),
                HeaderBackgroundColor     = MediaColor.FromRgb(45, 45, 48),
                AccentColor               = MediaColor.FromRgb(30, 144, 255),
                BorderColor               = MediaColor.FromRgb(63, 63, 70),
                EditorBackgroundColor     = MediaColor.FromRgb(30, 30, 30),
                EditorForegroundColor     = MediaColor.FromRgb(220, 220, 220),
                EditorLineNumberColor     = MediaColor.FromRgb(100, 100, 100)
            });

            AvailableThemes.Add(new AppTheme
            {
                Name                      = "Light (Modern)",
                Mode                      = ThemeMode.Light,
                Icon                      = "☀️",
                BackgroundColor           = MediaColor.FromRgb(243, 244, 246),
                ForegroundColor           = MediaColor.FromRgb(17, 24, 39),
                SecondaryForegroundColor  = MediaColor.FromRgb(75, 85, 99),
                CardBackgroundColor       = MediaColor.FromRgb(255, 255, 255),
                HeaderBackgroundColor     = MediaColor.FromRgb(229, 231, 235),
                AccentColor               = MediaColor.FromRgb(37, 99, 235),
                BorderColor               = MediaColor.FromRgb(209, 213, 219),
                EditorBackgroundColor     = MediaColor.FromRgb(255, 255, 255),
                EditorForegroundColor     = MediaColor.FromRgb(17, 24, 39),
                EditorLineNumberColor     = MediaColor.FromRgb(156, 163, 175)
            });

            AvailableThemes.Add(new AppTheme
            {
                Name                      = "Midnight Blue",
                Mode                      = ThemeMode.MidnightBlue,
                Icon                      = "🌌",
                BackgroundColor           = MediaColor.FromRgb(15, 23, 42),
                ForegroundColor           = MediaColor.FromRgb(248, 250, 252),
                SecondaryForegroundColor  = MediaColor.FromRgb(148, 163, 184),
                CardBackgroundColor       = MediaColor.FromRgb(30, 41, 59),
                HeaderBackgroundColor     = MediaColor.FromRgb(51, 65, 85),
                AccentColor               = MediaColor.FromRgb(6, 182, 212),
                BorderColor               = MediaColor.FromRgb(71, 85, 105),
                EditorBackgroundColor     = MediaColor.FromRgb(15, 23, 42),
                EditorForegroundColor     = MediaColor.FromRgb(248, 250, 252),
                EditorLineNumberColor     = MediaColor.FromRgb(100, 116, 139)
            });

            AvailableThemes.Add(new AppTheme
            {
                Name                      = "Nord Dark",
                Mode                      = ThemeMode.Nord,
                Icon                      = "🌲",
                BackgroundColor           = MediaColor.FromRgb(46, 52, 64),
                ForegroundColor           = MediaColor.FromRgb(236, 239, 244),
                SecondaryForegroundColor  = MediaColor.FromRgb(216, 222, 233),
                CardBackgroundColor       = MediaColor.FromRgb(59, 66, 82),
                HeaderBackgroundColor     = MediaColor.FromRgb(67, 76, 94),
                AccentColor               = MediaColor.FromRgb(136, 192, 208),
                BorderColor               = MediaColor.FromRgb(76, 86, 106),
                EditorBackgroundColor     = MediaColor.FromRgb(46, 52, 64),
                EditorForegroundColor     = MediaColor.FromRgb(236, 239, 244),
                EditorLineNumberColor     = MediaColor.FromRgb(94, 129, 172)
            });

            AvailableThemes.Add(new AppTheme
            {
                Name                      = "Cyberpunk Purple",
                Mode                      = ThemeMode.Cyberpunk,
                Icon                      = "🔮",
                BackgroundColor           = MediaColor.FromRgb(26, 27, 38),
                ForegroundColor           = MediaColor.FromRgb(192, 202, 245),
                SecondaryForegroundColor  = MediaColor.FromRgb(154, 165, 206),
                CardBackgroundColor       = MediaColor.FromRgb(36, 40, 59),
                HeaderBackgroundColor     = MediaColor.FromRgb(41, 46, 66),
                AccentColor               = MediaColor.FromRgb(247, 118, 142),
                BorderColor               = MediaColor.FromRgb(65, 72, 104),
                EditorBackgroundColor     = MediaColor.FromRgb(26, 27, 38),
                EditorForegroundColor     = MediaColor.FromRgb(192, 202, 245),
                EditorLineNumberColor     = MediaColor.FromRgb(86, 95, 137)
            });

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InitThemes: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.InitThemes: 例外発生 - {ex.Message}");
            throw;
        }
    }

    partial void OnSelectedThemeChanged(AppTheme? value)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.OnSelectedThemeChanged: {value?.Name}");
            if (value != null)
            {
                ThemeChanged?.Invoke(value);
                StatusMessage = $"Applied Color Theme: {value.Name}";
                AddLogEntry($"Applied Color Theme: {value.Name}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OnSelectedThemeChanged: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Classic Menu Commands

    [RelayCommand]
    private void ExitApp()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ExitApp: アプリ終了を実行します");
            ApiProcessManager.KillExistingApiProcesses();
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ExitApp: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ShowAbout: アバウトダイアログを表示します");
            MessageBox.Show(
                "⚡ NeoDB Studio v1.0 - Advanced Database Engineering Studio\n\n" +
                "• License: MIT License (Free & Open Source Software)\n" +
                "• SSMS-Style Multi-Query Editors\n" +
                "• gRPC Streamed Engine (HTTP/2)\n" +
                "• Real-time DBMS Object Explorer\n" +
                "• MSAGL Reverse Engineering Modeler\n" +
                "• Multi-Color Theme Engine\n\n" +
                "Copyright (c) 2026 オリオンシステムズ (Orion Systems). All Rights Reserved.",
                "About NeoDB Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ShowAbout: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void ShowLicenses()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ShowLicenses: ライセンス表示ダイアログを開きます");
            var dlg = new LicenseViewerDialog
            {
                Owner = Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ShowLicenses: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Query Tab Management

    public QueryTabViewModel AddQueryTab(string? title = null, string? initialSql = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.AddQueryTab: 開始します");

            // 不要な初期未編集デフォルトタブ (Project_Query.sql 等) が単一で存在する場合は自動除去
            if (QueryTabs.Count == 1)
            {
                var firstTab = QueryTabs[0];
                if ((firstTab.Title.Contains("Project_Query") || firstTab.Title.Contains("Query 1")) && string.IsNullOrWhiteSpace(firstTab.SqlScript))
                {
                    QueryTabs.RemoveAt(0);
                }
            }

            int count       = QueryTabs.Count + 1;
            string tabTitle = title ?? $"Query {count}.sql";
            string sql      = initialSql ?? string.Empty; // 新規クエリ作成時は完全に「空白」でオープン

            var tab = new QueryTabViewModel(_apiManager, tabTitle, SelectedProvider, ConnectionString, sql)
            {
                SshTunnel = SshTunnel
            };

            tab.QueryExecuted += (msg) =>
            {
                AddQueryHistoryEntry(msg);
                AddLogEntry(msg);
            };

            QueryTabs.Add(tab);
            ActiveQueryTab = tab;

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.AddQueryTab: 正常終了しました");
            return tab;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.AddQueryTab: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 現在アクティブな接続とは異なる、明示的に指定した接続（プロバイダー・接続文字列・SSHトンネル）で
    /// 新規クエリタブを開きます。スキーマ比較の同期スクリプトのように「実行先が現在の接続と異なる」
    /// 場合に使用します（現在の接続を使う通常の <see cref="AddQueryTab"/> とは区別）。
    /// </summary>
    /// <param name="title">[パラメータ] タブタイトルを指定します。</param>
    /// <param name="initialSql">[パラメータ] 初期SQLスクリプトを指定します。</param>
    /// <param name="provider">[パラメータ] 実行先のDBMSプロバイダー種別を指定します。</param>
    /// <param name="connectionString">[パラメータ] 実行先の接続文字列を指定します。</param>
    /// <param name="sshTunnel">[パラメータ] 実行先のSSHトンネル設定を指定します（未使用時はnull可）。</param>
    /// <returns>作成された QueryTabViewModel を返却します。</returns>
    public QueryTabViewModel AddQueryTabForConnection(string title, string initialSql, string provider, string connectionString, SshTunnelConfig? sshTunnel)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.AddQueryTabForConnection: 開始します");

            var tab = new QueryTabViewModel(_apiManager, title, provider, connectionString, initialSql)
            {
                SshTunnel = sshTunnel ?? new SshTunnelConfig()
            };

            tab.QueryExecuted += (msg) =>
            {
                AddQueryHistoryEntry(msg);
                AddLogEntry(msg);
            };

            QueryTabs.Add(tab);
            ActiveQueryTab = tab;

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.AddQueryTabForConnection: 正常終了しました");
            return tab;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.AddQueryTabForConnection: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void NewQueryTab()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.NewQueryTab: 開始します");
            AddQueryTab();
            StatusMessage = "New SQL Query tab added.";
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.NewQueryTab: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.NewQueryTab: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void CloseQueryTab(QueryTabViewModel tab)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.CloseQueryTab: 開始します");

            if (tab != null && QueryTabs.Contains(tab))
            {
                QueryTabs.Remove(tab);
                if (QueryTabs.Count > 0)
                {
                    ActiveQueryTab = QueryTabs.Last();
                }
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.CloseQueryTab: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.CloseQueryTab: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #region SSMS Context Menu Scripting Commands

    [RelayCommand]
    private void ScriptSelect(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            string quotedTableName = SqlIdentifierQuoter.Quote(SelectedProvider, tableName);
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptSelect: {tableName}");
            AddQueryTab($"Select_{tableName}.sql", $"-- SSMS Script Table as SELECT\nSELECT * FROM {quotedTableName} LIMIT 1000;");
            StatusMessage = $"SELECT script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptSelect: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void ScriptInsert(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            string quotedTableName = SqlIdentifierQuoter.Quote(SelectedProvider, tableName);
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptInsert: {tableName}");

            string cols = node.Children.Count > 0
                ? string.Join(", ", node.Children.Select(c => c.Name.Split(' ')[0]))
                : "col1, col2, created_at";

            string vals = node.Children.Count > 0
                ? string.Join(", ", node.Children.Select(c => "'value'"))
                : "1, 'value', NOW()";

            string sql = $"-- SSMS Script Table as INSERT To\nINSERT INTO {quotedTableName} (\n    {cols}\n)\nVALUES (\n    {vals}\n);";
            AddQueryTab($"Insert_{tableName}.sql", sql);
            StatusMessage = $"INSERT script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptInsert: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void ScriptUpdate(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            string quotedTableName = SqlIdentifierQuoter.Quote(SelectedProvider, tableName);
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptUpdate: {tableName}");

            string setClause = node.Children.Count > 0
                ? string.Join(",\n    ", node.Children.Where(c => !c.Name.Contains("(integer)") && !c.Name.Contains("id")).Select(c => $"{c.Name.Split(' ')[0]} = 'new_value'"))
                : "column1 = 'new_value'";

            string pkCol = node.Children.FirstOrDefault(c => c.Icon == "🔑" || c.Name.StartsWith("id"))?.Name.Split(' ')[0] ?? "id";

            string sql = $"-- SSMS Script Table as UPDATE To\nUPDATE {quotedTableName}\nSET\n    {setClause}\nWHERE {pkCol} = 1;";
            AddQueryTab($"Update_{tableName}.sql", sql);
            StatusMessage = $"UPDATE script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptUpdate: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void ScriptDelete(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            string quotedTableName = SqlIdentifierQuoter.Quote(SelectedProvider, tableName);
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptDelete: {tableName}");

            string pkCol = node.Children.FirstOrDefault(c => c.Icon == "🔑" || c.Name.StartsWith("id"))?.Name.Split(' ')[0] ?? "id";

            string sql = $"-- SSMS Script Table as DELETE To\nDELETE FROM {quotedTableName}\nWHERE {pkCol} = 1;";
            AddQueryTab($"Delete_{tableName}.sql", sql);
            StatusMessage = $"DELETE script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptDelete: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void ScriptCreate(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            string quotedTableName = SqlIdentifierQuoter.Quote(SelectedProvider, tableName);
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptCreate: {tableName}");

            string colDefs = node.Children.Count > 0
                ? string.Join(",\n    ", node.Children.Select(FormatColumnDefinitionForCreate))
                : "id INT PRIMARY KEY,\n    name VARCHAR(255)";

            string sql = $"-- SSMS Script Table as CREATE To\nCREATE TABLE {quotedTableName} (\n    {colDefs}\n);";
            AddQueryTab($"Create_{tableName}.sql", sql);
            StatusMessage = $"CREATE TABLE script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptCreate: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// オブジェクトツリーのカラムノード表示名（"colname (datatype)" 形式）から、
    /// CREATE TABLE スクリプト用のカラム定義（実データ型・主キー修飾付き）を組み立てます。
    /// </summary>
    /// <param name="column">[パラメータ] カラムを表す DbObjectNode を指定します。</param>
    /// <returns>"colname DATATYPE[ PRIMARY KEY]" 形式のカラム定義文字列を返却します。</returns>
    private static string FormatColumnDefinitionForCreate(DbObjectNode column)
    {
        string label = column.Name;
        int openParen  = label.IndexOf('(');
        int closeParen = label.LastIndexOf(')');

        string name;
        string dataType;
        if (openParen > 0 && closeParen > openParen) // "colname (datatype)" 形式から実データ型を抽出
        {
            name     = label.Substring(0, openParen).Trim();
            dataType = label.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        }
        else // 想定外の書式は従来どおりの安全側フォールバック
        {
            name     = label.Split(' ')[0];
            dataType = "VARCHAR(255)";
        }

        string pkSuffix = column.Icon == "🔑" ? " PRIMARY KEY" : ""; // 🔑 アイコンは主キー列を示す（既存の ScriptUpdate/ScriptDelete と同一基準）
        return $"{name} {dataType}{pkSuffix}";
    }

    [RelayCommand]
    private void ScriptDrop(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.ScriptDrop: {tableName}");

            string sql = $"-- SSMS Script Table as DROP To\nDROP TABLE IF EXISTS {tableName};";
            AddQueryTab($"Drop_{tableName}.sql", sql);
            StatusMessage = $"DROP TABLE script generated for {tableName}.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ScriptDrop: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void DesignTableNode(DbObjectNode? node)
    {
        try
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Name)) return;
            string tableName = node.Name;
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.DesignTableNode: {tableName}");
            OpenTableDesigner(tableName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.DesignTableNode: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenTableDesigner(string? tableName = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenTableDesigner: 開始します");

            string name    = tableName ?? "NewTable";
            var designer   = new TableDesignerViewModel(name);
            designer.LogNotification += AddLogEntry;

            // 実テーブルであれば直近取得済みのスキーマから実カラム構成を読み込む（未該当時は既定のサンプル2列のまま）
            if (!string.IsNullOrEmpty(tableName) && _lastSchemaTablesByName.TryGetValue(tableName, out var realSchema))
            {
                designer.LoadFromSchema(realSchema);
            }

            TableDesigners.Add(designer);

            var dlg = new TableDesignerDialog(designer, SelectedProvider, (title, sql) => AddQueryTab(title, sql))
            {
                Owner = Application.Current?.MainWindow
            };
            dlg.Show(); // 非モーダル：クエリタブと並行して参照しながら編集できるようにする

            StatusMessage = designer.IsLoadedFromRealSchema
                ? $"Table Structure Designer opened for {name} ({designer.Columns.Count} columns loaded from live schema)."
                : $"Table Structure Designer opened for {name}.";

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenTableDesigner: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenTableDesigner: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 現在の接続（ソース）と任意のターゲット接続との間でスキーマ比較を行うダイアログを開きます。
    /// </summary>
    [RelayCommand]
    private void OpenSchemaDiff()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenSchemaDiff: 開始します");

            var dlg = new NeoDbStudio.Client.Views.SchemaDiffDialog(
                _apiManager, SelectedProvider, ConnectionString, SshTunnel,
                (title, sql, provider, connectionString) => AddQueryTabForConnection(title, sql, provider, connectionString, null))
            {
                Owner = Application.Current?.MainWindow
            };
            dlg.Show(); // 非モーダル：比較結果を見ながら他のクエリタブも並行操作できるようにする

            StatusMessage = "Schema Comparison dialog opened.";

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenSchemaDiff: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenSchemaDiff: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    public void AddLogEntry(string message)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.AddLogEntry: {message}");
            ExecutionLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.AddLogEntry: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region MSAGL ER Diagram Graph Rendering

    private void InitSampleGraphModels()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InitSampleGraphModels: 開始します");
            ClearTableModels();
            RebuildMsaglGraph();
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.InitSampleGraphModels: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.InitSampleGraphModels: 例外発生 - {ex.Message}");
            throw;
        }
    }

    private void ClearTableModels()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ClearTableModels: 開始します");
            while (TableModels.Count > 0)
            {
                TableModels.RemoveAt(0);
            }
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ClearTableModels: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ClearTableModels: 例外発生 - {ex.Message}");
            throw;
        }
    }

    private void RebuildMsaglGraph()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.RebuildMsaglGraph: 開始します");

            var g = new Graph("ER Diagram");
            foreach (var t in TableModels)
            {
                var node = g.AddNode(t.TableName);
                var colNames = t.Columns.Select(c => c.Name);
                node.LabelText = $"{t.TableName}\n" + string.Join("\n", colNames);
                node.Attr.Shape = Shape.Box;
                node.Attr.FillColor = t.IsView ? MsaglColor.Lavender : MsaglColor.Azure; // ビュー由来ノードはテーブルと色分けして区別する
            }

            Graph = g;
            GraphUpdated?.Invoke();
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.RebuildMsaglGraph: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.RebuildMsaglGraph: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Project File Operations (.neodb & .sql)

    [RelayCommand]
    private void NewProject()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.NewProject: 開始します");

            var dlg = new ConnectionWizardDialog(_apiManager, string.IsNullOrEmpty(SelectedProvider) ? "PostgreSQL" : SelectedProvider, ConnectionString, SshTunnel)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedProvider   = dlg.SelectedProvider;
                ConnectionString   = dlg.ConnectionString;
                SshTunnel          = BuildSshTunnelConfig(dlg);
                ProjectName        = $"{SelectedProvider} Project";
                CurrentProjectPath = null;
                HasActiveProject   = true;

                if (QueryTabs.Count == 0)
                {
                    AddQueryTab($"{SelectedProvider}_Query.sql", $"-- Query for {SelectedProvider}\nSELECT * FROM Users;");
                }

                AddRecentProject(ProjectName, SelectedProvider, ConnectionString, "", SshTunnel);

                InitSampleGraphModels();
                _ = LoadSchemaAsync();

                StatusMessage = $"New Project created for {SelectedProvider}.";
                AddLogEntry($"Created new project for {SelectedProvider}.");
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.NewProject: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.NewProject: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void OpenSqlFile()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenSqlFile: 開始します");

            var dlg = new OpenFileDialog
            {
                Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                Title  = "Open SQL Script File"
            };

            if (dlg.ShowDialog() == true)
            {
                string sql = File.ReadAllText(dlg.FileName);
                AddQueryTab(Path.GetFileName(dlg.FileName), sql);
                StatusMessage = $"SQL Script loaded: {Path.GetFileName(dlg.FileName)}";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenSqlFile: 正常終了しました");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open SQL file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenSqlFile: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenProject: 開始します");

            var dlg = new OpenFileDialog
            {
                Filter = "NeoDB Project Files (*.neodb)|*.neodb|All Files (*.*)|*.*",
                Title  = "Open NeoDB Studio Project"
            };

            if (dlg.ShowDialog() == true)
            {
                // ファイル全体が DPAPI 暗号化コンテナの場合は復号し、旧形式（平文JSON）はそのまま読込む（後方互換）
                string json = SecureFileStore.ReadFileContent(dlg.FileName);
                var proj    = JsonSerializer.Deserialize<NeoDbProjectFile>(json);
                if (proj != null)
                {
                    // 旧バージョン（フィールド単位DPAPI暗号化）で保存された値のみ該当し、そのまま通過する（後方互換）
                    proj.ConnectionString = CredentialProtector.Unprotect(proj.ConnectionString);
                    proj.SshPassword      = CredentialProtector.Unprotect(proj.SshPassword);
                    proj.SshPassphrase    = CredentialProtector.Unprotect(proj.SshPassphrase);

                    ProjectName        = proj.ProjectName;
                    SelectedProvider   = proj.ProviderType;
                    ConnectionString   = proj.ConnectionString;
                    SshTunnel          = BuildSshTunnelConfig(proj);
                    CurrentProjectPath = dlg.FileName;
                    HasActiveProject   = true;

                    AddRecentProject(proj.ProjectName, proj.ProviderType, proj.ConnectionString, dlg.FileName, SshTunnel);

                    if (!string.IsNullOrWhiteSpace(proj.SqlScript))
                    {
                        AddQueryTab("Query.sql", proj.SqlScript);
                    }

                    using (_editContext.UndoManager.BeginTransaction("Open Project Nodes"))
                    {
                        ClearTableModels();
                        foreach (var n in proj.TableNodes)
                        {
                            var model = new TableNodeModel(n.TableName)
                            {
                                X            = n.X,
                                Y            = n.Y,
                                FillColorHex = n.FillColorHex,
                                IsView       = n.IsView
                            };
                            foreach (var c in proj.TableNodes.First(x => x.TableName == n.TableName).Columns)
                            {
                                model.Columns.Add(new ColumnModel(c));
                            }
                            TableModels.Add(model);
                        }
                    }

                    RebuildMsaglGraph();
                    StatusMessage = $"Project loaded: {Path.GetFileName(dlg.FileName)}";
                    AddLogEntry($"Loaded project from {dlg.FileName}");

                    _ = LoadSchemaAsync();
                }
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenProject: 正常終了しました");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open project file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenProject: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveProject()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveProject: 開始します");

            if (string.IsNullOrEmpty(CurrentProjectPath))
            {
                SaveProjectAs();
            }
            else
            {
                SaveProjectToFile(CurrentProjectPath);
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveProject: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.SaveProject: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveProjectAs: 開始します");

            var dlg = new SaveFileDialog
            {
                Filter     = "NeoDB Project Files (*.neodb)|*.neodb|All Files (*.*)|*.*",
                DefaultExt = ".neodb",
                FileName   = $"{ProjectName}.neodb",
                Title      = "Save NeoDB Studio Project"
            };

            if (dlg.ShowDialog() == true)
            {
                CurrentProjectPath = dlg.FileName;
                ProjectName        = Path.GetFileNameWithoutExtension(dlg.FileName);
                SaveProjectToFile(dlg.FileName);
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveProjectAs: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.SaveProjectAs: 例外発生 - {ex.Message}");
            throw;
        }
    }

    private void SaveProjectToFile(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] MainViewModel.SaveProjectToFile: 開始します (filePath={filePath})");

            // ファイル全体を DPAPI 暗号化して保存するため、個々のフィールドは平文のままシリアライズしてよい
            var proj = new NeoDbProjectFile
            {
                ProjectName       = ProjectName,
                ProviderType      = SelectedProvider,
                ConnectionString  = ConnectionString,
                SshEnabled        = SshTunnel.Enabled,
                SshHost           = SshTunnel.Host,
                SshPort           = SshTunnel.Port,
                SshUsername       = SshTunnel.Username,
                SshAuthType       = SshTunnel.AuthType,
                SshPassword       = SshTunnel.Password,
                SshPrivateKeyPath = SshTunnel.PrivateKeyPath,
                SshPassphrase     = SshTunnel.Passphrase,
                SshRemoteHost     = SshTunnel.RemoteHost,
                SshRemotePort     = SshTunnel.RemotePort,
                SqlScript         = ActiveQueryTab?.SqlScript ?? string.Empty,
                LastSavedAt       = DateTime.Now
            };

            foreach (var t in TableModels)
            {
                proj.TableNodes.Add(new TableNodeSaveData
                {
                    TableName    = t.TableName,
                    X            = t.X,
                    Y            = t.Y,
                    FillColorHex = t.FillColorHex,
                    IsView       = t.IsView,
                    Columns      = t.Columns.Select(c => c.Name).ToList()
                });
            }

            string json = JsonSerializer.Serialize(proj, new JsonSerializerOptions { WriteIndented = true });
            SecureFileStore.WriteEncryptedFile(filePath, json);

            AddRecentProject(ProjectName, SelectedProvider, ConnectionString, filePath, SshTunnel);

            StatusMessage = $"Project saved to {Path.GetFileName(filePath)}";
            AddLogEntry($"Project saved to {filePath}");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.SaveProjectToFile: 正常終了しました");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save project file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.SaveProjectToFile: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Connection Wizard

    [RelayCommand]
    private void OpenConnectionWizard()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenConnectionWizard: 開始します");

            var dlg = new ConnectionWizardDialog(_apiManager, string.IsNullOrEmpty(SelectedProvider) ? "PostgreSQL" : SelectedProvider, ConnectionString, SshTunnel)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedProvider = dlg.SelectedProvider;
                ConnectionString = dlg.ConnectionString;
                SshTunnel        = BuildSshTunnelConfig(dlg);
                HasActiveProject = true;
                StatusMessage    = $"DBMS Connection updated: {SelectedProvider}";

                AddRecentProject($"{SelectedProvider} Session", SelectedProvider, ConnectionString, "", SshTunnel);

                _ = LoadSchemaAsync();
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.OpenConnectionWizard: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.OpenConnectionWizard: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region SQL Execution

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ExecuteQueryAsync: 開始します");

            if (ActiveQueryTab != null)
            {
                ActiveQueryTab.ProviderType     = SelectedProvider;
                ActiveQueryTab.ConnectionString = ConnectionString;
                ActiveQueryTab.SshTunnel        = SshTunnel;
                await ActiveQueryTab.ExecuteQueryAsync();
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ExecuteQueryAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ExecuteQueryAsync: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// アクティブなクエリタブのSQLに対する実行計画を取得し、新しいクエリタブへ表示します。
    ///
    /// [2. 処理フロー]
    /// 1. プロバイダー種別に応じて EXPLAIN 系構文でSQLをラップします（MySQL/MariaDB/PostgreSQL/SQLite は単一文）。
    /// 2. Oracle は EXPLAIN PLAN FOR 実行後に DBMS_XPLAN.DISPLAY から取得する2段階方式を用います。
    /// 3. SQL Server は本ビューアでは非対応のため、その旨を通知して終了します。
    /// </summary>
    [RelayCommand]
    private async Task ShowExecutionPlanAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ShowExecutionPlanAsync: 開始します");

            if (ActiveQueryTab == null || string.IsNullOrWhiteSpace(ActiveQueryTab.SqlScript))
            {
                StatusMessage = "実行計画を表示するSQLがありません。";
                return;
            }

            string originalSql = ActiveQueryTab.SqlScript.Trim();
            if (originalSql.EndsWith(";", StringComparison.Ordinal))
            {
                originalSql = originalSql.Substring(0, originalSql.Length - 1).TrimEnd();
            }

            string dialect = SelectedProvider.ToLowerInvariant();
            string explainSql;

            switch (dialect)
            {
                case "mysql":
                case "mariadb":
                case "postgresql":
                    explainSql = $"EXPLAIN {originalSql}";
                    break;

                case "sqlite":
                    explainSql = $"EXPLAIN QUERY PLAN {originalSql}";
                    break;

                case "oracle":
                {
                    // Oracle は単一文で完結しないため、EXPLAIN PLAN FOR を先に実行してから DBMS_XPLAN.DISPLAY で取得する
                    var client = await _apiManager.EnsureApiServerRunningAsync();
                    using var explainCall = client.ExecuteQuery(new QueryRequest
                    {
                        ProviderType     = SelectedProvider,
                        ConnectionString = ConnectionString,
                        Sql              = $"EXPLAIN PLAN FOR {originalSql}",
                        SshTunnel        = SshTunnel ?? new SshTunnelConfig()
                    });
                    while (await explainCall.ResponseStream.MoveNext(CancellationToken.None)) { }

                    explainSql = "SELECT PLAN_TABLE_OUTPUT FROM TABLE(DBMS_XPLAN.DISPLAY())";
                    break;
                }

                case "mssql":
                case "sqlserver":
                case "sql server":
                    StatusMessage = "SQL Server の実行計画表示は本ビューアでは未対応です（SQL Server Management Studio 等をご利用ください）。";
                    return;

                default:
                    StatusMessage = $"{SelectedProvider} の実行計画表示には対応していません。";
                    return;
            }

            var tab = AddQueryTab($"ExecutionPlan_{DateTime.Now:HHmmss}.sql", explainSql);
            await tab.ExecuteQueryAsync();

            StatusMessage = "実行計画を新しいクエリタブへ表示しました。";
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ShowExecutionPlanAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            StatusMessage = $"実行計画の取得に失敗しました: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ShowExecutionPlanAsync: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Reverse Engineering Schema & DBMS Object Tree

    [RelayCommand]
    private async Task LoadSchemaAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.LoadSchemaAsync: 開始します");

            StatusMessage = "Starting API Server on-demand & fetching DB Schema...";
            AddLogEntry($"On-demand fetching schema for {SelectedProvider}...");

            try
            {
                var client = await _apiManager.EnsureApiServerRunningAsync();

                var schemaResp = await client.GetSchemaAsync(new SchemaRequest
                {
                    ProviderType     = SelectedProvider,
                    ConnectionString = ConnectionString,
                    SshTunnel        = SshTunnel
                });

                // Table Designer が実カラム構成を読み込めるよう、テーブル名でスキーマを索引化して保持する
                _lastSchemaTablesByName.Clear();
                foreach (var t in schemaResp.Tables)
                {
                    _lastSchemaTablesByName[t.Name] = t;
                }

                using (_editContext.UndoManager.BeginTransaction("Load DB Schema"))
                {
                    ClearTableModels();
                    foreach (var t in schemaResp.Tables)
                    {
                        var model = new TableNodeModel(t.Name);
                        foreach (var c in t.Columns)
                        {
                            string pkMarker = c.IsPrimaryKey ? " (PK)" : "";
                            model.Columns.Add(new ColumnModel($"{c.Name} [{c.DataType}]{pkMarker}"));
                        }
                        TableModels.Add(model);
                    }

                    // ビューも ER 図（TableModels）へ投入する。同じ ModelObject 経路を通るため
                    // Undo/Redo・Copy/Cut/Paste もテーブルと同様に対応する（IsView フラグで区別・色分け）
                    foreach (var v in schemaResp.Views)
                    {
                        var model = new TableNodeModel(v.Name) { IsView = true };
                        foreach (var c in v.Columns)
                        {
                            model.Columns.Add(new ColumnModel($"{c.Name} [{c.DataType}]"));
                        }
                        TableModels.Add(model);
                    }
                }

                // ER図（MSAGL Graph）は全テーブルを1枚に描画すると大規模DB（実データ規模で数百テーブル）で
                // 視認不能になるため、完全なスキーマ応答を保持しておき、スキーマ（データベース）単位＋
                // 手動テーブル選択で都度絞り込んで描画する方式に変更。RebuildErDiagramGraph が実際の描画を担う。
                _lastFullSchemaResponse = schemaResp;

                var schemaGroups = schemaResp.Tables.Select(t => GetErDiagramSchemaGroup(t.Name))
                    .Concat(schemaResp.Views.Select(v => GetErDiagramSchemaGroup(v.Name)))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                ErDiagramSchemas.Clear();
                foreach (var s in schemaGroups)
                {
                    ErDiagramSchemas.Add(s);
                }

                // SelectedErDiagramSchema の変更は OnSelectedErDiagramSchemaChanged 経由で
                // ErDiagramTableChoices の再構築 → RebuildErDiagramGraph（初回ER図描画）まで自動的に行われるが、
                // 再接続時に同名スキーマが選択済みだと値が変化せずフックが発火しないため、明示的にも呼び直す
                // （_lastFullSchemaResponse は新しいデータに更新済みのため、確実に最新化するための保険）
                SelectedErDiagramSchema = schemaGroups.FirstOrDefault();
                RebuildErDiagramTableChoices();
                RebuildErDiagramGraph();

                DbObjectTree.Clear();
                var dbRootNode = new DbObjectNode($"{SelectedProvider} Database (Live Active)", DbObjectType.Database, "🗄");
                var schemaNode = new DbObjectNode("public / default", DbObjectType.Schema, "📦");

                var tablesFolder = new DbObjectNode("Tables", DbObjectType.Folder, "📁");
                foreach (var t in schemaResp.Tables)
                {
                    var tNode = new DbObjectNode(t.Name, DbObjectType.Table, "📋");
                    foreach (var c in t.Columns)
                    {
                        string icon = c.IsPrimaryKey ? "🔑" : "🔹";
                        tNode.Children.Add(new DbObjectNode($"{c.Name} ({c.DataType})", DbObjectType.Column, icon));
                    }

                    if (t.Indexes.Count > 0)
                    {
                        var indexesFolder = new DbObjectNode("Indexes", DbObjectType.Folder, "📁");
                        foreach (var idx in t.Indexes)
                        {
                            string uniqueLabel = idx.IsUnique ? "UNIQUE" : "NON-UNIQUE";
                            indexesFolder.Children.Add(new DbObjectNode(
                                $"{idx.Name} ({uniqueLabel}: {string.Join(", ", idx.Columns)})",
                                DbObjectType.Index, "🗝"));
                        }
                        tNode.Children.Add(indexesFolder);
                    }

                    tablesFolder.Children.Add(tNode);
                }
                schemaNode.Children.Add(tablesFolder);

                var viewsFolder = new DbObjectNode("Views", DbObjectType.Folder, "📁");
                foreach (var v in schemaResp.Views)
                {
                    var vNode = new DbObjectNode(v.Name, DbObjectType.View, "👁");
                    foreach (var c in v.Columns)
                    {
                        vNode.Children.Add(new DbObjectNode($"{c.Name} ({c.DataType})", DbObjectType.Column, "🔹"));
                    }
                    viewsFolder.Children.Add(vNode);
                }
                schemaNode.Children.Add(viewsFolder);

                var procFolder = new DbObjectNode("Stored Procedures", DbObjectType.Folder, "📁");
                foreach (var p in schemaResp.Procedures)
                {
                    procFolder.Children.Add(new DbObjectNode($"{p.Name} ({p.RoutineType})", DbObjectType.Procedure, "⚙"));
                }
                schemaNode.Children.Add(procFolder);

                dbRootNode.Children.Add(schemaNode);
                DbObjectTree.Add(dbRootNode);

                StatusMessage = $"DBMS Live Connected: {schemaResp.Tables.Count} tables, {schemaResp.Views.Count} views, {schemaResp.Procedures.Count} procedures, {schemaResp.ForeignKeys.Count} relationships.";
                AddLogEntry($"DBMS Live Connected successfully ({schemaResp.Tables.Count} tables).");
            }
            catch (Exception apiEx)
            {
                // バックエンド未接続または接続試行エラー時は、ダミーテーブルを表示せず明確なエラー/通知ノードを表示
                System.Diagnostics.Debug.WriteLine($"[WARNING] LoadSchemaAsync: DB Connection Error - {apiEx.Message}");
                
                DbObjectTree.Clear();
                var dbRootNode = new DbObjectNode($"{SelectedProvider} ({ProjectName})", DbObjectType.Database, "🗄");
                var errorNode  = new DbObjectNode($"❌ Connection Error: {apiEx.Message}", DbObjectType.Folder, "⚠️");
                dbRootNode.Children.Add(errorNode);
                DbObjectTree.Add(dbRootNode);

                StatusMessage = $"Connection Notice: Could not connect to {SelectedProvider} ({ProjectName}).";
                AddLogEntry($"Connection Notice: Could not fetch live schema for {SelectedProvider} ({ProjectName}).");
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.LoadSchemaAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Schema Notice: {ex.Message}";
            AddLogEntry($"Schema Notice: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.LoadSchemaAsync: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Undo / Redo & Copy / Cut / Paste

    [RelayCommand]
    private void Undo()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Undo: 開始します");

            if (_undoManager.CanUndo)
            {
                _undoManager.Undo();
                RebuildMsaglGraph();
                StatusMessage = "Undo performed.";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Undo: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.Undo: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void Redo()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Redo: 開始します");

            if (_undoManager.CanRedo)
            {
                _undoManager.Redo();
                RebuildMsaglGraph();
                StatusMessage = "Redo performed.";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Redo: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.Redo: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Copy: 開始します");

            if (TableModels.Count > 0)
            {
                _clipboardManager.Copy(TableModels.Cast<ModelObject>().ToList());
                StatusMessage = $"{TableModels.Count} Table Nodes copied to clipboard.";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Copy: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.Copy: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void Cut()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Cut: 開始します");

            if (TableModels.Count > 0)
            {
                _clipboardManager.Cut(TableModels, TableModels.ToList());
                RebuildMsaglGraph();
                StatusMessage = "Nodes cut to clipboard.";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Cut: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.Cut: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private void Paste()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Paste: 開始します");

            if (_clipboardManager.CanPaste)
            {
                _clipboardManager.PasteMultiple(TableModels);
                RebuildMsaglGraph();
                StatusMessage = "Nodes pasted from clipboard.";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.Paste: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.Paste: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Stored Procedure Debugger

    [RelayCommand]
    private async Task StartDebugAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StartDebugAsync: 開始します");

            IsDebugging   = true;
            StatusMessage = "[Preview] Starting simulated debug session (does not execute against the real database)...";
            DebugVariables.Clear();
            AddLogEntry("[Preview] Stored Procedure Debugger is a simulated preview feature. Line hits and variables shown are NOT the result of executing your procedure against the connected database.");

            var client = await _apiManager.EnsureApiServerRunningAsync();

            _debugCall = client.DebugProcedure();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _debugCall.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        var ev = _debugCall.ResponseStream.Current;
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            CurrentDebugLine = ev.LineNumber;
                            DebugLineChanged?.Invoke(ev.LineNumber);

                            DebugVariables.Clear();
                            foreach (var v in ev.Variables)
                            {
                                DebugVariables.Add(v);
                            }
                            StatusMessage = $"[Debug] Line {ev.LineNumber}: {ev.Message}";
                            AddLogEntry($"[Debug Line {ev.LineNumber}] {ev.Message}");

                            if (ev.EventType == DebugEventType.EventCompleted)
                            {
                                IsDebugging      = false;
                                CurrentDebugLine = -1;
                                DebugLineChanged?.Invoke(-1);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Debug Stream Ended: {ex.Message}";
                        IsDebugging   = false;
                    });
                }
            });

            await _debugCall.RequestStream.WriteAsync(new DebugCommand
            {
                Action           = DebugAction.ActionStart,
                ProviderType     = SelectedProvider,
                ConnectionString = ConnectionString,
                ProcedureName    = ActiveQueryTab?.Title ?? "Procedure",
                SqlScript        = ActiveQueryTab?.SqlScript ?? string.Empty
            });

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StartDebugAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start debug session: {ex.Message}";
            IsDebugging   = false;
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.StartDebugAsync: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StepOverDebugAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StepOverDebugAsync: 開始します");
            if (_debugCall == null || !IsDebugging) return;
            await _debugCall.RequestStream.WriteAsync(new DebugCommand { Action = DebugAction.ActionStepOver });
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StepOverDebugAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.StepOverDebugAsync: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private async Task ContinueDebugAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ContinueDebugAsync: 開始します");
            if (_debugCall == null || !IsDebugging) return;
            await _debugCall.RequestStream.WriteAsync(new DebugCommand { Action = DebugAction.ActionContinue });
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.ContinueDebugAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.ContinueDebugAsync: 例外発生 - {ex.Message}");
            throw;
        }
    }

    [RelayCommand]
    private async Task StopDebugAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StopDebugAsync: 開始します");

            if (_debugCall == null || !IsDebugging) return;
            await _debugCall.RequestStream.WriteAsync(new DebugCommand { Action = DebugAction.ActionStop });

            IsDebugging      = false;
            CurrentDebugLine = -1;
            DebugLineChanged?.Invoke(-1);
            StatusMessage    = "Debug session stopped.";
            AddLogEntry("Debug session stopped.");

            System.Diagnostics.Debug.WriteLine("[INFO] MainViewModel.StopDebugAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MainViewModel.StopDebugAsync: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
