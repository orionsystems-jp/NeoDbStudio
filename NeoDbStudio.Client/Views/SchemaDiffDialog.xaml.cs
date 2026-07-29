// ファイル名     : SchemaDiffDialog.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\SchemaDiffDialog.xaml.cs
// クラス/概要    : SchemaDiffDialog (Class)
// 処理概要/目的  : 現在の接続（ソース）と任意のターゲット接続のスキーマを比較し、差分を表示する。
//                  検出した差分からターゲットをソースへ同期するDDLスクリプトを生成しクエリタブへ出力する。
// 使用方法/適用先: MainViewModel から非モーダルダイアログとして表示
// 依存関係       : NeoDbStudio.Client.Helpers.SchemaComparer, NeoDbStudio.Client.Helpers.ApiProcessManager
// 注意事項       : 同期スクリプトの自動実行は行わない（クエリタブへ出力しユーザーが内容確認後に任意実行する運用）。
//                 ターゲット接続はソースと同一プロバイダーを前提とする（異種DBMS間の型変換は非対応）。
// 更新履歴       : 2026/07/29 新規作成（スキーマ比較機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows;
using System.Windows.Controls;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Client.Models;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Views;

#region SchemaDiffDialog Class

/// <summary>
/// スキーマ比較ダイアログの Code-Behind。
/// </summary>
public partial class SchemaDiffDialog : Window
{
    #region Fields

    private readonly ApiProcessManager _apiManager;
    private readonly string _sourceProvider;
    private readonly string _sourceConnectionString;
    private readonly SshTunnelConfig? _sourceSshTunnel;
    private readonly Action<string, string, string, string> _onGenerateScript; // (タイトル, SQL, プロバイダー, 接続文字列)

    private SchemaDiffResult? _lastDiffResult;

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// ソース接続情報・APIマネージャー・同期スクリプト生成時のコールバックを指定して初期化します。
    /// </summary>
    /// <param name="apiManager">[パラメータ] gRPCバックエンド起動・接続管理を行う ApiProcessManager を指定します。</param>
    /// <param name="sourceProvider">[パラメータ] ソース側のDBMSプロバイダー種別を指定します。</param>
    /// <param name="sourceConnectionString">[パラメータ] ソース側の接続文字列を指定します。</param>
    /// <param name="sourceSshTunnel">[パラメータ] ソース側のSSHトンネル設定を指定します（未使用時はnull可）。</param>
    /// <param name="onGenerateScript">[パラメータ] 生成された同期スクリプトをクエリタブへ出力するコールバックを指定します。</param>
    public SchemaDiffDialog(
        ApiProcessManager apiManager,
        string sourceProvider,
        string sourceConnectionString,
        SshTunnelConfig? sourceSshTunnel,
        Action<string, string, string, string> onGenerateScript)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] SchemaDiffDialog.ctor: 開始します");
            InitializeComponent();

            _apiManager             = apiManager ?? throw new ArgumentNullException(nameof(apiManager));
            _sourceProvider         = string.IsNullOrEmpty(sourceProvider) ? "PostgreSQL" : sourceProvider;
            _sourceConnectionString = sourceConnectionString ?? string.Empty;
            _sourceSshTunnel        = sourceSshTunnel;
            _onGenerateScript       = onGenerateScript ?? throw new ArgumentNullException(nameof(onGenerateScript));

            TxtSourceInfo.Text = $"[{_sourceProvider}] {_sourceConnectionString}";

            System.Diagnostics.Debug.WriteLine("[INFO] SchemaDiffDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] SchemaDiffDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 処理内容     : 「Compare」ボタンの Click イベントを処理します。
    /// 処理ロジック : ソース・ターゲット両方のスキーマを実際にDBMSから取得し、差分を検出して画面へ反映します。
    /// </summary>
    private async void BtnCompare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetConnectionString = TxtTargetConnectionString.Text.Trim();
            if (string.IsNullOrEmpty(targetConnectionString))
            {
                MessageBox.Show("ターゲットの接続文字列を入力してください。", "Schema Comparison", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnCompare.IsEnabled     = false;
            BtnGenerateSync.IsEnabled = false;
            TxtStatus.Text = "スキーマを取得・比較しています...";

            var client = await _apiManager.EnsureApiServerRunningAsync();

            var sourceSchema = await client.GetSchemaAsync(new SchemaRequest
            {
                ProviderType     = _sourceProvider,
                ConnectionString = _sourceConnectionString,
                SshTunnel        = _sourceSshTunnel ?? new SshTunnelConfig()
            });

            var targetSchema = await client.GetSchemaAsync(new SchemaRequest
            {
                ProviderType     = _sourceProvider, // MVP：異種DBMS間の型変換は非対応のためソースと同一プロバイダーを前提とする
                ConnectionString = targetConnectionString,
                SshTunnel        = new SshTunnelConfig()
            });

            _lastDiffResult = SchemaComparer.Compare(sourceSchema, targetSchema);
            RenderDiffResult(_lastDiffResult);

            BtnGenerateSync.IsEnabled = !_lastDiffResult.IsIdentical;
            TxtStatus.Text = _lastDiffResult.IsIdentical
                ? "差分はありませんでした（スキーマは一致しています）。"
                : $"差分を検出しました：ソースのみ {_lastDiffResult.TablesOnlyInSource.Count} 件 / ターゲットのみ {_lastDiffResult.TablesOnlyInTarget.Count} 件 / 列差分のあるテーブル {_lastDiffResult.CommonTableDiffs.Count} 件";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"スキーマ比較に失敗しました: {ex.Message}", "Schema Comparison", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"エラー: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ERROR] SchemaDiffDialog.BtnCompare_Click: 例外発生 - {ex.Message}");
        }
        finally
        {
            BtnCompare.IsEnabled = true;
        }
    }

    /// <summary>
    /// 検出済みの差分結果を、テーブル一覧・列差分ツリーへ反映します。
    /// </summary>
    /// <param name="diff">[パラメータ] 表示対象の差分結果を指定します。</param>
    private void RenderDiffResult(SchemaDiffResult diff)
    {
        ListTablesOnlyInSource.ItemsSource = diff.TablesOnlyInSource;
        ListTablesOnlyInTarget.ItemsSource = diff.TablesOnlyInTarget;

        TreeCommonTableDiffs.Items.Clear();
        foreach (var tableDiff in diff.CommonTableDiffs)
        {
            var tableItem = new TreeViewItem { Header = $"📋 {tableDiff.TableName} （{tableDiff.ColumnDiffs.Count}件の差分）", IsExpanded = true, Foreground = System.Windows.Media.Brushes.White };
            foreach (var col in tableDiff.ColumnDiffs)
            {
                tableItem.Items.Add(new TreeViewItem { Header = col.Summary, Foreground = System.Windows.Media.Brushes.LightGray });
            }
            TreeCommonTableDiffs.Items.Add(tableItem);
        }
    }

    /// <summary>
    /// 処理内容     : 「Generate Sync Script」ボタンの Click イベントを処理します。
    /// 処理ロジック : 検出済みの差分から同期DDLを生成し、コールバック経由でクエリタブ（ターゲット接続）へ出力します。
    /// </summary>
    private void BtnGenerateSync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastDiffResult == null || _lastDiffResult.IsIdentical)
            {
                return;
            }

            string script = SchemaComparer.GenerateSyncScript(_lastDiffResult, _sourceProvider);
            _onGenerateScript("SchemaSync.sql", script, _sourceProvider, TxtTargetConnectionString.Text.Trim());

            TxtStatus.Text = "同期スクリプトをクエリタブへ出力しました（ターゲット接続宛）。内容を確認のうえ実行してください。";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"同期スクリプトの生成に失敗しました: {ex.Message}", "Schema Comparison", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"[ERROR] SchemaDiffDialog.BtnGenerateSync_Click: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 処理内容     : 「Close」ボタンの Click イベントを処理します。
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}

#endregion
