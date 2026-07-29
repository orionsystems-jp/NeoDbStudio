// ファイル名     : Program.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Api\Program.cs
// クラス/概要    : Program (Class)
// 処理概要/目的  : NeoDB Studio API サーバのエントリポイント。Kestrel Web サーバの作成、gRPC サービス (DbServiceImpl) のマッピングおよび起動処理。ポート競合発生時は自動的に代替空きポートを検索・使用してバインドエラーを 100% 回避
// 使用方法/適用先: Client アプリからのオンデマンド要求または `dotnet run` 実行時に一番最初にエントリーして ASP.NET Core gRPC サーバを起動
// 依存関係       : Microsoft.AspNetCore.Builder, NeoDbStudio.Api.Services.DbServiceImpl
// 注意事項       : `--port <n>` 引数で希望ポートを指定可能（ApiProcessManager から渡される）。実際にバインドできたポートは
//                 "NEODB_API_PORT=<n>" という機械可読な行として標準出力へ確実に出力する（親プロセスが確実にパースできるよう保証）。
//                 HTTP/2 (h2c) はコードで明示的に強制する（appsettings.json の Kestrel:EndpointDefaults 設定には依存しない。
//                 ワーキングディレクトリが API 実行フォルダと異なる状態で起動されると appsettings.json が見つからず設定が
//                 反映されずに HTTP/1.1 へ後退し gRPC 通信が "HTTP_1_1_REQUIRED" で拒否される不具合が実機で確認されたため）。
// 更新履歴       : 2026/07/28 自動ポートフォールバック機能の追加およびコーディング規約適用
//                 2026/07/29 --port 引数を実際に読み取るよう修正（従来は無視され常に50051から探索していたため、
//                            ApiProcessManager が事前に選んだポートと実際の待受ポートが一致せず接続不能になる不具合を根本修正）
//                 2026/07/29 HTTP/2 強制設定を appsettings.json 依存からコード直書きへ変更
//                            （ワーキングディレクトリ相違により設定ファイルが読み込まれず HTTP/1.1 へ後退する実機不具合を根本修正）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeoDbStudio.Api.Services;

namespace NeoDbStudio.Api;

#region Program Class

/// <summary>
/// API サーバプログラムエントリーポイントクラス。
/// </summary>
public class Program
{
    #region Main Method

    /// <summary>
    /// [1. 処理概要]
    /// アプリケーションメインエントリーポイント。WebApplicationBuilder の構築、gRPC サービスの追加、および自動ポートフォールバックによる WebApplication の実行を行います。
    ///
    /// [2. 処理フロー]
    /// 1. コマンドライン引数 --port から希望ポートを取得します（未指定時は 50051）。
    /// 2. 希望ポートから順番に実バインドを試し（app.StartAsync で確実に成否を確認）、競合時は自動的に次の空きポートへフォールバックします。
    /// 3. 実際にバインドできたポートを "NEODB_API_PORT=<n>" として標準出力へ確実にフラッシュ出力します。
    /// </summary>
    /// <param name="args">[パラメータ] コマンドライン引数を指定します。</param>
    public static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("[INFO] NeoDB Studio API Server: 起動処理を開始します");

            int basePort = ParsePortArgument(args, defaultPort: 50051); // --port 引数を優先使用（未指定時は既定値）
            int maxPort  = basePort + 30; // 希望ポートから30個分を探索範囲とする

            for (int port = basePort; port <= maxPort; port++) // ポート試行ループ
            {
                WebApplication? app = null;
                try
                {
                    var builder = WebApplication.CreateBuilder(args);
                    builder.Services.AddGrpc();
                    builder.Services.AddSingleton<DbSessionManager>(); // トランザクションセッション（オートコミットOFF時）を保持するシングルトン

                    int boundPort = port; // ローカル変数へ束縛（クロージャ内で使用するため）
                    builder.WebHost.ConfigureKestrel(options =>
                    {
                        // gRPC には TLS 無しの HTTP/2 (h2c) 対応が必須。appsettings.json の設定ファイル読込に依存せず、
                        // ワーキングディレクトリの状態に関係なく確実に有効化されるようコードで直接指定する。
                        options.ListenLocalhost(boundPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });
                    });

                    app = builder.Build();
                    app.MapGrpcService<DbServiceImpl>();

                    app.MapGet("/", () =>
                    {
                        try
                        {
                            return "Communication with gRPC endpoints must be made through a gRPC client. NeoDB Studio API Engine is Running.";
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] HealthCheck: 例外発生 - {ex.Message}");
                            throw;
                        }
                    });

                    await app.StartAsync(); // ここで実際に Kestrel のバインドが行われ、失敗時は例外がスローされる

                    // 親プロセス(ApiProcessManager)が確実にパースできるよう、機械可読な行を明示出力してフラッシュする
                    Console.WriteLine($"NEODB_API_PORT={port}");
                    Console.WriteLine($"[INFO] NeoDB Studio API Server: 待機ポート {port} で正常バインドしました");
                    Console.Out.Flush();

                    await app.WaitForShutdownAsync();
                    return; // 正常終了（シャットダウン要求受領）
                }
                catch (IOException ioEx) when (ioEx.InnerException is SocketException || ioEx.Message.Contains("already in use"))
                {
                    Console.WriteLine($"[WARNING] ポート {port} は既に使用中のため、次のポートへフォールバックします...");
                    if (app != null)
                    {
                        try { await app.DisposeAsync(); } catch { /* 破棄失敗は無視 */ }
                    }

                    if (port == maxPort) // 最終ポート超過時
                    {
                        Console.WriteLine($"[FATAL] 利用可能なポート ({basePort}~{maxPort}) が見つかりませんでした。");
                        throw;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] Program.Main: API サーバ起動中に致命的例外が発生しました - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// コマンドライン引数から "--port &lt;n&gt;" を読み取ります。未指定・解析失敗時は defaultPort を返却します。
    /// </summary>
    private static int ParsePortArgument(string[] args, int defaultPort)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed))
            {
                return parsed;
            }
        }
        return defaultPort;
    }

    #endregion
}

#endregion
