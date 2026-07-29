// ファイル名     : DbSessionManager.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Api\Services\DbSessionManager.cs
// クラス/概要    : DbSessionManager (Class)
// 処理概要/目的  : オートコミットOFF時のトランザクションセッション（DbConnection + DbTransaction）をセッションID単位で保持・管理するシングルトンサービス
// 使用方法/適用先: DbServiceImpl の BeginTransaction/CommitTransaction/RollbackTransaction/ExecuteQuery(session_id指定時) から利用
// 依存関係       : System.Data.Common, Microsoft.Extensions.Logging
// 注意事項       : アイドルタイムアウトを超えたセッションは自動的にロールバックし破棄する（コネクションリーク防止）。
// 更新履歴       : 2026/07/29 新規作成（トランザクション制御機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace NeoDbStudio.Api.Services;

#region DbSessionManager Class

/// <summary>
/// トランザクションセッション（DbConnection + DbTransaction）をセッションID単位で保持するシングルトンサービス。
/// </summary>
public sealed class DbSessionManager : IDisposable
{
    #region Nested Types

    /// <summary>1トランザクションセッション分の保持状態。</summary>
    private sealed class Session
    {
        public DbConnection Connection { get; init; } = default!;
        public DbTransaction Transaction { get; init; } = default!;
        public IDisposable? Tunnel { get; init; }
        public DateTime LastUsedAt { get; set; }
    }

    #endregion

    #region Fields

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15); // 未使用のまま放置されたセッションの自動失効時間
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ILogger<DbSessionManager> _logger;
    private readonly Timer _cleanupTimer;

    #endregion

    #region Constructors

    /// <summary>
    /// DbSessionManager インスタンスを初期化し、アイドルセッション自動回収タイマーを開始します。
    /// </summary>
    public DbSessionManager(ILogger<DbSessionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cleanupTimer = new Timer(_ => CleanupIdleSessions(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 新しいトランザクションセッションを登録し、一意なセッションIDを発行します。
    /// </summary>
    public string CreateSession(DbConnection connection, DbTransaction transaction, IDisposable? tunnel)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new Session
        {
            Connection  = connection,
            Transaction = transaction,
            Tunnel      = tunnel,
            LastUsedAt  = DateTime.UtcNow
        };
        _logger.LogInformation($"[INFO] DbSessionManager.CreateSession: セッション開始 ({sessionId})");
        return sessionId;
    }

    /// <summary>
    /// セッションIDから保持中の DbConnection / DbTransaction を取得します。存在しない場合は false を返却します。
    /// </summary>
    public bool TryGetSession(string sessionId, out DbConnection connection, out DbTransaction transaction)
    {
        if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var session))
        {
            session.LastUsedAt = DateTime.UtcNow;
            connection  = session.Connection;
            transaction = session.Transaction;
            return true;
        }

        connection  = default!;
        transaction = default!;
        return false;
    }

    /// <summary>
    /// セッションをコミットまたはロールバックして終了し、保持していた接続・トンネルを解放します。
    /// </summary>
    public bool EndSession(string sessionId, bool commit)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return false;
        }

        try
        {
            if (commit)
            {
                session.Transaction.Commit();
            }
            else
            {
                session.Transaction.Rollback();
            }
            _logger.LogInformation($"[INFO] DbSessionManager.EndSession: セッション終了 ({sessionId}, commit={commit})");
        }
        finally
        {
            try { session.Transaction.Dispose(); } catch { /* 既に切断済み等は無視 */ }
            try { session.Connection.Dispose(); } catch { /* 既に切断済み等は無視 */ }
            try { session.Tunnel?.Dispose(); } catch { /* 既に切断済み等は無視 */ }
        }

        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// アイドルタイムアウトを超えたセッションを自動的にロールバック・破棄します（クライアント異常終了時のリーク防止）。
    /// </summary>
    private void CleanupIdleSessions()
    {
        var cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.LastUsedAt >= cutoff)
            {
                continue;
            }

            if (_sessions.TryRemove(kvp.Key, out var session))
            {
                _logger.LogWarning($"[WARNING] DbSessionManager.CleanupIdleSessions: アイドルタイムアウトによりセッション {kvp.Key} を自動ロールバックします");
                try { session.Transaction.Rollback(); } catch { /* 既に切断済み等は無視 */ }
                try { session.Transaction.Dispose(); } catch { /* 既に切断済み等は無視 */ }
                try { session.Connection.Dispose(); } catch { /* 既に切断済み等は無視 */ }
                try { session.Tunnel?.Dispose(); } catch { /* 既に切断済み等は無視 */ }
            }
        }
    }

    #endregion

    #region IDisposable

    /// <summary>アプリケーション終了時、保持中の全セッションをロールバックして破棄します。</summary>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var key in _sessions.Keys)
        {
            EndSession(key, commit: false);
        }
    }

    #endregion
}

#endregion
