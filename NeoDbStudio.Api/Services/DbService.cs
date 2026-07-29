// ファイル名     : DbService.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Api\Services\DbService.cs
// クラス/概要    : DbServiceImpl (Class)
// 処理概要/目的  : NeoDB Studio API サーバの gRPC サービス実装。マルチ DBMS（PostgreSQL, MySQL, SQLite, ODBC, OLE DB 等）に対するクエリ非同期ストリーミング、スキーマ自動解析、およびストアドデバッグの双方向シミュレーション処理を提供
// 使用方法/適用先: NeoDbStudio.Api プロジェクトの ASP.NET Core gRPC パイプラインへマップ登録
// 依存関係       : Dapper, System.Data.Common.DbDataReader, Grpc.Core, NeoDbStudio.Shared.DbEngine.DbEngineBase
// 注意事項       : クエリ実行は ExecuteQuery RPC 経由で 1 行ずつストリーミング応答を返却します。
//                 MongoDB / Redis は ADO.NET（DbConnection）を持たない非リレーショナル DBMS のため、
//                 CreateConnection を経由せず専用の実行・解析経路（ExecuteDocumentStoreQueryAsync 等）で処理します。
// 更新履歴       : 2026/07/28 コーディング規約全適用リファクタリング
//                 2026/07/29 Oracle / MongoDB / Redis のプロバイダー実装を追加
//                            （従来は switch の default 節で Npgsql へ落ちており、接続ウィザードで
//                             選択可能であるにもかかわらず必ず接続失敗していた不具合を根本修正）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
using NeoDbStudio.Shared;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Renci.SshNet;
using StackExchange.Redis;

namespace NeoDbStudio.Api.Services;

#region DbServiceImpl Class

/// <summary>
/// NeoDB Studio API の gRPC サービス実装クラス。
/// </summary>
public class DbServiceImpl : DbEngine.DbEngineBase
{
    #region Fields

    private readonly ILogger<DbServiceImpl> _logger; // 構造化ロガーインスタンス
    private readonly DbSessionManager _sessionManager; // トランザクションセッション管理（オートコミットOFF時）

    private const int MaxRedisKeyScanCount     = 5000; // Redis スキーマ解析時のキー走査上限（大規模キー空間での応答停止を防止）
    private const int DefaultDocumentFetchLimit = 200;  // MongoDB クエリで limit 未指定時の既定取得件数上限
    private const int RedisPipelineBatchSize   = 500;  // Redis KeyTypeAsync のパイプライン化バッチサイズ（大量同時発火によるバースト負荷を抑制）

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// ILogger と DbSessionManager を受け取り DbServiceImpl インスタンスを初期化します。
    /// </summary>
    /// <param name="logger">[パラメータ] ロガーインスタンスを指定します。</param>
    /// <param name="sessionManager">[パラメータ] トランザクションセッション管理サービスを指定します。</param>
    public DbServiceImpl(ILogger<DbServiceImpl> logger, DbSessionManager sessionManager)
    {
        try
        {
            _logger         = logger ?? throw new ArgumentNullException(nameof(logger)); // NULL検証（?? 演算子）
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger.LogInformation("[INFO] DbServiceImpl.ctor: 初期化完了しました");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] DbServiceImpl.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region gRPC RPC Implementations

    /// <summary>
    /// [1. 処理概要]
    /// クエリ実行要求を受け取り、指定された DBMS へ接続して非同期ストリーミング形式で結果セット（列情報および行データ）を返却します。
    /// </summary>
    /// <param name="request">[パラメータ] 実行要求パラメータ（Provider, ConnStr, SQL）を指定します。</param>
    /// <param name="responseStream">[パラメータ] クライアントへレスポンスを送るストリームを指定します。</param>
    /// <param name="context">[パラメータ] gRPC 呼び出しコンテキストを指定します。</param>
    /// <returns>非同期タスク Task を返却します。</returns>
    public override async Task ExecuteQuery(
        QueryRequest request, 
        IServerStreamWriter<QueryResponse> responseStream, 
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"[INFO] DbServiceImpl.ExecuteQuery: 開始します (Provider={request.ProviderType})");

            if (request == null || responseStream == null) // NULL 検証
            {
                throw new ArgumentNullException("Request or ResponseStream is null.");
            }

            // MongoDB / Redis は ADO.NET 非対応のため、リレーショナル経路（CreateConnection）へ入れずに専用経路で処理する
            if (IsNonRelationalProvider(request.ProviderType))
            {
                await ExecuteNonRelationalQueryAsync(request, responseStream, context);
                _logger.LogInformation("[INFO] DbServiceImpl.ExecuteQuery: 非リレーショナルクエリが正常完了しました");
                return;
            }

            bool useSession = !string.IsNullOrEmpty(request.SessionId);
            DbConnection conn;
            DbTransaction? transaction = null;
            SshTunnelHandle? tunnel = null;

            if (useSession)
            {
                // オートコミットOFF：BeginTransaction で確立済みの接続・トランザクションを再利用（新規接続もトンネルも張らない）
                if (!_sessionManager.TryGetSession(request.SessionId, out var sessionConn, out var sessionTx))
                {
                    throw new InvalidOperationException($"トランザクションセッションが見つかりません（タイムアウト等で失効した可能性があります）: {request.SessionId}");
                }
                conn        = sessionConn;
                transaction = sessionTx;
            }
            else
            {
                // オートコミットON（既定）：従来どおり呼び出し毎に接続・トンネルを確立して即座に破棄
                string connectionString = request.ConnectionString;
                tunnel = OpenSshTunnelIfNeeded(request.SshTunnel, ref connectionString, request.ProviderType);
                conn = CreateConnection(request.ProviderType, connectionString);
                await conn.OpenAsync(context.CancellationToken);
            }

            try
            {
                // page_size > 0 の場合、単純な単一 SELECT 文に限りサーバー側 LIMIT/OFFSET 相当へ書き換える
                // （複数ステートメント・非SELECT等は安全側で従来どおり全件取得のまま実行する）
                string sqlToExecute = request.PageSize > 0
                    ? BuildPaginatedSql(request.ProviderType, request.Sql, request.PageSize, request.PageOffset)
                    : request.Sql;

                using var reader = await conn.ExecuteReaderAsync(sqlToExecute, transaction: transaction); // Dapper による非同期リーダー取得（トランザクション参加時は同一Tx上で実行）

                // 1. カラム名ヘッダーのレスポンス構築と送信
                var headerResp = new QueryResponse { Header = new ColumnHeader() };
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    headerResp.Header.Names.Add(reader.GetName(i)); // 列名追加
                }
                await responseStream.WriteAsync(headerResp); // ヘッダー送信

                // 2. データ行の非同期ストリーミング送信
                if (reader is DbDataReader dbDataReader) // DbDataReader 型キャスト判定
                {
                    while (await dbDataReader.ReadAsync(context.CancellationToken))
                    {
                        var rowResp = new QueryResponse { Row = new RowData() };
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var val = reader.GetValue(i); // フィールド値取得
                            rowResp.Row.Values.Add(FormatCellValue(val)); // BLOB/CLOB対応の文字列化（NULLガード込み）
                        }
                        await responseStream.WriteAsync(rowResp); // 行データ非同期送信
                    }
                }
            }
            finally
            {
                if (!useSession) // セッション利用時は BeginTransaction 側の所有物のため、ここでは破棄しない
                {
                    conn.Dispose();
                    tunnel?.Dispose();
                }
            }

            _logger.LogInformation("[INFO] DbServiceImpl.ExecuteQuery: クエリ送信が正常完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.ExecuteQuery: 例外発生 - {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// フィールド値を QueryResponse.Row.Values（文字列配列）向けへ変換します。
    /// バイト配列（BLOB/BINARY/VARBINARY列）は <see cref="BlobMarker"/> でマーカー付きBase64へ
    /// エンコードします（従来は byte[].ToString() が "System.Byte[]" という無意味な文字列になっていた不具合の修正）。
    /// </summary>
    /// <param name="val">[パラメータ] DbDataReader から取得したフィールド値を指定します。</param>
    /// <returns>文字列化されたフィールド値を返却します。</returns>
    private static string FormatCellValue(object? val)
    {
        if (val == null || val == DBNull.Value)
        {
            return string.Empty;
        }

        if (val is byte[] bytes)
        {
            return BlobMarker.Encode(bytes);
        }

        return val.ToString() ?? string.Empty;
    }

    /// <summary>
    /// SQL 文を単純な単一 SELECT 文（サブクエリとして安全にラップ可能）へ書き換え可能かどうかを判定します。
    /// セミコロンを含む（複数ステートメントの可能性がある）場合や、SELECT/WITH で始まらない場合、
    /// 既に LIMIT/OFFSET/FETCH/TOP を含む場合は、安全側に倒して対象外（false）とします。
    /// </summary>
    /// <param name="sql">[パラメータ] 判定対象の SQL 文字列を指定します。</param>
    /// <param name="trimmedSql">[出力パラメータ] 末尾のセミコロン・空白を除去した SQL を返却します。</param>
    /// <returns>ページング用サブクエリとして安全にラップ可能な場合に true を返却します。</returns>
    private static bool IsSimplePaginatableSelect(string sql, out string trimmedSql)
    {
        trimmedSql = (sql ?? string.Empty).Trim();
        if (trimmedSql.EndsWith(";", StringComparison.Ordinal))
        {
            trimmedSql = trimmedSql.Substring(0, trimmedSql.Length - 1).TrimEnd();
        }

        if (trimmedSql.Length == 0)
        {
            return false;
        }

        if (trimmedSql.Contains(';')) // 複数ステートメントの可能性を安全側で一律除外（文字列リテラル内含む）
        {
            return false;
        }

        if (!Regex.IsMatch(trimmedSql, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(trimmedSql, @"\b(LIMIT|OFFSET|FETCH\s+(FIRST|NEXT)|TOP\s*\(|ROWNUM)\b", RegexOptions.IgnoreCase)) // 既にページング指定済み（Oracle の FETCH FIRST/ROWNUM 含む）なら二重ラップしない
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// [1. 処理概要]
    /// SQL 文をサブクエリとして各 DBMS 方言の LIMIT/OFFSET（または OFFSET/FETCH）でラップし、
    /// サーバー側ページングを実現します。対象外と判定された場合は元の SQL をそのまま返却します。
    /// </summary>
    /// <param name="providerType">[パラメータ] プロバイダー種別を指定します。</param>
    /// <param name="sql">[パラメータ] 元の SQL 文を指定します。</param>
    /// <param name="pageSize">[パラメータ] 1ページあたりの取得件数を指定します。</param>
    /// <param name="pageOffset">[パラメータ] 取得開始オフセット（0始まり）を指定します。</param>
    /// <returns>ページング用にラップされた SQL 文（対象外の場合は元の SQL）を返却します。</returns>
    private static string BuildPaginatedSql(string providerType, string sql, int pageSize, int pageOffset)
    {
        if (!IsSimplePaginatableSelect(sql, out string trimmedSql))
        {
            return sql; // ページング対象外：安全側で元の SQL をそのまま返却（従来どおり全件取得）
        }

        switch (providerType.ToLower())
        {
            case "mssql":
            case "sqlserver":
            case "sql server":
            {
                // SQL Server はサブクエリ（派生テーブル）内の ORDER BY に TOP/OFFSET の併記が必須のため、
                // 内側クエリに ORDER BY が含まれる場合は "OFFSET 0 ROWS" を付与して有効化する
                string innerSql = Regex.IsMatch(trimmedSql, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase)
                    ? trimmedSql + " OFFSET 0 ROWS"
                    : trimmedSql;
                return $"SELECT * FROM ({innerSql}) AS neodb_page_src ORDER BY (SELECT NULL) OFFSET {pageOffset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
            }
            case "oracle":
                // Oracle はテーブルエイリアスへの AS キーワード付与を許容しないため付けない
                return $"SELECT * FROM ({trimmedSql}) neodb_page_src OFFSET {pageOffset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
            case "mysql":
            case "mariadb":
            case "postgresql":
            case "sqlite":
            default:
                return $"SELECT * FROM ({trimmedSql}) AS neodb_page_src LIMIT {pageSize} OFFSET {pageOffset}";
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// データベースのテーブル一覧、カラム詳細、主キー、外来キー関係を解析して SchemaResponse を返却します。
    /// </summary>
    /// <param name="request">[パラメータ] スキーマ取得要求パラメータ（Provider, ConnStr）を指定します。</param>
    /// <param name="context">[パラメータ] gRPC 呼び出しコンテキストを指定します。</param>
    /// <returns>テーブル・カラム・外部キー情報を格納した SchemaResponse を返却します。</returns>
    public override async Task<SchemaResponse> GetSchema(SchemaRequest request, ServerCallContext context)
    {
        try
        {
            if (request == null) // NULL 検証
            {
                throw new ArgumentNullException(nameof(request));
            }

            _logger.LogInformation($"[INFO] DbServiceImpl.GetSchema: スキーマ取得を開始します (Provider={request.ProviderType})");

            var response = new SchemaResponse();

            string connectionString = request.ConnectionString;
            using var tunnel = OpenSshTunnelIfNeeded(request.SshTunnel, ref connectionString, request.ProviderType); // 必要時のみ SSH ポートフォワードを確立

            // MongoDB / Redis は ADO.NET 非対応のため、リレーショナル経路（CreateConnection）へ入れずに専用経路で解析する
            if (IsNonRelationalProvider(request.ProviderType))
            {
                switch (request.ProviderType.ToLower())
                {
                    case "mongodb":
                        await LoadMongoSchemaAsync(connectionString, response, context.CancellationToken);
                        break;
                    case "redis":
                    default:
                        await LoadRedisSchemaAsync(connectionString, response);
                        break;
                }

                _logger.LogInformation($"[INFO] DbServiceImpl.GetSchema: DB スキーマ解析完了 (Provider={request.ProviderType}, Tables={response.Tables.Count}件, ForeignKeys={response.ForeignKeys.Count}件)");
                return response;
            }

            using var conn = CreateConnection(request.ProviderType, connectionString); // IDbConnection 生成
            await conn.OpenAsync(context.CancellationToken); // 非同期接続オープン

            switch (request.ProviderType.ToLower())
            {
                case "sqlite":
                    await LoadSqliteSchemaAsync(conn, response);
                    break;
                case "mysql":
                case "mariadb":
                    await LoadMySqlSchemaAsync(conn, response, connectionString);
                    break;
                case "mssql":
                case "sqlserver":
                case "sql server":
                    await LoadMsSqlSchemaAsync(conn, response);
                    break;
                case "oracle":
                    await LoadOracleSchemaAsync(conn, response);
                    break;
                case "postgresql":
                default:
                    await LoadPostgreSqlSchemaAsync(conn, response);
                    break;
            }

            _logger.LogInformation($"[INFO] DbServiceImpl.GetSchema: DB スキーマ解析完了 (Provider={request.ProviderType}, Tables={response.Tables.Count}件, ForeignKeys={response.ForeignKeys.Count}件)");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.GetSchema: 例外発生 - {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 接続・SSHトンネルを確立したままトランザクションを開始し、以後の ExecuteQuery で再利用できるセッションIDを発行します（オートコミットOFF時に使用）。
    /// </summary>
    public override async Task<BeginTransactionResponse> BeginTransaction(BeginTransactionRequest request, ServerCallContext context)
    {
        try
        {
            if (request == null) // NULL 検証
            {
                throw new ArgumentNullException(nameof(request));
            }

            _logger.LogInformation($"[INFO] DbServiceImpl.BeginTransaction: 開始します (Provider={request.ProviderType})");

            if (IsNonRelationalProvider(request.ProviderType)) // MongoDB / Redis は本サービスのトランザクション制御対象外
            {
                throw new NotSupportedException($"{request.ProviderType} はトランザクション（オートコミットOFF）に対応していません。オートコミットONでご利用ください。");
            }

            string connectionString = request.ConnectionString;
            var tunnel = OpenSshTunnelIfNeeded(request.SshTunnel, ref connectionString, request.ProviderType);

            DbConnection conn;
            DbTransaction transaction;
            try
            {
                conn = CreateConnection(request.ProviderType, connectionString);
                await conn.OpenAsync(context.CancellationToken);
                transaction = await conn.BeginTransactionAsync(context.CancellationToken);
            }
            catch
            {
                tunnel.Dispose(); // 接続/Tx開始に失敗した場合はトンネルも道連れに破棄
                throw;
            }

            string sessionId = _sessionManager.CreateSession(conn, transaction, tunnel);

            _logger.LogInformation($"[INFO] DbServiceImpl.BeginTransaction: トランザクション開始完了 (SessionId={sessionId})");
            return new BeginTransactionResponse { Success = true, SessionId = sessionId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.BeginTransaction: 例外発生 - {ex.Message}");
            return new BeginTransactionResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 指定セッションのトランザクションをコミットし、保持していた接続・トンネルを解放します。
    /// </summary>
    public override Task<TransactionAck> CommitTransaction(TransactionSessionRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"[INFO] DbServiceImpl.CommitTransaction: 開始します (SessionId={request.SessionId})");
            bool found = _sessionManager.EndSession(request.SessionId, commit: true);
            return Task.FromResult(found
                ? new TransactionAck { Success = true }
                : new TransactionAck { Success = false, ErrorMessage = "指定されたトランザクションセッションが見つかりません。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.CommitTransaction: 例外発生 - {ex.Message}");
            return Task.FromResult(new TransactionAck { Success = false, ErrorMessage = ex.Message });
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 指定セッションのトランザクションをロールバックし、保持していた接続・トンネルを解放します。
    /// </summary>
    public override Task<TransactionAck> RollbackTransaction(TransactionSessionRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"[INFO] DbServiceImpl.RollbackTransaction: 開始します (SessionId={request.SessionId})");
            bool found = _sessionManager.EndSession(request.SessionId, commit: false);
            return Task.FromResult(found
                ? new TransactionAck { Success = true }
                : new TransactionAck { Success = false, ErrorMessage = "指定されたトランザクションセッションが見つかりません。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.RollbackTransaction: 例外発生 - {ex.Message}");
            return Task.FromResult(new TransactionAck { Success = false, ErrorMessage = ex.Message });
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// ストアドプロシージャのデバッグ実行シミュレーション（双方向ストリーミング RPC）を提供します。
    /// </summary>
    public override async Task DebugProcedure(
        IAsyncStreamReader<DebugCommand> requestStream, 
        IServerStreamWriter<DebugEvent> responseStream, 
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("[INFO] DbServiceImpl.DebugProcedure: デバッグセッションを開始します");

            while (await requestStream.MoveNext(context.CancellationToken))
            {
                var cmd = requestStream.Current; // 受信コマンド

                if (cmd.Action == DebugAction.ActionStart) // デバッグ開始時
                {
                    await responseStream.WriteAsync(new DebugEvent
                    {
                        EventType  = DebugEventType.EventLineHit,
                        LineNumber = 3,
                        Message    = "Step: Initializing Procedure Variables"
                    });
                }
                else if (cmd.Action == DebugAction.ActionStepOver) // ステップ実行時
                {
                    await responseStream.WriteAsync(new DebugEvent
                    {
                        EventType  = DebugEventType.EventLineHit,
                        LineNumber = 6,
                        Message    = "Step: Executing SELECT Query"
                    });
                }
                else if (cmd.Action == DebugAction.ActionStop) // 停止時
                {
                    break; // ループ脱出
                }
            }

            _logger.LogInformation("[INFO] DbServiceImpl.DebugProcedure: デバッグセッションが正常終了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.DebugProcedure: 例外発生 - {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    #endregion

    #region Private Non-Relational Query Execution

    /// <summary>
    /// [1. 処理概要]
    /// MongoDB / Redis に対するクエリを実行し、リレーショナル DBMS と同一の
    /// ヘッダー＋行データ形式（ColumnHeader / RowData）でストリーミング返却します。
    ///
    /// [2. 処理フロー]
    /// 1. SSH トンネルが有効な場合はポートフォワードを確立します。
    /// 2. プロバイダー種別に応じて MongoDB / Redis の各実行処理へ委譲します。
    /// </summary>
    /// <param name="request">[パラメータ] 実行要求パラメータを指定します。</param>
    /// <param name="responseStream">[パラメータ] クライアントへレスポンスを送るストリームを指定します。</param>
    /// <param name="context">[パラメータ] gRPC 呼び出しコンテキストを指定します。</param>
    /// <returns>非同期タスク Task を返却します。</returns>
    private async Task ExecuteNonRelationalQueryAsync(
        QueryRequest request,
        IServerStreamWriter<QueryResponse> responseStream,
        ServerCallContext context)
    {
        string connectionString = request.ConnectionString;
        using var tunnel = OpenSshTunnelIfNeeded(request.SshTunnel, ref connectionString, request.ProviderType);

        switch (request.ProviderType.ToLower())
        {
            case "mongodb":
                await ExecuteMongoQueryAsync(connectionString, request.Sql, responseStream, context.CancellationToken);
                break;
            case "redis":
            default:
                await ExecuteRedisCommandAsync(connectionString, request.Sql, responseStream);
                break;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// MongoDB のコレクション検索を実行し、取得ドキュメントを表形式へ射影してストリーミング返却します。
    ///
    /// [2. 処理フロー]
    /// 1. クエリ文字列を解析し、コレクション名・フィルタ条件・取得件数上限を抽出します。
    /// 2. 対象コレクションを検索し、結果ドキュメントを一旦バッファします。
    /// 3. 全ドキュメントのフィールド名の和集合を列ヘッダーとして送信した後、各ドキュメントを行データとして送信します。
    /// </summary>
    /// <param name="connectionString">[パラメータ] MongoDB 接続文字列を指定します。</param>
    /// <param name="query">[パラメータ] 実行クエリ文字列を指定します（例: db.users.find({}).limit(100)）。</param>
    /// <param name="responseStream">[パラメータ] クライアントへレスポンスを送るストリームを指定します。</param>
    /// <param name="cancellationToken">[パラメータ] キャンセルトークンを指定します。</param>
    /// <returns>非同期タスク Task を返却します。</returns>
    private async Task ExecuteMongoQueryAsync(
        string connectionString,
        string query,
        IServerStreamWriter<QueryResponse> responseStream,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) // 空クエリガード
        {
            throw new ArgumentException("MongoDB のクエリが指定されていません。例: db.users.find({}).limit(100)");
        }

        // 受理する書式: [db.]<コレクション名>[.find(<フィルタJSON>)][.limit(<件数>)]
        var match = Regex.Match(
            query.Trim(),
            @"^(?:db\s*\.\s*)?(?<collection>[A-Za-z0-9_\-]+(?:\.[A-Za-z0-9_\-]+)*?)\s*(?:\.\s*find\s*\(\s*(?<filter>\{.*\})?\s*\))?\s*(?:\.\s*limit\s*\(\s*(?<limit>\d+)\s*\))?\s*;?$",
            RegexOptions.Singleline);

        if (!match.Success) // 解釈できない書式は利用方法を明示して拒否する
        {
            throw new ArgumentException("MongoDB のクエリ書式を解釈できません。例: db.users.find({ \"age\": { \"$gt\": 20 } }).limit(100)");
        }

        string collectionPath = match.Groups["collection"].Value;
        string filterJson     = match.Groups["filter"].Success ? match.Groups["filter"].Value : "{}";
        int fetchLimit        = match.Groups["limit"].Success
            ? int.Parse(match.Groups["limit"].Value)
            : DefaultDocumentFetchLimit;

        var url    = new MongoUrl(connectionString);
        var client = new MongoClient(connectionString);

        // "DB名.コレクション名" 形式で指定された場合は接続文字列の DB 指定より優先する
        string databaseName;
        string collectionName;
        int lastSeparator = collectionPath.LastIndexOf('.');
        if (lastSeparator > 0)
        {
            databaseName   = collectionPath.Substring(0, lastSeparator);
            collectionName = collectionPath.Substring(lastSeparator + 1);
        }
        else
        {
            databaseName   = url.DatabaseName ?? string.Empty;
            collectionName = collectionPath;
        }

        if (string.IsNullOrEmpty(databaseName)) // DB 名が特定できない場合はガード
        {
            throw new InvalidOperationException("対象データベースを特定できません。接続文字列に DB 名を含めるか、db.<DB名>.<コレクション名> 形式で指定してください。");
        }

        var collection = client.GetDatabase(databaseName).GetCollection<BsonDocument>(collectionName);
        var filter     = BsonDocument.Parse(filterJson);

        var documents = await collection.Find(filter)
                                        .Limit(fetchLimit)
                                        .ToListAsync(cancellationToken);

        // 全ドキュメントのフィールド名の和集合を、初出順を保ったまま列として構築する
        var columnNames = new List<string>();
        var columnSeen  = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var element in document.Elements)
            {
                if (columnSeen.Add(element.Name))
                {
                    columnNames.Add(element.Name);
                }
            }
        }

        var headerResp = new QueryResponse { Header = new ColumnHeader() };
        headerResp.Header.Names.AddRange(columnNames);
        await responseStream.WriteAsync(headerResp);

        foreach (var document in documents)
        {
            var rowResp = new QueryResponse { Row = new RowData() };
            foreach (var columnName in columnNames)
            {
                // 当該フィールドを持たないドキュメントは空文字として整列させる（スキーマレス対応）
                rowResp.Row.Values.Add(document.TryGetValue(columnName, out var value)
                    ? ConvertBsonValueToText(value)
                    : string.Empty);
            }
            await responseStream.WriteAsync(rowResp);
        }
    }

    /// <summary>
    /// BSON 値を表示用テキストへ変換します。ドキュメント・配列は JSON 文字列として返却します。
    /// </summary>
    /// <param name="value">[パラメータ] 変換対象の BSON 値を指定します。</param>
    /// <returns>表示用テキストを返却します。</returns>
    private static string ConvertBsonValueToText(BsonValue value)
    {
        if (value == null || value.IsBsonNull) // NULL ガード
        {
            return string.Empty;
        }

        if (value.IsBsonDocument || value.IsBsonArray) // 入れ子構造は JSON 文字列として可視化する
        {
            return value.ToJson();
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// [1. 処理概要]
    /// Redis コマンドを実行し、結果を表形式へ射影してストリーミング返却します。
    ///
    /// [2. 処理フロー]
    /// 1. コマンド文字列を（引用符を考慮して）コマンド名と引数へ分解します。
    /// 2. コマンドを実行し、配列応答は 1 要素 1 行、単一応答は 1 行として送信します。
    /// </summary>
    /// <param name="connectionString">[パラメータ] Redis 接続文字列を指定します。</param>
    /// <param name="command">[パラメータ] 実行する Redis コマンド文字列を指定します（例: GET user:1）。</param>
    /// <param name="responseStream">[パラメータ] クライアントへレスポンスを送るストリームを指定します。</param>
    /// <returns>非同期タスク Task を返却します。</returns>
    private async Task ExecuteRedisCommandAsync(
        string connectionString,
        string command,
        IServerStreamWriter<QueryResponse> responseStream)
    {
        if (string.IsNullOrWhiteSpace(command)) // 空コマンドガード
        {
            throw new ArgumentException("Redis のコマンドが指定されていません。例: KEYS *");
        }

        var tokens = TokenizeRedisCommand(command);
        if (tokens.Count == 0) // 分解不能ガード
        {
            throw new ArgumentException("Redis のコマンドを解釈できませんでした。");
        }

        var options        = ConfigurationOptions.Parse(connectionString);
        options.AllowAdmin = true; // KEYS / INFO 等の管理系コマンドを許可

        using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var database          = multiplexer.GetDatabase();

        string commandName = tokens[0];
        object[] arguments = tokens.Skip(1).Cast<object>().ToArray();

        var result = await database.ExecuteAsync(commandName, arguments);

        if (result.Resp2Type == ResultType.Array) // 配列応答は 1 要素 1 行で返却する
        {
            var headerResp = new QueryResponse { Header = new ColumnHeader() };
            headerResp.Header.Names.Add("#");
            headerResp.Header.Names.Add("Value");
            await responseStream.WriteAsync(headerResp);

            var values = (RedisValue[]?)result ?? Array.Empty<RedisValue>();
            for (int i = 0; i < values.Length; i++)
            {
                var rowResp = new QueryResponse { Row = new RowData() };
                rowResp.Row.Values.Add((i + 1).ToString());
                rowResp.Row.Values.Add(values[i].ToString() ?? string.Empty);
                await responseStream.WriteAsync(rowResp);
            }
            return;
        }

        var singleHeader = new QueryResponse { Header = new ColumnHeader() };
        singleHeader.Header.Names.Add("Result");
        await responseStream.WriteAsync(singleHeader);

        var singleRow = new QueryResponse { Row = new RowData() };
        singleRow.Row.Values.Add(result.IsNull ? string.Empty : result.ToString() ?? string.Empty);
        await responseStream.WriteAsync(singleRow);
    }

    /// <summary>
    /// Redis コマンド文字列を、二重引用符で囲まれた引数を 1 トークンとして扱いながら分解します。
    /// </summary>
    /// <param name="command">[パラメータ] 分解対象のコマンド文字列を指定します。</param>
    /// <returns>分解されたトークンのリストを返却します。</returns>
    private static List<string> TokenizeRedisCommand(string command)
    {
        var tokens  = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;

        foreach (char c in command.Trim())
        {
            if (c == '"') // 引用符の開始・終了を切り替える
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && char.IsWhiteSpace(c)) // 引用符外の空白はトークン区切り
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) // 末尾トークンの取りこぼし防止
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    #endregion

    #region Private Connection Factory

    /// <summary>
    /// DBMS プロバイダー種別に応じた DbConnection インスタンスを生成して返却します。
    /// </summary>
    private DbConnection CreateConnection(string providerType, string connectionString)
    {
        try
        {
            _logger.LogInformation($"[INFO] DbServiceImpl.CreateConnection: {providerType} 接続を生成します");

            switch (providerType.ToLower())
            {
                case "sqlite":
                    return new SqliteConnection(connectionString);
                case "mysql":
                case "mariadb":
                    return new MySqlConnection(connectionString);
                case "mssql":
                case "sqlserver":
                case "sql server":
                    return new SqlConnection(connectionString);
                case "oracle":
                    return new OracleConnection(connectionString);
                case "mongodb":
                case "redis":
                    // 非リレーショナル DBMS は ADO.NET の DbConnection を持たない。呼び出し側の経路誤りを明示的に検出する
                    throw new NotSupportedException($"{providerType} は ADO.NET 接続に対応していません。専用経路で処理してください。");
                case "postgresql":
                default:
                    return new NpgsqlConnection(connectionString);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.CreateConnection: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 指定プロバイダーが非リレーショナル（ADO.NET 非対応）DBMS であるかを判定します。
    /// </summary>
    /// <param name="providerType">[パラメータ] プロバイダー種別文字列を指定します。</param>
    /// <returns>MongoDB または Redis の場合に true を返却します。</returns>
    private static bool IsNonRelationalProvider(string? providerType)
    {
        if (string.IsNullOrWhiteSpace(providerType)) // NULL・空文字ガード
        {
            return false;
        }

        switch (providerType.ToLower())
        {
            case "mongodb":
            case "redis":
                return true;
            default:
                return false;
        }
    }

    #endregion

    #region Private SSH Tunnel

    /// <summary>
    /// SSH クライアント・ポートフォワードのライフサイクルを保持する破棄用ハンドル（未使用時は何もしない no-op）。
    /// </summary>
    private sealed class SshTunnelHandle : IDisposable
    {
        public SshClient? Client { get; init; }
        public ForwardedPortLocal? Port { get; init; }

        public void Dispose()
        {
            try { Port?.Stop(); } catch { /* 停止失敗は無視（既に切断済み等） */ }
            try { Client?.Disconnect(); } catch { /* 切断失敗は無視 */ }
            Client?.Dispose();
        }
    }

    /// <summary>
    /// SshTunnelConfig が有効な場合のみ SSH 接続とローカルポートフォワードを確立し、
    /// connectionString の接続先をフォワード済みローカルポートへ書き換えます。
    /// </summary>
    private SshTunnelHandle OpenSshTunnelIfNeeded(SshTunnelConfig? cfg, ref string connectionString, string providerType)
    {
        if (cfg == null || !cfg.Enabled || string.Equals(providerType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return new SshTunnelHandle(); // 無効時は no-op ハンドルを返す
        }

        try
        {
            _logger.LogInformation($"[INFO] DbServiceImpl.OpenSshTunnelIfNeeded: SSHトンネルを確立します ({cfg.Username}@{cfg.Host}:{cfg.Port} → {cfg.RemoteHost}:{cfg.RemotePort})");

            AuthenticationMethod authMethod;
            if (string.Equals(cfg.AuthType, "key", StringComparison.OrdinalIgnoreCase))
            {
                var keyFile = string.IsNullOrEmpty(cfg.Passphrase)
                    ? new PrivateKeyFile(cfg.PrivateKeyPath)
                    : new PrivateKeyFile(cfg.PrivateKeyPath, cfg.Passphrase);
                authMethod = new PrivateKeyAuthenticationMethod(cfg.Username, keyFile);
            }
            else
            {
                authMethod = new PasswordAuthenticationMethod(cfg.Username, cfg.Password);
            }

            var connInfo = new Renci.SshNet.ConnectionInfo(cfg.Host, cfg.Port > 0 ? cfg.Port : 22, cfg.Username, authMethod);
            var sshClient = new SshClient(connInfo);
            sshClient.Connect();

            var forwardedPort = new ForwardedPortLocal("127.0.0.1", 0, cfg.RemoteHost, (uint)cfg.RemotePort);
            sshClient.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            uint localPort = forwardedPort.BoundPort;
            connectionString = RewriteConnectionStringForTunnel(providerType, connectionString, localPort);

            _logger.LogInformation($"[INFO] DbServiceImpl.OpenSshTunnelIfNeeded: トンネル確立完了 (127.0.0.1:{localPort} → {cfg.RemoteHost}:{cfg.RemotePort} via {cfg.Host})");

            return new SshTunnelHandle { Client = sshClient, Port = forwardedPort };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[ERROR] DbServiceImpl.OpenSshTunnelIfNeeded: SSHトンネル確立に失敗しました - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// プロバイダー種別に応じた接続文字列ビルダーで Server/Host および Port をフォワード済みローカルポートへ書き換えます。
    /// </summary>
    private string RewriteConnectionStringForTunnel(string providerType, string connectionString, uint localPort)
    {
        switch (providerType.ToLower())
        {
            case "mysql":
            case "mariadb":
            {
                var b = new MySqlConnectionStringBuilder(connectionString) { Server = "127.0.0.1", Port = localPort };
                return b.ConnectionString;
            }
            case "mssql":
            case "sqlserver":
            case "sql server":
            {
                var b = new SqlConnectionStringBuilder(connectionString) { DataSource = $"127.0.0.1,{localPort}" };
                return b.ConnectionString;
            }
            case "oracle":
            {
                return RewriteOracleDataSourceForTunnel(connectionString, localPort);
            }
            case "mongodb":
            {
                return RewriteHostPortAuthorityForTunnel(connectionString, localPort);
            }
            case "redis":
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.EndPoints.Clear();
                options.EndPoints.Add("127.0.0.1", (int)localPort);
                return options.ToString();
            }
            case "postgresql":
            default:
            {
                var b = new NpgsqlConnectionStringBuilder(connectionString) { Host = "127.0.0.1", Port = (int)localPort };
                return b.ConnectionString;
            }
        }
    }

    /// <summary>
    /// Oracle 接続文字列の接続先を、フォワード済みローカルポートへ書き換えます。
    /// TNS 記述子形式（(HOST=...)(PORT=...)）と EZ-Connect 形式（host:port/service）の双方に対応します。
    /// </summary>
    /// <param name="connectionString">[パラメータ] 書き換え対象の Oracle 接続文字列を指定します。</param>
    /// <param name="localPort">[パラメータ] フォワード済みローカルポート番号を指定します。</param>
    /// <returns>接続先を書き換えた Oracle 接続文字列を返却します。</returns>
    private string RewriteOracleDataSourceForTunnel(string connectionString, uint localPort)
    {
        var builder    = new OracleConnectionStringBuilder(connectionString);
        string dataSrc = builder.DataSource ?? string.Empty;

        if (dataSrc.IndexOf("(HOST", StringComparison.OrdinalIgnoreCase) >= 0) // TNS 記述子形式
        {
            dataSrc = Regex.Replace(dataSrc, @"(?<=\(\s*HOST\s*=\s*)[^)]+", "127.0.0.1", RegexOptions.IgnoreCase);
            dataSrc = Regex.Replace(dataSrc, @"(?<=\(\s*PORT\s*=\s*)[^)]+", localPort.ToString(), RegexOptions.IgnoreCase);
        }
        else // EZ-Connect 形式（host:port/service_name または host/service_name）
        {
            int slashIndex  = dataSrc.IndexOf('/');
            string service  = slashIndex >= 0 ? dataSrc.Substring(slashIndex) : string.Empty;
            dataSrc         = $"127.0.0.1:{localPort}{service}";
        }

        builder.DataSource = dataSrc;
        return builder.ConnectionString;
    }

    /// <summary>
    /// URI 形式の接続文字列（mongodb://user:pass@host:port/db 等）のホスト・ポート部のみを
    /// フォワード済みローカルポートへ書き換えます。認証情報・データベース名・クエリ文字列は保持します。
    /// </summary>
    /// <param name="connectionString">[パラメータ] 書き換え対象の URI 形式接続文字列を指定します。</param>
    /// <param name="localPort">[パラメータ] フォワード済みローカルポート番号を指定します。</param>
    /// <returns>接続先を書き換えた接続文字列を返却します。</returns>
    private string RewriteHostPortAuthorityForTunnel(string connectionString, uint localPort)
    {
        // scheme://[credentials@]host[:port][/rest] の host[:port] のみを置換する
        var match = Regex.Match(connectionString, @"^(?<prefix>[a-zA-Z0-9\+\.\-]+://(?:[^@/]*@)?)(?<authority>[^/?]+)(?<rest>.*)$");
        if (!match.Success) // 想定外の書式は書き換えず原文のまま返す（誤変換防止）
        {
            _logger.LogWarning($"[WARNING] DbServiceImpl.RewriteHostPortAuthorityForTunnel: 接続文字列の書式を解釈できないため書き換えを行いません");
            return connectionString;
        }

        return $"{match.Groups["prefix"].Value}127.0.0.1:{localPort}{match.Groups["rest"].Value}";
    }

    #endregion

    #region Private Schema Introspection

    /// <summary>
    /// MySQL / MariaDB の information_schema からテーブル・カラム・主キー・外部キーを取得し response へ格納します。
    /// 接続文字列に Database 指定が無い場合（例: マルチテナントDBをroot権限で横断接続）は、
    /// 全ユーザースキーマを横断し、テーブル名を "スキーマ名.テーブル名" 形式で返却して衝突を回避します。
    /// </summary>
    private async Task LoadMySqlSchemaAsync(DbConnection conn, SchemaResponse response, string connectionString)
    {
        bool hasDatabase = !string.IsNullOrEmpty(new MySqlConnectionStringBuilder(connectionString).Database);

        // BASE TABLE のみを対象とする（VIEW は下記の別クエリで Views フォルダへ振り分ける。
        // 従来は TABLE_TYPE を判定していなかったため VIEW の列も Tables 側に混入していた）
        string columnsSql = hasDatabase
            ? @"SELECT c.TABLE_SCHEMA AS TableSchemaName, c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName,
                       c.COLUMN_TYPE AS DataType, c.IS_NULLABLE AS IsNullable, c.COLUMN_KEY AS ColumnKey
                FROM information_schema.COLUMNS c
                JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE c.TABLE_SCHEMA = DATABASE() AND t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;"
            : @"SELECT c.TABLE_SCHEMA AS TableSchemaName, c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName,
                       c.COLUMN_TYPE AS DataType, c.IS_NULLABLE AS IsNullable, c.COLUMN_KEY AS ColumnKey
                FROM information_schema.COLUMNS c
                JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE c.TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                      AND t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;";

        string viewColumnsSql = hasDatabase
            ? @"SELECT c.TABLE_SCHEMA AS TableSchemaName, c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName,
                       c.COLUMN_TYPE AS DataType, c.IS_NULLABLE AS IsNullable
                FROM information_schema.COLUMNS c
                JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE c.TABLE_SCHEMA = DATABASE() AND t.TABLE_TYPE = 'VIEW'
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;"
            : @"SELECT c.TABLE_SCHEMA AS TableSchemaName, c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName,
                       c.COLUMN_TYPE AS DataType, c.IS_NULLABLE AS IsNullable
                FROM information_schema.COLUMNS c
                JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE c.TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                      AND t.TABLE_TYPE = 'VIEW'
                ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;";

        string routinesSql = hasDatabase
            ? @"SELECT ROUTINE_NAME AS Name, ROUTINE_TYPE AS RoutineType
                FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA = DATABASE()
                ORDER BY ROUTINE_NAME;"
            : @"SELECT ROUTINE_NAME AS Name, ROUTINE_TYPE AS RoutineType
                FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";

        string fkSql = hasDatabase
            ? @"SELECT CONSTRAINT_NAME AS ConstraintName, TABLE_SCHEMA AS FkSchemaName, TABLE_NAME AS FkTable, COLUMN_NAME AS FkColumn,
                       REFERENCED_TABLE_SCHEMA AS PkSchemaName, REFERENCED_TABLE_NAME AS PkTable, REFERENCED_COLUMN_NAME AS PkColumn
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL;"
            : @"SELECT CONSTRAINT_NAME AS ConstraintName, TABLE_SCHEMA AS FkSchemaName, TABLE_NAME AS FkTable, COLUMN_NAME AS FkColumn,
                       REFERENCED_TABLE_SCHEMA AS PkSchemaName, REFERENCED_TABLE_NAME AS PkTable, REFERENCED_COLUMN_NAME AS PkColumn
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                      AND REFERENCED_TABLE_NAME IS NOT NULL;";

        var columnRows = (await conn.QueryAsync<RawColumnRow>(columnsSql)).ToList();
        if (!hasDatabase)
        {
            foreach (var row in columnRows)
            {
                row.TableName = $"{row.TableSchemaName}.{row.TableName}"; // スキーマ横断時は衝突回避のためスキーマ名を付与
            }
        }
        BuildTablesFromRows(response, columnRows, c => string.Equals(c.ColumnKey, "PRI", StringComparison.OrdinalIgnoreCase));

        var viewColumnRows = (await conn.QueryAsync<RawColumnRow>(viewColumnsSql)).ToList();
        if (!hasDatabase)
        {
            foreach (var row in viewColumnRows)
            {
                row.TableName = $"{row.TableSchemaName}.{row.TableName}";
            }
        }
        BuildViewsFromRows(response, viewColumnRows);

        var routineRows = await conn.QueryAsync<RawRoutineRow>(routinesSql);
        AppendProcedures(response, routineRows);

        // インデックス一覧（PRIMARYは既にカラムの🔑マーカーで表現済みのため除外し重複表示を避ける）
        string indexSql = hasDatabase
            ? @"SELECT TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName,
                       CASE WHEN NON_UNIQUE = 0 THEN '1' ELSE '0' END AS IsUniqueFlag, SEQ_IN_INDEX AS SeqInIndex
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME <> 'PRIMARY'
                ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;"
            : @"SELECT TABLE_SCHEMA AS TableSchemaForIndex, TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName,
                       CASE WHEN NON_UNIQUE = 0 THEN '1' ELSE '0' END AS IsUniqueFlag, SEQ_IN_INDEX AS SeqInIndex
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys') AND INDEX_NAME <> 'PRIMARY'
                ORDER BY TABLE_SCHEMA, TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;";

        var indexRows = (await conn.QueryAsync<RawIndexRowWithSchema>(indexSql)).ToList();
        if (!hasDatabase)
        {
            foreach (var idxRow in indexRows)
            {
                idxRow.TableName = $"{idxRow.TableSchemaForIndex}.{idxRow.TableName}";
            }
        }
        AppendIndexesToTables(response, indexRows);

        var fkRows = (await conn.QueryAsync<RawForeignKeyRow>(fkSql)).ToList();
        if (!hasDatabase)
        {
            foreach (var fk in fkRows)
            {
                fk.FkTable = $"{fk.FkSchemaName}.{fk.FkTable}";
                fk.PkTable = $"{fk.PkSchemaName}.{fk.PkTable}";
            }
        }
        AppendForeignKeys(response, fkRows);
    }

    /// <summary>
    /// PostgreSQL の information_schema からテーブル・カラム・主キー・外部キーを取得し response へ格納します。
    /// </summary>
    private async Task LoadPostgreSqlSchemaAsync(DbConnection conn, SchemaResponse response)
    {
        // BASE TABLE のみを対象とする（VIEW は下記の別クエリで Views フォルダへ振り分ける）
        const string columnsSql = @"
            SELECT c.table_name  AS TableName,
                   c.column_name AS ColumnName,
                   c.data_type   AS DataType,
                   c.is_nullable AS IsNullable
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE'
            ORDER BY c.table_name, c.ordinal_position;";

        const string viewColumnsSql = @"
            SELECT c.table_name  AS TableName,
                   c.column_name AS ColumnName,
                   c.data_type   AS DataType,
                   c.is_nullable AS IsNullable
            FROM information_schema.columns c
            JOIN information_schema.views v ON v.table_schema = c.table_schema AND v.table_name = c.table_name
            WHERE c.table_schema = 'public'
            ORDER BY c.table_name, c.ordinal_position;";

        const string routinesSql = @"
            SELECT routine_name AS Name, routine_type AS RoutineType
            FROM information_schema.routines
            WHERE routine_schema = 'public'
            ORDER BY routine_name;";

        const string pkSql = @"
            SELECT tc.table_name AS TableName, kcu.column_name AS ColumnName
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = 'public';";

        const string fkSql = @"
            SELECT tc.constraint_name AS ConstraintName,
                   kcu.table_name AS FkTable, kcu.column_name AS FkColumn,
                   ccu.table_name AS PkTable, ccu.column_name AS PkColumn
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'public';";

        var columnRows = (await conn.QueryAsync<RawColumnRow>(columnsSql)).ToList();
        var pkSet = new HashSet<string>(
            (await conn.QueryAsync<RawPrimaryKeyRow>(pkSql)).Select(p => $"{p.TableName}.{p.ColumnName}"));

        BuildTablesFromRows(response, columnRows, c => pkSet.Contains($"{c.TableName}.{c.ColumnName}"));

        var viewColumnRows = await conn.QueryAsync<RawColumnRow>(viewColumnsSql);
        BuildViewsFromRows(response, viewColumnRows);

        var routineRows = await conn.QueryAsync<RawRoutineRow>(routinesSql);
        AppendProcedures(response, routineRows);

        const string indexSql = @"
            SELECT t.relname AS TableName, i.relname AS IndexName, a.attname AS ColumnName,
                   CASE WHEN ix.indisunique THEN '1' ELSE '0' END AS IsUniqueFlag,
                   array_position(ix.indkey, a.attnum) AS SeqInIndex
            FROM pg_class t
            JOIN pg_index ix ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'public' AND t.relkind = 'r' AND NOT ix.indisprimary
            ORDER BY t.relname, i.relname, SeqInIndex;";
        var indexRows = await conn.QueryAsync<RawIndexRow>(indexSql);
        AppendIndexesToTables(response, indexRows);

        var fkRows = await conn.QueryAsync<RawForeignKeyRow>(fkSql);
        AppendForeignKeys(response, fkRows);
    }

    /// <summary>
    /// SQL Server の INFORMATION_SCHEMA / sys カタログからテーブル・カラム・主キー・外部キーを取得し response へ格納します。
    /// </summary>
    private async Task LoadMsSqlSchemaAsync(DbConnection conn, SchemaResponse response)
    {
        // BASE TABLE のみを対象とする（VIEW は下記の別クエリで Views フォルダへ振り分ける）
        const string columnsSql = @"
            SELECT c.TABLE_NAME  AS TableName,
                   c.COLUMN_NAME AS ColumnName,
                   c.DATA_TYPE   AS DataType,
                   c.IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;";

        const string viewColumnsSql = @"
            SELECT c.TABLE_NAME  AS TableName,
                   c.COLUMN_NAME AS ColumnName,
                   c.DATA_TYPE   AS DataType,
                   c.IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'VIEW'
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;";

        const string routinesSql = @"
            SELECT ROUTINE_NAME AS Name, ROUTINE_TYPE AS RoutineType
            FROM INFORMATION_SCHEMA.ROUTINES
            ORDER BY ROUTINE_NAME;";

        const string pkSql = @"
            SELECT ku.TABLE_NAME AS TableName, ku.COLUMN_NAME AS ColumnName
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY';";

        const string fkSql = @"
            SELECT fk.name AS ConstraintName,
                   OBJECT_NAME(fk.parent_object_id)     AS FkTable,
                   cfk.name                              AS FkColumn,
                   OBJECT_NAME(fk.referenced_object_id) AS PkTable,
                   cpk.name                              AS PkColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns cpk ON cpk.object_id = fkc.referenced_object_id AND cpk.column_id = fkc.referenced_column_id
            JOIN sys.columns cfk ON cfk.object_id = fkc.parent_object_id AND cfk.column_id = fkc.parent_column_id;";

        var columnRows = (await conn.QueryAsync<RawColumnRow>(columnsSql)).ToList();
        var pkSet = new HashSet<string>(
            (await conn.QueryAsync<RawPrimaryKeyRow>(pkSql)).Select(p => $"{p.TableName}.{p.ColumnName}"));

        BuildTablesFromRows(response, columnRows, c => pkSet.Contains($"{c.TableName}.{c.ColumnName}"));

        var viewColumnRows = await conn.QueryAsync<RawColumnRow>(viewColumnsSql);
        BuildViewsFromRows(response, viewColumnRows);

        var routineRows = await conn.QueryAsync<RawRoutineRow>(routinesSql);
        AppendProcedures(response, routineRows);

        const string indexSql = @"
            SELECT OBJECT_NAME(ic.object_id) AS TableName, i.name AS IndexName, c.name AS ColumnName,
                   CASE WHEN i.is_unique = 1 THEN '1' ELSE '0' END AS IsUniqueFlag, ic.key_ordinal AS SeqInIndex
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 0 AND i.name IS NOT NULL
            ORDER BY TableName, i.name, ic.key_ordinal;";
        var indexRows = await conn.QueryAsync<RawIndexRow>(indexSql);
        AppendIndexesToTables(response, indexRows);

        var fkRows = await conn.QueryAsync<RawForeignKeyRow>(fkSql);
        AppendForeignKeys(response, fkRows);
    }

    /// <summary>
    /// SQLite の sqlite_master / PRAGMA からテーブル・カラム・主キー・外部キーを取得し response へ格納します。
    /// </summary>
    private async Task LoadSqliteSchemaAsync(DbConnection conn, SchemaResponse response)
    {
        const string tablesSql = @"
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;";

        var tableNames = await conn.QueryAsync<string>(tablesSql);

        foreach (var tableName in tableNames)
        {
            var safeName = tableName.Replace("'", "''"); // PRAGMA はバインドパラメータ非対応のためエスケープ
            var table = new TableSchema { Name = tableName };
            response.Tables.Add(table);

            var columnRows = await conn.QueryAsync<SqliteColumnRow>($"PRAGMA table_info('{safeName}');");
            foreach (var col in columnRows)
            {
                table.Columns.Add(new ColumnSchema
                {
                    Name         = col.Name ?? string.Empty,
                    DataType     = col.Type ?? string.Empty,
                    IsPrimaryKey = col.Pk > 0,
                    IsNullable   = col.NotNull == 0
                });
            }

            var fkRows = await conn.QueryAsync<SqliteForeignKeyRow>($"PRAGMA foreign_key_list('{safeName}');");
            foreach (var fk in fkRows)
            {
                response.ForeignKeys.Add(new ForeignKeySchema
                {
                    ConstraintName = $"fk_{tableName}_{fk.Id}",
                    PkTable        = fk.Table ?? string.Empty,
                    PkColumn       = fk.To ?? string.Empty,
                    FkTable        = tableName,
                    FkColumn       = fk.From ?? string.Empty
                });
            }

            // インデックス一覧（origin='pk' は主キー由来の自動生成インデックスのため除外し、🔑マーカーとの重複を避ける）
            var indexList = await conn.QueryAsync<SqliteIndexListRow>($"PRAGMA index_list('{safeName}');");
            foreach (var idxInfo in indexList.Where(i => !string.Equals(i.Origin, "pk", StringComparison.OrdinalIgnoreCase)))
            {
                var safeIndexName = idxInfo.Name.Replace("'", "''");
                var indexColumns = await conn.QueryAsync<SqliteIndexColumnRow>($"PRAGMA index_info('{safeIndexName}');");

                table.Indexes.Add(new IndexSchema
                {
                    Name     = idxInfo.Name,
                    IsUnique = idxInfo.Unique == 1,
                    Columns  = { indexColumns.OrderBy(c => c.Seqno).Select(c => c.Name ?? string.Empty) }
                });
            }
        }

        // ビュー一覧（SQLite にストアドプロシージャの概念は無いため Procedures は常に空のままとなる）
        const string viewsSql = @"
            SELECT name FROM sqlite_master
            WHERE type = 'view'
            ORDER BY name;";

        var viewNames = await conn.QueryAsync<string>(viewsSql);
        foreach (var viewName in viewNames)
        {
            var safeName = viewName.Replace("'", "''");
            var view = new TableSchema { Name = viewName };
            response.Views.Add(view);

            var columnRows = await conn.QueryAsync<SqliteColumnRow>($"PRAGMA table_info('{safeName}');");
            foreach (var col in columnRows)
            {
                view.Columns.Add(new ColumnSchema
                {
                    Name         = col.Name ?? string.Empty,
                    DataType     = col.Type ?? string.Empty,
                    IsPrimaryKey = false,
                    IsNullable   = col.NotNull == 0
                });
            }
        }
    }

    /// <summary>
    /// Oracle のデータディクショナリ（USER_TAB_COLUMNS / USER_CONSTRAINTS）から
    /// テーブル・カラム・主キー・外部キーを取得し response へ格納します。
    /// </summary>
    /// <param name="conn">[パラメータ] オープン済みの Oracle 接続を指定します。</param>
    /// <param name="response">[パラメータ] 解析結果の格納先を指定します。</param>
    private async Task LoadOracleSchemaAsync(DbConnection conn, SchemaResponse response)
    {
        // NULLABLE は 'Y'/'N' で返るため、他プロバイダーと同一の 'YES'/'NO' へ正規化して取得する
        const string columnsSql = @"
            SELECT tc.TABLE_NAME  AS TableName,
                   tc.COLUMN_NAME AS ColumnName,
                   tc.DATA_TYPE   AS DataType,
                   DECODE(tc.NULLABLE, 'Y', 'YES', 'NO') AS IsNullable
            FROM   USER_TAB_COLUMNS tc
            JOIN   USER_TABLES t ON t.TABLE_NAME = tc.TABLE_NAME
            ORDER  BY tc.TABLE_NAME, tc.COLUMN_ID";

        const string pkSql = @"
            SELECT c.TABLE_NAME   AS TableName,
                   cc.COLUMN_NAME AS ColumnName
            FROM   USER_CONSTRAINTS c
            JOIN   USER_CONS_COLUMNS cc ON cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            WHERE  c.CONSTRAINT_TYPE = 'P'";

        const string fkSql = @"
            SELECT c.CONSTRAINT_NAME AS ConstraintName,
                   c.TABLE_NAME      AS FkTable,
                   cc.COLUMN_NAME    AS FkColumn,
                   rc.TABLE_NAME     AS PkTable,
                   rcc.COLUMN_NAME   AS PkColumn
            FROM   USER_CONSTRAINTS c
            JOIN   USER_CONS_COLUMNS cc  ON cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            JOIN   USER_CONSTRAINTS rc   ON rc.CONSTRAINT_NAME = c.R_CONSTRAINT_NAME
            JOIN   USER_CONS_COLUMNS rcc ON rcc.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
                                        AND rcc.POSITION        = cc.POSITION
            WHERE  c.CONSTRAINT_TYPE = 'R'";

        // ビュー一覧（USER_VIEWS と USER_TAB_COLUMNS を結合）
        const string viewColumnsSql = @"
            SELECT tc.TABLE_NAME  AS TableName,
                   tc.COLUMN_NAME AS ColumnName,
                   tc.DATA_TYPE   AS DataType,
                   DECODE(tc.NULLABLE, 'Y', 'YES', 'NO') AS IsNullable
            FROM   USER_TAB_COLUMNS tc
            JOIN   USER_VIEWS v ON v.VIEW_NAME = tc.TABLE_NAME
            ORDER  BY tc.TABLE_NAME, tc.COLUMN_ID";

        // ストアドプロシージャ・ファンクション・パッケージ一覧
        const string routinesSql = @"
            SELECT OBJECT_NAME AS Name, OBJECT_TYPE AS RoutineType
            FROM   USER_OBJECTS
            WHERE  OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION', 'PACKAGE')
            ORDER  BY OBJECT_NAME";

        var columnRows = (await conn.QueryAsync<RawColumnRow>(columnsSql)).ToList();
        var pkSet = new HashSet<string>(
            (await conn.QueryAsync<RawPrimaryKeyRow>(pkSql)).Select(p => $"{p.TableName}.{p.ColumnName}"));

        BuildTablesFromRows(response, columnRows, c => pkSet.Contains($"{c.TableName}.{c.ColumnName}"));

        var viewColumnRows = await conn.QueryAsync<RawColumnRow>(viewColumnsSql);
        BuildViewsFromRows(response, viewColumnRows);

        var routineRows = await conn.QueryAsync<RawRoutineRow>(routinesSql);
        AppendProcedures(response, routineRows);

        // インデックス一覧（主キー制約と同名の自動生成インデックスは除外し、🔑マーカーとの重複を避ける）
        const string indexSql = @"
            SELECT ic.TABLE_NAME  AS TableName,
                   ic.INDEX_NAME  AS IndexName,
                   ic.COLUMN_NAME AS ColumnName,
                   DECODE(ui.UNIQUENESS, 'UNIQUE', '1', '0') AS IsUniqueFlag,
                   ic.COLUMN_POSITION AS SeqInIndex
            FROM   USER_IND_COLUMNS ic
            JOIN   USER_INDEXES ui ON ui.INDEX_NAME = ic.INDEX_NAME
            WHERE  NOT EXISTS (
                SELECT 1 FROM USER_CONSTRAINTS con
                WHERE con.CONSTRAINT_TYPE = 'P' AND con.CONSTRAINT_NAME = ic.INDEX_NAME
            )
            ORDER  BY ic.TABLE_NAME, ic.INDEX_NAME, ic.COLUMN_POSITION";
        var indexRows = await conn.QueryAsync<RawIndexRow>(indexSql);
        AppendIndexesToTables(response, indexRows);

        var fkRows = await conn.QueryAsync<RawForeignKeyRow>(fkSql);
        AppendForeignKeys(response, fkRows);
    }

    /// <summary>
    /// [1. 処理概要]
    /// MongoDB のコレクション一覧を取得し、各コレクションの先頭ドキュメントからフィールド構成を推定して response へ格納します。
    ///
    /// [2. 処理フロー]
    /// 1. 接続文字列にデータベース名が含まれる場合は当該 DB のみを、含まれない場合はシステム DB を除く全 DB を走査します。
    /// 2. 各コレクションの先頭 1 ドキュメントを取得し、フィールド名と BSON 型名を疑似カラムとして登録します。
    /// 3. スキーマレスのため外部キーは生成しません（_id のみ主キー相当として扱います）。
    /// </summary>
    /// <param name="connectionString">[パラメータ] MongoDB 接続文字列を指定します。</param>
    /// <param name="response">[パラメータ] 解析結果の格納先を指定します。</param>
    /// <param name="cancellationToken">[パラメータ] キャンセルトークンを指定します。</param>
    private async Task LoadMongoSchemaAsync(string connectionString, SchemaResponse response, CancellationToken cancellationToken)
    {
        var url    = new MongoUrl(connectionString);
        var client = new MongoClient(connectionString);

        var databaseNames = new List<string>();
        if (!string.IsNullOrEmpty(url.DatabaseName)) // 接続文字列で DB 指定あり
        {
            databaseNames.Add(url.DatabaseName);
        }
        else // DB 未指定時はシステム DB を除く全 DB を横断（コレクション名は "DB名.コレクション名" 形式で衝突回避）
        {
            using var dbCursor = await client.ListDatabaseNamesAsync(cancellationToken);
            var allNames = await dbCursor.ToListAsync(cancellationToken);
            databaseNames.AddRange(allNames.Where(n => n != "admin" && n != "local" && n != "config"));
        }

        bool qualifyName = databaseNames.Count > 1 || string.IsNullOrEmpty(url.DatabaseName);

        foreach (var dbName in databaseNames)
        {
            var database = client.GetDatabase(dbName);

            using var collectionCursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
            var collectionNames = await collectionCursor.ToListAsync(cancellationToken);

            foreach (var collectionName in collectionNames)
            {
                var table = new TableSchema { Name = qualifyName ? $"{dbName}.{collectionName}" : collectionName };
                response.Tables.Add(table);

                var collection = database.GetCollection<BsonDocument>(collectionName);
                var sample     = await collection.Find(new BsonDocument())
                                                 .Limit(1)
                                                 .FirstOrDefaultAsync(cancellationToken);

                if (sample == null) // 空コレクションはフィールドを推定できないため名称のみ登録する
                {
                    continue;
                }

                foreach (var element in sample.Elements)
                {
                    table.Columns.Add(new ColumnSchema
                    {
                        Name         = element.Name,
                        DataType     = element.Value.BsonType.ToString(),
                        IsPrimaryKey = string.Equals(element.Name, "_id", StringComparison.Ordinal),
                        IsNullable   = element.Value.IsBsonNull
                    });
                }
            }
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// Redis のキー空間を走査し、キー名の名前空間（":" 区切りの先頭要素）を疑似テーブル、
    /// 個々のキーを疑似カラムとして response へ格納します。
    ///
    /// [2. 処理フロー]
    /// 1. AllowAdmin を有効化して接続し、SCAN によるキー列挙を可能にします。
    /// 2. 走査件数の上限（MaxRedisKeyScanCount）を超えた時点で打ち切り、大規模キー空間での応答停止を防止します。
    /// 3. 各キーの Redis 型（string / list / hash / set / zset 等）をデータ型として登録します。
    /// </summary>
    /// <param name="connectionString">[パラメータ] Redis 接続文字列を指定します。</param>
    /// <param name="response">[パラメータ] 解析結果の格納先を指定します。</param>
    private async Task LoadRedisSchemaAsync(string connectionString, SchemaResponse response)
    {
        var options       = ConfigurationOptions.Parse(connectionString);
        options.AllowAdmin = true; // SCAN（キー列挙）に必要

        using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var database          = multiplexer.GetDatabase();

        var endpoint = multiplexer.GetEndPoints().FirstOrDefault();
        if (endpoint == null) // 接続先エンドポイント未解決時はガード
        {
            throw new InvalidOperationException("Redis の接続先エンドポイントを解決できませんでした。");
        }

        var server = multiplexer.GetServer(endpoint);

        // 1. SCAN でキー名のみを先に収集する（この時点では型取得を行わない）
        var keys = new List<RedisKey>();
        foreach (var key in server.Keys(database.Database, pattern: "*", pageSize: 250))
        {
            if (keys.Count >= MaxRedisKeyScanCount) // 走査上限に到達したら打ち切る
            {
                _logger.LogWarning($"[WARNING] DbServiceImpl.LoadRedisSchemaAsync: キー走査上限 {MaxRedisKeyScanCount} 件に到達したため打ち切りました");
                break;
            }
            keys.Add(key);
        }

        // 2. 型取得（KeyTypeAsync）はキー毎に逐次 await せず、バッチ単位でまとめて発火してパイプライン化する。
        //    3,000件超のキーで逐次await方式（1件ずつ完了を待つ）が約3秒かかっていた性能問題を解消するための対応
        var keyTypes = new RedisType[keys.Count];
        for (int offset = 0; offset < keys.Count; offset += RedisPipelineBatchSize)
        {
            int batchSize = Math.Min(RedisPipelineBatchSize, keys.Count - offset);
            var tasks = new Task<RedisType>[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                tasks[i] = database.KeyTypeAsync(keys[offset + i]); // await せず即座に発火し多重化接続へパイプライン投入する
            }

            var results = await Task.WhenAll(tasks);
            Array.Copy(results, 0, keyTypes, offset, batchSize);
        }

        // 3. 名前空間（":" 区切りの先頭要素）ごとに疑似テーブルへグルーピングして response へ格納する
        var tableMap = new Dictionary<string, TableSchema>();
        for (int i = 0; i < keys.Count; i++)
        {
            string keyName   = keys[i].ToString();
            int separator    = keyName.IndexOf(':');
            string groupName = separator > 0 ? keyName.Substring(0, separator) : "(root)"; // 名前空間なしのキーは (root) へ集約

            if (!tableMap.TryGetValue(groupName, out var table))
            {
                table = new TableSchema { Name = groupName };
                tableMap[groupName] = table;
                response.Tables.Add(table);
            }

            table.Columns.Add(new ColumnSchema
            {
                Name         = keyName,
                DataType     = keyTypes[i].ToString().ToLower(),
                IsPrimaryKey = false,
                IsNullable   = false
            });
        }
    }

    /// <summary>
    /// インデックス行データをテーブル名でグルーピングし、対応する TableSchema.Indexes へ格納します
    /// （BuildTablesFromRows で response.Tables が構築済みであることが前提。対象テーブルが見つからない行は安全側でスキップ）。
    /// </summary>
    /// <param name="response">[パラメータ] 格納先の SchemaResponse を指定します。</param>
    /// <param name="rows">[パラメータ] インデックス行データを指定します。</param>
    private static void AppendIndexesToTables(SchemaResponse response, IEnumerable<RawIndexRow> rows)
    {
        var tableMap = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in response.Tables)
        {
            tableMap[t.Name] = t; // 同名重複は通常発生しない前提（後勝ちで許容）
        }

        var indexMap = new Dictionary<(string Table, string Index), IndexSchema>();

        foreach (var row in rows.OrderBy(r => r.SeqInIndex))
        {
            if (!tableMap.TryGetValue(row.TableName, out var table))
            {
                continue; // 対象テーブルが見つからない場合は安全側でスキップ
            }

            var key = (row.TableName, row.IndexName);
            if (!indexMap.TryGetValue(key, out var idx))
            {
                idx = new IndexSchema { Name = row.IndexName, IsUnique = row.IsUniqueFlag == "1" };
                indexMap[key] = idx;
                table.Indexes.Add(idx);
            }
            idx.Columns.Add(row.ColumnName);
        }
    }

    /// <summary>
    /// カラム行データからテーブル単位にグルーピングし response.Tables を構築します。
    /// </summary>
    private void BuildTablesFromRows(SchemaResponse response, IEnumerable<RawColumnRow> rows, Func<RawColumnRow, bool> isPrimaryKey)
    {
        var tableMap = new Dictionary<string, TableSchema>();
        foreach (var row in rows)
        {
            if (!tableMap.TryGetValue(row.TableName, out var table))
            {
                table = new TableSchema { Name = row.TableName };
                tableMap[row.TableName] = table;
                response.Tables.Add(table);
            }

            table.Columns.Add(new ColumnSchema
            {
                Name         = row.ColumnName ?? string.Empty,
                DataType     = row.DataType ?? string.Empty,
                IsPrimaryKey = isPrimaryKey(row),
                IsNullable   = string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    /// <summary>
    /// カラム行データからビュー単位にグルーピングし response.Views を構築します（BuildTablesFromRows のビュー版）。
    /// </summary>
    private void BuildViewsFromRows(SchemaResponse response, IEnumerable<RawColumnRow> rows)
    {
        var viewMap = new Dictionary<string, TableSchema>();
        foreach (var row in rows)
        {
            if (!viewMap.TryGetValue(row.TableName, out var view))
            {
                view = new TableSchema { Name = row.TableName };
                viewMap[row.TableName] = view;
                response.Views.Add(view);
            }

            view.Columns.Add(new ColumnSchema
            {
                Name         = row.ColumnName ?? string.Empty,
                DataType     = row.DataType ?? string.Empty,
                IsPrimaryKey = false, // ビューに主キー概念は無い
                IsNullable   = string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    /// <summary>
    /// ストアドプロシージャ・ファンクション行データを response.Procedures へ変換して追加します。
    /// </summary>
    private void AppendProcedures(SchemaResponse response, IEnumerable<RawRoutineRow> rows)
    {
        foreach (var row in rows)
        {
            response.Procedures.Add(new RoutineSchema
            {
                Name        = row.Name ?? string.Empty,
                RoutineType = row.RoutineType ?? string.Empty
            });
        }
    }

    /// <summary>
    /// 外部キー行データを response.ForeignKeys へ変換して追加します。
    /// </summary>
    private void AppendForeignKeys(SchemaResponse response, IEnumerable<RawForeignKeyRow> rows)
    {
        foreach (var fk in rows)
        {
            response.ForeignKeys.Add(new ForeignKeySchema
            {
                ConstraintName = fk.ConstraintName ?? string.Empty,
                PkTable        = fk.PkTable ?? string.Empty,
                PkColumn       = fk.PkColumn ?? string.Empty,
                FkTable        = fk.FkTable ?? string.Empty,
                FkColumn       = fk.FkColumn ?? string.Empty
            });
        }
    }

    #endregion

    #region Schema Introspection Row DTOs

    /// <summary>information_schema.COLUMNS の取得結果マッピング用行データ。</summary>
    private class RawColumnRow
    {
        public string TableSchemaName { get; set; } = string.Empty;
        public string TableName  { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string DataType   { get; set; } = string.Empty;
        public string IsNullable { get; set; } = string.Empty;
        public string? ColumnKey { get; set; }
    }

    /// <summary>主キー列挙用の行データ（table_name + column_name）。</summary>
    private class RawPrimaryKeyRow
    {
        public string TableName  { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
    }

    /// <summary>インデックス一覧の取得結果マッピング用行データ（"1"/"0" 文字列で一意性フラグを方言非依存に統一）。</summary>
    private class RawIndexRow
    {
        public string TableName    { get; set; } = string.Empty;
        public string IndexName    { get; set; } = string.Empty;
        public string ColumnName   { get; set; } = string.Empty;
        public string IsUniqueFlag { get; set; } = "0";
        public int    SeqInIndex   { get; set; }
    }

    /// <summary>MySQL クロススキーマ横断時（Database未指定）に、所属スキーマ名も併せて取得するための拡張行データ。</summary>
    private class RawIndexRowWithSchema : RawIndexRow
    {
        public string TableSchemaForIndex { get; set; } = string.Empty;
    }

    /// <summary>ストアドプロシージャ・ファンクション一覧の取得結果マッピング用行データ。</summary>
    private class RawRoutineRow
    {
        public string Name        { get; set; } = string.Empty;
        public string RoutineType { get; set; } = string.Empty;
    }

    /// <summary>外部キー制約の取得結果マッピング用行データ。</summary>
    private class RawForeignKeyRow
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string? FkSchemaName  { get; set; }
        public string FkTable        { get; set; } = string.Empty;
        public string FkColumn       { get; set; } = string.Empty;
        public string? PkSchemaName  { get; set; }
        public string PkTable        { get; set; } = string.Empty;
        public string PkColumn       { get; set; } = string.Empty;
    }

    /// <summary>SQLite PRAGMA table_info の取得結果マッピング用行データ。</summary>
    private class SqliteColumnRow
    {
        public string Name    { get; set; } = string.Empty;
        public string Type    { get; set; } = string.Empty;
        public int NotNull    { get; set; }
        public int Pk         { get; set; }
    }

    /// <summary>SQLite PRAGMA index_list の取得結果マッピング用行データ。</summary>
    private class SqliteIndexListRow
    {
        public int    Seq    { get; set; }
        public string Name   { get; set; } = string.Empty;
        public int    Unique { get; set; }
        public string Origin { get; set; } = string.Empty; // "c"=CREATE INDEX, "u"=UNIQUE制約, "pk"=主キー由来
        public int    Partial { get; set; }
    }

    /// <summary>SQLite PRAGMA index_info の取得結果マッピング用行データ。</summary>
    private class SqliteIndexColumnRow
    {
        public int     Seqno { get; set; }
        public int     Cid   { get; set; }
        public string? Name  { get; set; }
    }

    /// <summary>SQLite PRAGMA foreign_key_list の取得結果マッピング用行データ。</summary>
    private class SqliteForeignKeyRow
    {
        public int Id         { get; set; }
        public string Table   { get; set; } = string.Empty;
        public string From    { get; set; } = string.Empty;
        public string To      { get; set; } = string.Empty;
    }

    #endregion
}

#endregion