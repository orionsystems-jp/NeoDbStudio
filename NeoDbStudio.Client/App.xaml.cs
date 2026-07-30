// ファイル名     : App.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\App.xaml.cs
// クラス/概要    : App (Partial Class)
// 処理概要/目的  : NeoDB Studio WPF アプリケーションのエントリポイントおよび Dependency Injection (DI) コンテナ構築・MainWindow 起動
// 使用方法/適用先: アプリ起動時に Services を構築し、MainWindow および MainViewModel を登録・起動
// 依存関係       : Microsoft.Extensions.DependencyInjection, OrionSystems.UndoRedoKit
// 注意事項       : 未処理例外のグローバルハンドリングおよびバックエンド API ポート走査の初期化を行います。
// 更新履歴       : 2026/07/29 DI 完全初期化および DataContext 保証
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Client.ViewModels;
using OrionSystems.UndoRedoKit.Extensions.DependencyInjection;

namespace NeoDbStudio.Client;

#region App Class

/// <summary>
/// NeoDB Studio アプリケーションクラス。
/// </summary>
[SupportedOSPlatform("windows7.0")]
public partial class App : Application
{
    #region Properties

    /// <summary>サービスプロバイダーインスタンス。</summary>
    public static IServiceProvider? Services { get; private set; }

    #endregion

    #region Event Handlers

    /// <summary>
    /// [1. 処理概要]
    /// アプリケーション起動時に DI コンテナを構築し、MainWindow を生成して DataContext を明示的に設定・起動します。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // 全メソッドの [INFO] Entry/Success トレースを抑制し [ERROR]/[FATAL] のみ出力する。
        // 実データ規模（300テーブル超）でトレース量が数万行に達しデバッガーアタッチ時に著しく遅くなる問題を解消するため、
        // 他のどのコードよりも先にリスナーを差し替える。
        System.Diagnostics.Trace.Listeners.Clear();
        System.Diagnostics.Trace.Listeners.Add(new ErrorOnlyTraceListener());

        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] App.OnStartup: アプリ起動・DIコンテナ構築を開始します");
            base.OnStartup(e);

            var services = new ServiceCollection();

            // 1. UndoRedoKit セッションサービスの登録
            services.AddUndoRedoKit();

            // 2. NeoDB Studio コアヘルパー・サービスの登録
            services.AddSingleton<ApiProcessManager>();

            // 3. ViewModels & Views の登録
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            // MainWindow および MainViewModel の解体・注入
            var mainVm = Services.GetRequiredService<MainViewModel>();
            var window = Services.GetRequiredService<MainWindow>();
            window.DataContext = mainVm;
            window.Show();

            System.Diagnostics.Debug.WriteLine("[INFO] App.OnStartup: MainWindow を DataContext 適用済みで正常起動しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] App.OnStartup: アプリ起動例外発生 - {ex.Message}");
            MessageBox.Show($"Application Startup Failed: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// [1. 処理概要]
    /// アプリケーション終了時に DI コンテナを破棄し、ApiProcessManager.Dispose() 経由で
    /// バックグラウンド起動した NeoDbStudio.Api.exe 子プロセスを確実に終了させます。
    /// （未破棄のまま放置すると子プロセスがオーファン化し、次回ビルド時に bin フォルダの
    /// ロック競合を起こすため必須。）
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] App.OnExit: DIコンテナの破棄を開始します");
            (Services as IDisposable)?.Dispose();
            System.Diagnostics.Debug.WriteLine("[INFO] App.OnExit: DIコンテナの破棄が正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] App.OnExit: DIコンテナ破棄中に例外発生 - {ex.Message}");
        }
        finally
        {
            base.OnExit(e);
        }
    }

    #endregion
}

#endregion
