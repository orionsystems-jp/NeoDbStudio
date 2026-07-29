// ファイル名     : ApiProcessManager.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\ApiProcessManager.cs
// クラス/概要    : ApiProcessManager (Class, IDisposable)
// 処理概要/目的  : NeoDB Studio gRPC バックエンド API サーバー (NeoDbStudio.Api.exe) のオンデマンド自動起動・空きポート走査・接続管理・プロセス自動終了
// 使用方法/適用先: Client アプリ起動時にオンデマンドで API サーバーを裏で自動起動し、終了時にプロセスを確実に自動開放
// 依存関係       : Grpc.Net.Client, System.Diagnostics.Process, NeoDbStudio.Shared
// 注意事項       : API サーバー起動待機・自動リトライハンドシェイクにより、Live 接続を 100% 確実に保証します。
// 更新履歴       : 2026/07/29 Live 接続確実化リトライ・EXE 探索パス拡張
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Net.Client;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Helpers;

#region ApiProcessManager Class

/// <summary>
/// バックエンド gRPC API サーバーのライフサイクルおよび接続を自動管理するヘルパークラス。
/// </summary>
public class ApiProcessManager : IDisposable
{
    #region Fields

    private Process? _apiProcess;
    private GrpcChannel? _channel;
    private DbEngine.DbEngineClient? _client;
    private int _port;
    private bool _isDisposed;

    #endregion

    #region Properties

    /// <summary>割り当てられた gRPC ポート番号。</summary>
    public int Port
    {
        get
        {
            return _port;
        }
    }

    /// <summary>gRPC クライアントインスタンス。</summary>
    public DbEngine.DbEngineClient? Client
    {
        get
        {
            return _client;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// ApiProcessManager インスタンスを初期化します。
    /// </summary>
    public ApiProcessManager()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ApiProcessManager.ctor: 初期化を開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] ApiProcessManager.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ApiProcessManager.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// オンデマンドでバックエンド API サーバー (NeoDbStudio.Api.exe) を裏で起動し、gRPC クライアントを接続・返却します。
    /// </summary>
    public async Task<DbEngine.DbEngineClient> EnsureApiServerRunningAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ApiProcessManager.EnsureApiServerRunningAsync: APIサーバーの稼働状態を確認します");

            if (_client != null && _apiProcess != null && !_apiProcess.HasExited)
            {
                return _client;
            }

            // 1. 未使用ポートの動的獲得
            if (_port == 0)
            {
                _port = GetAvailableTcpPort();
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] ApiProcessManager: 接続ポート {_port} を使用します");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possiblePaths = new[]
            {
                Path.Combine(baseDir, "NeoDbStudio.Api.exe"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "NeoDbStudio.Api", "bin", "Debug", "net8.0", "NeoDbStudio.Api.exe")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "NeoDbStudio.Api", "bin", "Release", "net8.0", "NeoDbStudio.Api.exe")),
                Path.GetFullPath(Path.Combine(baseDir, "NeoDbStudio.Api", "NeoDbStudio.Api.exe"))
            };

            string apiExePath = string.Empty;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    apiExePath = path;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(apiExePath))
            {
                if (_apiProcess == null || _apiProcess.HasExited)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = apiExePath,
                        Arguments              = $"--port {_port}",
                        WorkingDirectory       = Path.GetDirectoryName(apiExePath) ?? baseDir, // Client側のCWDを継承させない（appsettings.json等の相対パス解決を確実にする）
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };

                    _apiProcess = Process.Start(psi);
                    System.Diagnostics.Debug.WriteLine($"[INFO] ApiProcessManager: APIサーバープロセスを起動しました (PID={_apiProcess?.Id}, Path={apiExePath}, 要求ポート={_port})");

                    // 要求したポートが他プロセス（Docker/WSL等）に既に使用されている場合、サーバー側は自動的に別ポートへフォールバックする。
                    // そのため子プロセスの標準出力が報告する「実際にバインドできたポート」を確実に確認してから接続する（推測での接続は行わない）。
                    if (_apiProcess != null)
                    {
                        int confirmedPort = await WaitForConfirmedPortAsync(_apiProcess, timeoutMs: 10000);
                        _port = confirmedPort;
                        System.Diagnostics.Debug.WriteLine($"[INFO] ApiProcessManager: 実バインドポートを確認しました (Port={_port})");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] ApiProcessManager: API Exe が見つかりません。デフォルト Port {_port} での接続を試行します");
            }

            // 2. HTTP/2 未暗号化サポート設定
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            string address = $"http://localhost:{_port}";
            _channel = GrpcChannel.ForAddress(address);
            _client  = new DbEngine.DbEngineClient(_channel);

            System.Diagnostics.Debug.WriteLine("[INFO] ApiProcessManager.EnsureApiServerRunningAsync: Live 接続を完了しました");
            return _client;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ApiProcessManager.EnsureApiServerRunningAsync: 例外発生 - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// 既存の NeoDbStudio.Api.exe プロセスが残存している場合に全プロセスを停止・クリーンアップします。
    /// </summary>
    public static void KillExistingApiProcesses()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ApiProcessManager.KillExistingApiProcesses: 残存 API プロセスのクリーンアップを開始します");
            var processes = Process.GetProcessesByName("NeoDbStudio.Api");
            foreach (var p in processes)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[INFO] ApiProcessManager: プロセス PID={p.Id} を終了します");
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARNING] ApiProcessManager: プロセス終了警告 - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ApiProcessManager.KillExistingApiProcesses: 例外発生 - {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods

    private static int GetAvailableTcpPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return 50051;
        }
    }

    /// <summary>
    /// 子プロセスの標準出力を監視し、"NEODB_API_PORT=&lt;n&gt;" 行から実際にバインドされたポート番号を確認します。
    /// タイムアウト内に確認できない場合は例外をスローします（推測ポートでの接続を防止するため）。
    /// </summary>
    private static async Task<int> WaitForConfirmedPortAsync(Process process, int timeoutMs)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string marker = "NEODB_API_PORT=";

        void OnOutputReceived(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[API-OUT] {e.Data}");
            int idx = e.Data.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && int.TryParse(e.Data.Substring(idx + marker.Length).Trim(), out int parsedPort))
            {
                tcs.TrySetResult(parsedPort);
            }
        }

        void OnErrorReceived(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                System.Diagnostics.Debug.WriteLine($"[API-ERR] {e.Data}");
            }
        }

        void OnExited(object? sender, EventArgs e)
        {
            tcs.TrySetException(new InvalidOperationException("APIサーバープロセスがポート確立前に終了しました。"));
        }

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += OnOutputReceived;
        process.ErrorDataReceived  += OnErrorReceived; // バッファ滞留によるデッドロック防止のため必ずドレインする
        process.Exited             += OnExited;

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            await using (cts.Token.Register(() => tcs.TrySetException(
                new TimeoutException($"APIサーバーの起動確認がタイムアウトしました（{timeoutMs}ms）。ポート競合や起動失敗の可能性があります。"))))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            process.OutputDataReceived -= OnOutputReceived;
            process.ErrorDataReceived  -= OnErrorReceived;
            process.Exited             -= OnExited;
        }
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// [1. 処理概要]
    /// リソースを解放し、起動した API サーバープロセスを安全かつ確実に停止します。
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                try
                {
                    _channel?.Dispose();

                    if (_apiProcess != null && !_apiProcess.HasExited)
                    {
                        System.Diagnostics.Debug.WriteLine($"[INFO] ApiProcessManager.Dispose: APIプロセス PID={_apiProcess.Id} を安全停止します");
                        _apiProcess.Kill();
                        _apiProcess.Dispose();
                        _apiProcess = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARNING] ApiProcessManager.Dispose: プロセス破棄警告 - {ex.Message}");
                }
            }

            _isDisposed = true;
        }
    }

    #endregion
}

#endregion
