// ファイル名     : QueryTabViewModel.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\ViewModels\QueryTabViewModel.cs
// クラス/概要    : QueryTabViewModel (Class)
// 処理概要/目的  : SSMS スタイルの独立したクエリタブ1枚分のビューモデル。リアル DBMS 通信を行い、DBMS から取得した本物のデータセットを忠実に UI へ表示。
// 使用方法/適用先: MainViewModel の QueryTabs コレクション要素としてバインドされ、クエリ送信・結果表示を提供
// 依存関係       : CommunityToolkit.Mvvm.ComponentModel, NeoDbStudio.Client.Helpers.ApiProcessManager, System.Data.DataTable
// 注意事項       : ダミーデータ生成ロジックは完全に全廃。DBMS の応答結果およびエラー状態をありのまま正確に表示します。
// 更新履歴       : 2026/01/01 新規作成
//                 2026/07/29 ダミーデータ全廃・純粋リアル DBMS 応答表示専用化
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.ViewModels;

#region QueryTabViewModel Class

/// <summary>
/// 個別クエリタブ用 ViewModel (純粋リアル DBMS 応答表示モデル)。
/// </summary>
public partial class QueryTabViewModel : ObservableObject
{
    #region Fields & Properties

    private readonly ApiProcessManager? _apiManager;
    private DataTable _fullResultTable = new DataTable(); // 全件バッファ（サーバー側ページング中は現在ページ分のみを保持）
    private string _lastExecutedSql = string.Empty;        // サーバー側ページング再取得用に保持する実行済みSQL文

    [ObservableProperty]
    private bool _isServerPaginated; // true: サーバー側 LIMIT/OFFSET によるページング中（大規模結果セットでも高速）

    [ObservableProperty]
    private string _title = "Query 1.sql";

    [ObservableProperty]
    private string _sqlScript = string.Empty;

    [ObservableProperty]
    private string _providerType = "PostgreSQL";

    [ObservableProperty]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    private SshTunnelConfig? _sshTunnel = null;

    [ObservableProperty]
    private bool _autoCommit = true; // false時：BeginTransactionで確立したセッション上で全クエリを実行しCommit/Rollbackを待つ

    [ObservableProperty]
    private string _sessionId = string.Empty; // オートコミットOFF時、進行中トランザクションのセッションID（無い間は空）

    /// <summary>進行中のトランザクションがあるかどうか。</summary>
    public bool IsInTransaction => !string.IsNullOrEmpty(SessionId);

    [ObservableProperty]
    private DataTable _resultTable = new DataTable(); // 画面表示用 DataTable

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = "Ready - Execute Query (F5) to fetch data from DBMS.";

    // --- Paging Properties ---
    [ObservableProperty]
    private int _pageIndex = 1; // 1ベース

    [ObservableProperty]
    private int _pageSize = 50; // デフォルト 50件/ページ

    [ObservableProperty]
    private int _totalRowCount = 0;

    [ObservableProperty]
    private int _totalPages = 1;

    public ObservableCollection<int> PageSizeOptions { get; } = new() { 10, 50, 100, 500, 1000 };

    /// <summary>クエリ実行完了時に発火する通知イベント。</summary>
    public event Action<string>? QueryExecuted;

    #endregion

    #region Constructors

    /// <summary>
    /// 処理内容     : QueryTabViewModel のデフォルトコンストラクター。
    /// 処理ロジック : 空の状態へ初期化します。
    /// </summary>
    public QueryTabViewModel()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] QueryTabViewModel.ctor: デフォルト初期化を行います");
            ResetToEmptyState();
            System.Diagnostics.Debug.WriteLine("[INFO] QueryTabViewModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 処理内容     : パラメータ付きコンストラクター。
    /// 処理ロジック : タイトル、プロバイダー、接続文字列、初期 SQL をセットして初期化します。
    /// </summary>
    public QueryTabViewModel(
        ApiProcessManager? apiManager,
        string title,
        string providerType,
        string connectionString,
        string initialSql = "")
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] QueryTabViewModel.ctor: 初期化を行います ({title})");

            _apiManager      = apiManager;
            Title            = title ?? "Query.sql";
            ProviderType     = string.IsNullOrEmpty(providerType) ? "PostgreSQL" : providerType;
            ConnectionString = connectionString ?? string.Empty;
            SqlScript        = initialSql ?? string.Empty;

            ResetToEmptyState();

            System.Diagnostics.Debug.WriteLine("[INFO] QueryTabViewModel.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Public Accessors

    /// <summary>
    /// ページングされていない全件の結果セットを取得します（CSV/JSON エクスポート等、表示ページに限定しない用途向け）。
    /// 注意: <see cref="IsServerPaginated"/> が true の間はこのメソッドは「現在ページ分のみ」を返却します。
    /// 全件を確実に取得したい場合は <see cref="GetFullResultTableAsync"/> を使用してください。
    /// </summary>
    /// <returns>直近のクエリ実行で取得した結果を保持する DataTable を返却します。</returns>
    public DataTable GetFullResultTable()
    {
        return _fullResultTable;
    }

    /// <summary>
    /// [1. 処理概要]
    /// 表示ページに限定しない全件の結果セットを取得します。サーバー側ページング中（<see cref="IsServerPaginated"/> が true）の場合は、
    /// エクスポート等で全件が必要な用途向けに、ページングを付けずに DBMS へ再クエリして全件を取得します。
    /// </summary>
    /// <returns>全件を保持する DataTable を返却します（再取得失敗時は現在保持分へフォールバック）。</returns>
    public async Task<DataTable> GetFullResultTableAsync()
    {
        if (!IsServerPaginated || _apiManager == null) // 非ページング時は既に全件を保持済み
        {
            return _fullResultTable;
        }

        try
        {
            var client = await _apiManager.EnsureApiServerRunningAsync();
            return await ExecuteAndCollectAsync(client, _lastExecutedSql);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.GetFullResultTableAsync: 例外発生 - {ex.Message}");
            return _fullResultTable; // 再取得失敗時は直近保持分（現在ページ分のみ）へフォールバック
        }
    }

    /// <summary>
    /// クエリを実行し、ヘッダー・行データを DataTable へ収集します（通常実行・件数取得・ページ再取得の共通処理）。
    /// </summary>
    /// <param name="client">[パラメータ] gRPC クライアントを指定します。</param>
    /// <param name="sql">[パラメータ] 実行する SQL 文を指定します。</param>
    /// <param name="pageSize">[パラメータ] サーバー側ページングを行う場合の1ページあたり件数（0=ページングなし）を指定します。</param>
    /// <param name="pageOffset">[パラメータ] サーバー側ページングを行う場合の開始オフセットを指定します。</param>
    /// <returns>取得結果を保持する DataTable を返却します。</returns>
    private async Task<DataTable> ExecuteAndCollectAsync(NeoDbStudio.Shared.DbEngine.DbEngineClient client, string sql, int pageSize = 0, int pageOffset = 0)
    {
        var dt = new DataTable();
        using var call = client.ExecuteQuery(new QueryRequest
        {
            ProviderType     = ProviderType,
            ConnectionString = ConnectionString,
            Sql              = sql,
            SshTunnel        = SshTunnel ?? new SshTunnelConfig(),
            SessionId        = AutoCommit ? string.Empty : SessionId,
            PageSize         = pageSize,
            PageOffset       = pageOffset
        });

        while (await call.ResponseStream.MoveNext(CancellationToken.None))
        {
            var resp = call.ResponseStream.Current;

            if (resp.Header != null && resp.Header.Names.Count > 0)
            {
                foreach (var colName in resp.Header.Names)
                {
                    if (!dt.Columns.Contains(colName))
                    {
                        dt.Columns.Add(colName);
                    }
                }
            }
            else if (resp.Row != null && resp.Row.Values.Count > 0)
            {
                var row = dt.NewRow();
                for (int i = 0; i < resp.Row.Values.Count && i < dt.Columns.Count; i++)
                {
                    row[i] = resp.Row.Values[i];
                }
                dt.Rows.Add(row);
            }
        }

        // Rows.Add() 直後は RowState=Added のため DataRowVersion.Original が例外を投げる。
        // インライン編集（Original vs Current の差分検出）を成立させるため Unchanged へ確定させておく
        dt.AcceptChanges();

        return dt;
    }

    /// <summary>
    /// [1. 処理概要]
    /// SQL 文を COUNT(*) サブクエリでラップして総件数を取得します。サーバー側で単純な単一 SELECT 文と
    /// 判定できない場合（複数ステートメント・非SELECT・MongoDB/Redis 等）は DBMS 側でエラーとなるため、
    /// その場合は null を返却し、呼び出し元は従来どおりの全件取得（クライアント側ページング）へフォールバックします。
    /// </summary>
    /// <param name="client">[パラメータ] gRPC クライアントを指定します。</param>
    /// <param name="sql">[パラメータ] 件数を数える対象の SQL 文を指定します。</param>
    /// <returns>取得できた総件数、またはサーバー側ページング非対応と判定された場合は null を返却します。</returns>
    private async Task<long?> TryGetServerRowCountAsync(NeoDbStudio.Shared.DbEngine.DbEngineClient client, string sql)
    {
        try
        {
            string trimmed = sql.Trim();
            if (trimmed.EndsWith(";", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            }
            if (trimmed.Length == 0)
            {
                return null;
            }

            // Oracle はテーブルエイリアスへの AS キーワード付与を許容しないため、プロバイダー別に構文を分ける
            bool isOracle = string.Equals(ProviderType, "Oracle", StringComparison.OrdinalIgnoreCase);
            string countSql = isOracle
                ? $"SELECT COUNT(*) AS neodb_total_count FROM ({trimmed}) neodb_count_src"
                : $"SELECT COUNT(*) AS neodb_total_count FROM ({trimmed}) AS neodb_count_src";
            var dt = await ExecuteAndCollectAsync(client, countSql);

            if (dt.Rows.Count > 0 && dt.Columns.Count > 0
                && long.TryParse(dt.Rows[0][0]?.ToString(), out long count))
            {
                return count;
            }
            return null;
        }
        catch (Exception ex)
        {
            // 複数ステートメント・DDL・MongoDB/Redis 等、サブクエリ化できない SQL は例外となるため
            // サーバー側ページング非対応と判定し、呼び出し元での全件取得フォールバックへ委ねる
            System.Diagnostics.Debug.WriteLine($"[INFO] QueryTabViewModel.TryGetServerRowCountAsync: サーバー側ページング非対応と判定 - {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Inline Grid Edit Support

    /// <summary>
    /// [1. 処理概要]
    /// DataRow の Original/Current 値の差分と主キー列一覧から、UPDATE 文を組み立てます。
    /// WHERE 句には編集前（Original）の主キー値を用いて対象行を一意に特定します。
    /// 変更が無い場合や主キーが特定できない場合は null を返却します（純粋なロジックのため単体テスト可能）。
    /// </summary>
    /// <param name="tableName">[パラメータ] 対象テーブル名を指定します。</param>
    /// <param name="row">[パラメータ] 編集された DataRow を指定します（AcceptChanges 前）。</param>
    /// <param name="primaryKeyColumns">[パラメータ] 対象テーブルの主キー列名一覧を指定します。</param>
    /// <param name="errorMessage">[出力パラメータ] 生成不可の場合の理由メッセージを返却します。</param>
    /// <returns>生成された UPDATE 文、または生成不可の場合は null を返却します。</returns>
    public static string? BuildInlineUpdateSql(string tableName, DataRow row, IReadOnlyList<string> primaryKeyColumns, out string? errorMessage)
    {
        errorMessage = null;

        if (primaryKeyColumns.Count == 0)
        {
            errorMessage = "主キーが特定できないため編集を反映できません。";
            return null;
        }

        var changedColumns = new List<(string Name, object? NewValue)>();
        foreach (DataColumn col in row.Table.Columns)
        {
            object original = row[col, DataRowVersion.Original];
            object current  = row[col, DataRowVersion.Current];
            if (!Equals(original, current))
            {
                changedColumns.Add((col.ColumnName, current == DBNull.Value ? null : current));
            }
        }

        if (changedColumns.Count == 0) // 実質的な変更なし（エラーではない）
        {
            return null;
        }

        var whereParts = new List<string>();
        foreach (var pk in primaryKeyColumns)
        {
            if (!row.Table.Columns.Contains(pk))
            {
                errorMessage = $"結果セットに主キー列 '{pk}' が含まれていないため編集を反映できません（SELECT * のご利用を推奨します）。";
                return null;
            }

            object pkOriginal = row[pk, DataRowVersion.Original];
            if (pkOriginal == DBNull.Value)
            {
                errorMessage = $"主キー列 '{pk}' がNULLのため対象行を特定できません。";
                return null;
            }
            whereParts.Add($"{pk} = {FormatSqlLiteral(pkOriginal.ToString())}");
        }

        string setClause = string.Join(", ", changedColumns.Select(c =>
            $"{c.Name} = {(c.NewValue == null ? "NULL" : FormatSqlLiteral(c.NewValue.ToString()))}"));

        return $"UPDATE {tableName} SET {setClause} WHERE {string.Join(" AND ", whereParts)}";
    }

    /// <summary>SQL文字列リテラルとして安全な形式へエスケープします（単一引用符の二重化）。</summary>
    private static string FormatSqlLiteral(string? value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
    }

    /// <summary>
    /// 任意の1文（主に UPDATE/DELETE 等の DML）を実行し、結果ストリームを読み切ります。
    /// インライン編集で生成された UPDATE 文の実行に使用します。
    /// </summary>
    /// <param name="sql">[パラメータ] 実行する SQL 文を指定します。</param>
    public async Task ExecuteRawStatementAsync(string sql)
    {
        if (_apiManager == null)
        {
            throw new InvalidOperationException("APIマネージャーが初期化されていません。");
        }

        if (!AutoCommit && string.IsNullOrEmpty(SessionId))
        {
            var bootstrapClient = await _apiManager.EnsureApiServerRunningAsync();
            await BeginTransactionInternalAsync(bootstrapClient);
        }

        var client = await _apiManager.EnsureApiServerRunningAsync();
        await ExecuteAndCollectAsync(client, sql);
    }

    #endregion

    #region Reset & Empty State

    /// <summary>
    /// 処理内容     : 結果テーブルおよびページング状態を空へリセットします。
    /// </summary>
    private void ResetToEmptyState()
    {
        try
        {
            _fullResultTable = new DataTable();
            ResultTable      = new DataTable();
            TotalRowCount    = 0;
            TotalPages       = 1;
            PageIndex        = 1;
            StatusMessage    = "Ready - Execute Query (F5) to fetch data from DBMS.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.ResetToEmptyState: {ex.Message}");
        }
    }

    #endregion

    #region Public Commands & Execution (Pure Real DBMS Only)

    /// <summary>
    /// 処理内容     : SQL クエリをバックエンド DBMS へ送信実行し、返却された本物のデータを DataGrid へ表示します。
    /// 処理ロジック : gRPC バックエンド通信を行い、返却された実際のレコードを DataTable へ格納します。通信エラー時はエラーメッセージを正しく表示します。
    /// </summary>
    [RelayCommand]
    public async Task ExecuteQueryAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] QueryTabViewModel.ExecuteQueryAsync: クエリ実行を開始します ({Title})");

            if (string.IsNullOrWhiteSpace(SqlScript))
            {
                StatusMessage = "Warning: SQL script is empty.";
                return;
            }

            IsExecuting     = true;
            StatusMessage   = $"Executing query on DBMS [{ProviderType}]...";
            IsServerPaginated = false;
            _lastExecutedSql  = SqlScript;

            // gRPC バックエンド経由での純粋な DBMS クエリ実行
            if (_apiManager != null)
            {
                var client = await _apiManager.EnsureApiServerRunningAsync();

                if (!AutoCommit && string.IsNullOrEmpty(SessionId))
                {
                    // オートコミットOFFで初回実行時：先にトランザクションを開始してセッションを確立する
                    await BeginTransactionInternalAsync(client);
                }

                // まずサーバー側ページングを試みる（単純な単一SELECT文のみ対応・非対応時はnullが返る）。
                // 大規模テーブルでも件数取得＋当該ページ分のみの取得で完結し、全件をメモリへ読み込まずに済む
                long? serverTotal = await TryGetServerRowCountAsync(client, SqlScript);

                if (serverTotal.HasValue)
                {
                    IsServerPaginated = true;
                    TotalRowCount     = (int)Math.Min(serverTotal.Value, int.MaxValue);
                    PageIndex         = 1;

                    var pageDt = await ExecuteAndCollectAsync(client, _lastExecutedSql, PageSize, pageOffset: 0);
                    _fullResultTable = pageDt; // サーバー側ページング中はこの時点のページ分のみを保持
                    ResultTable       = pageDt;
                    TotalPages        = TotalRowCount > 0 ? (int)Math.Ceiling((double)TotalRowCount / PageSize) : 1;
                    OnPropertyChanged(nameof(ResultTable));
                }
                else
                {
                    // サーバー側ページング非対応（複数ステートメント・非SELECT等）：従来どおり全件取得しクライアント側でページングする
                    var dt = await ExecuteAndCollectAsync(client, SqlScript);
                    _fullResultTable = dt;
                    TotalRowCount    = dt.Rows.Count;
                    PageIndex        = 1;
                    UpdatePagedView();
                }
            }

            IsExecuting   = false;
            StatusMessage = IsServerPaginated
                ? $"Query Executed Successfully: {TotalRowCount} rows total (server-side paging, {ResultTable.Rows.Count} shown)."
                : $"Query Executed Successfully: {TotalRowCount} rows returned.";

            string logMsg = $"[{DateTime.Now:HH:mm:ss}] Executed SQL on {ProviderType}: {SqlScript.Replace("\n", " ")} ({TotalRowCount} rows)";
            QueryExecuted?.Invoke(logMsg);

            System.Diagnostics.Debug.WriteLine("[INFO] QueryTabViewModel.ExecuteQueryAsync: 正常終了しました");
        }
        catch (Exception ex)
        {
            IsExecuting       = false;
            IsServerPaginated = false;
            _fullResultTable  = new DataTable();
            ResultTable       = new DataTable();
            TotalRowCount = 0;
            StatusMessage = $"Execution Error: {ex.Message}";
            
            string logMsg = $"[{DateTime.Now:HH:mm:ss}] Execution Error ({ProviderType}): {ex.Message}";
            QueryExecuted?.Invoke(logMsg);

            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.ExecuteQueryAsync: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Transaction Control (Begin / Commit / Rollback)

    /// <summary>
    /// トランザクションを開始し SessionId を確立する内部処理（ExecuteQueryAsync の暗黙開始・明示 BeginTransactionCommand の両方から呼ばれる）。
    /// </summary>
    private async Task BeginTransactionInternalAsync(NeoDbStudio.Shared.DbEngine.DbEngineClient client)
    {
        var resp = await client.BeginTransactionAsync(new BeginTransactionRequest
        {
            ProviderType     = ProviderType,
            ConnectionString = ConnectionString,
            SshTunnel        = SshTunnel ?? new SshTunnelConfig()
        });

        if (!resp.Success)
        {
            throw new InvalidOperationException($"トランザクション開始に失敗しました: {resp.ErrorMessage}");
        }

        SessionId = resp.SessionId;
    }

    /// <summary>
    /// 明示的にトランザクションを開始します（オートコミットOFF時、クエリ実行前に呼び出し可能）。
    /// </summary>
    [RelayCommand]
    public async Task BeginTransactionAsync()
    {
        try
        {
            if (_apiManager == null || IsInTransaction)
            {
                return;
            }

            var client = await _apiManager.EnsureApiServerRunningAsync();
            await BeginTransactionInternalAsync(client);
            StatusMessage = "Transaction started.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Begin Transaction Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.BeginTransactionAsync: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 進行中のトランザクションをコミットします。
    /// </summary>
    [RelayCommand]
    public async Task CommitAsync()
    {
        try
        {
            if (_apiManager == null || !IsInTransaction)
            {
                return;
            }

            var client = await _apiManager.EnsureApiServerRunningAsync();
            var resp   = await client.CommitTransactionAsync(new TransactionSessionRequest { SessionId = SessionId });
            StatusMessage = resp.Success ? "Transaction committed." : $"Commit Error: {resp.ErrorMessage}";
            SessionId = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Commit Error: {ex.Message}";
            SessionId = string.Empty; // サーバー側で失効している可能性が高いためクライアント状態はリセット
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.CommitAsync: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 進行中のトランザクションをロールバックします。
    /// </summary>
    [RelayCommand]
    public async Task RollbackAsync()
    {
        try
        {
            if (_apiManager == null || !IsInTransaction)
            {
                return;
            }

            var client = await _apiManager.EnsureApiServerRunningAsync();
            var resp   = await client.RollbackTransactionAsync(new TransactionSessionRequest { SessionId = SessionId });
            StatusMessage = resp.Success ? "Transaction rolled back." : $"Rollback Error: {resp.ErrorMessage}";
            SessionId = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rollback Error: {ex.Message}";
            SessionId = string.Empty; // サーバー側で失効している可能性が高いためクライアント状態はリセット
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.RollbackAsync: 例外発生 - {ex.Message}");
        }
    }

    partial void OnSessionIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsInTransaction));
    }

    partial void OnAutoCommitChanged(bool value)
    {
        // オートコミットをONへ戻す際、宙に浮いたトランザクションが残らないよう安全側でロールバックする
        if (value && IsInTransaction)
        {
            _ = RollbackAsync();
        }
    }

    #endregion

    #region Paging Logic

    /// <summary>
    /// 処理内容     : 現在のページ番号およびページサイズに従って DataGrid 表示用の ResultTable を抽出設定します。
    /// 処理ロジック : _fullResultTable から該当範囲の DataRow をコピーし、ResultTable へ割り当てて PropertyChanged 通知を発火します。
    /// </summary>
    private void UpdatePagedView()
    {
        try
        {
            if (_fullResultTable == null || _fullResultTable.Rows.Count == 0)
            {
                ResultTable = new DataTable();
                TotalPages  = 1;
                return;
            }

            TotalPages = (int)Math.Ceiling((double)TotalRowCount / PageSize);
            if (TotalPages < 1) TotalPages = 1;

            if (PageIndex < 1) PageIndex = 1;
            if (PageIndex > TotalPages) PageIndex = TotalPages;

            int skip = (PageIndex - 1) * PageSize;
            var pagedDt = _fullResultTable.Clone();

            var rows = _fullResultTable.AsEnumerable().Skip(skip).Take(PageSize);
            foreach (var r in rows)
            {
                pagedDt.ImportRow(r);
            }

            ResultTable = pagedDt;
            OnPropertyChanged(nameof(ResultTable)); // UI への確実なプロパティ更新通知
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.UpdatePagedView: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のページ（PageIndex・PageSize）に応じた結果を表示へ反映します。
    /// サーバー側ページング中はDBMSへ再クエリし、非ページング時は保持済み全件から抽出します。
    /// </summary>
    private async Task GoToCurrentPageAsync()
    {
        try
        {
            if (IsServerPaginated && _apiManager != null)
            {
                var client = await _apiManager.EnsureApiServerRunningAsync();
                var pageDt = await ExecuteAndCollectAsync(client, _lastExecutedSql, PageSize, (PageIndex - 1) * PageSize);
                _fullResultTable = pageDt;
                ResultTable       = pageDt;
                TotalPages        = TotalRowCount > 0 ? (int)Math.Ceiling((double)TotalRowCount / PageSize) : 1;
                OnPropertyChanged(nameof(ResultTable));
            }
            else
            {
                UpdatePagedView();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Paging Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ERROR] QueryTabViewModel.GoToCurrentPageAsync: 例外発生 - {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FirstPageAsync()
    {
        try
        {
            if (PageIndex > 1)
            {
                PageIndex = 1;
                await GoToCurrentPageAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.FirstPageAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        try
        {
            if (PageIndex > 1)
            {
                PageIndex--;
                await GoToCurrentPageAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.PreviousPageAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        try
        {
            if (PageIndex < TotalPages)
            {
                PageIndex++;
                await GoToCurrentPageAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.NextPageAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LastPageAsync()
    {
        try
        {
            if (PageIndex < TotalPages)
            {
                PageIndex = TotalPages;
                await GoToCurrentPageAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.LastPageAsync: {ex.Message}");
        }
    }

    partial void OnPageSizeChanged(int value)
    {
        try
        {
            PageIndex = 1;
            _ = GoToCurrentPageAsync(); // partial プロパティ変更フックは void のため fire-and-forget で実行する
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] QueryTabViewModel.OnPageSizeChanged: {ex.Message}");
        }
    }

    #endregion
}

#endregion
