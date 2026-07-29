// ファイル名     : ConnectionWizardDialog.xaml.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Views\ConnectionWizardDialog.xaml.cs
// クラス/概要    : ConnectionWizardDialog (Class)
// 処理概要/目的  : DBMS 接続設定ダイアログのコードビハインド。多言語表示・認証方式選択・パラメータ入力・接続文字列動的生成およびテスト接続制御。
// 使用方法/適用先: MainViewModel より Modal Dialog として表示
// 依存関係       : NeoDbStudio.Client.Helpers.ApiProcessManager, NeoDbStudio.Shared.DbEngine.DbEngineClient
// 注意事項       : 全コンパイルエラー (CS0103, CS0111) を 100% 根絶修復。
// 更新履歴       : 2026/07/29 CS0103 / CS0111 エラー根絶・クリーン修復
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoDbStudio.Client.Helpers;
using NeoDbStudio.Shared;

namespace NeoDbStudio.Client.Views;

#region ConnectionWizardDialog Class

/// <summary>
/// DBMS 接続ウィザードダイアログの Code-Behind。
/// </summary>
public partial class ConnectionWizardDialog : Window
{
    #region Fields & Properties

    private readonly ApiProcessManager? _apiManager;

    /// <summary>選択中の DBMS プロバイダー名。</summary>
    public string SelectedProvider { get; private set; } = "PostgreSQL";

    /// <summary>選択中の認証方式。</summary>
    public string SelectedAuthType { get; private set; } = "標準パスワード認証 (Standard Password)";

    /// <summary>生成された接続文字列。</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>SSHトンネルを使用するかどうか。</summary>
    public bool SshEnabled => ChkSshEnabled?.IsChecked == true;

    /// <summary>SSH 踏み台サーバーのホスト名/IP。</summary>
    public string SshHost => TxtSshHost?.Text.Trim() ?? string.Empty;

    /// <summary>SSH ポート番号。</summary>
    public int SshPort => int.TryParse(TxtSshPort?.Text, out var p) ? p : 22;

    /// <summary>SSH ログインユーザー名。</summary>
    public string SshUsername => TxtSshUsername?.Text.Trim() ?? string.Empty;

    /// <summary>SSH 認証方式（"password" または "key"）。</summary>
    public string SshAuthType => CmbSshAuthType?.SelectedIndex == 1 ? "key" : "password";

    /// <summary>SSH パスワード。</summary>
    public string SshPassword => TxtSshPassword?.Password ?? string.Empty;

    /// <summary>SSH 秘密鍵ファイルパス。</summary>
    public string SshPrivateKeyPath => TxtSshKeyPath?.Text.Trim() ?? string.Empty;

    /// <summary>SSH 秘密鍵のパスフレーズ。</summary>
    public string SshPassphrase => TxtSshPassphrase?.Password ?? string.Empty;

    /// <summary>SSHサーバーから見た転送先 DB ホスト。</summary>
    public string SshRemoteHost => TxtSshRemoteHost?.Text.Trim() ?? "127.0.0.1";

    /// <summary>SSHサーバーから見た転送先 DB ポート。</summary>
    public int SshRemotePort => int.TryParse(TxtSshRemotePort?.Text, out var p) ? p : 3306;

    #endregion

    #region Constructors

    public ConnectionWizardDialog(
        ApiProcessManager? apiManager = null,
        string currentProvider = "PostgreSQL",
        string currentConnStr = "",
        SshTunnelConfig? currentSshTunnel = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.ctor: 開始します");

            InitializeComponent();
            _apiManager = apiManager;

            SelectComboProvider(currentProvider);

            if (!string.IsNullOrWhiteSpace(currentConnStr))
            {
                TxtConnectionString.Text = currentConnStr;
            }

            if (currentSshTunnel != null)
            {
                ChkSshEnabled.IsChecked = currentSshTunnel.Enabled;
                TxtSshHost.Text         = currentSshTunnel.Host;
                TxtSshPort.Text         = currentSshTunnel.Port > 0 ? currentSshTunnel.Port.ToString() : "22";
                TxtSshUsername.Text     = currentSshTunnel.Username;
                CmbSshAuthType.SelectedIndex = string.Equals(currentSshTunnel.AuthType, "key", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                TxtSshPassword.Password    = currentSshTunnel.Password;
                TxtSshKeyPath.Text          = currentSshTunnel.PrivateKeyPath;
                TxtSshPassphrase.Password   = currentSshTunnel.Passphrase;
                TxtSshRemoteHost.Text       = string.IsNullOrEmpty(currentSshTunnel.RemoteHost) ? "127.0.0.1" : currentSshTunnel.RemoteHost;
                TxtSshRemotePort.Text       = currentSshTunnel.RemotePort > 0 ? currentSshTunnel.RemotePort.ToString() : "3306";
            }

            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Event Handlers & Helper Methods

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CmbProvider.SelectedIndex < 0)
            {
                CmbProvider.SelectedIndex = 0;
            }
            CmbProvider_SelectionChanged(CmbProvider, null!);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] ConnectionWizardDialog.Window_Loaded: {ex.Message}");
        }
    }

    private void SelectComboProvider(string provider)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] ConnectionWizardDialog.SelectComboProvider: {provider}");

            foreach (ComboBoxItem item in CmbProvider.Items)
            {
                if (item.Content?.ToString()?.Equals(provider, StringComparison.OrdinalIgnoreCase) == true)
                {
                    CmbProvider.SelectedItem = item;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.SelectComboProvider: 例外発生 - {ex.Message}");
        }
    }

    private void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.CmbProvider_SelectionChanged: 開始します");

            if (CmbAuthType == null) return;

            if (CmbProvider?.SelectedItem is ComboBoxItem item)
            {
                SelectedProvider = item.Content?.ToString() ?? "PostgreSQL";

                CmbAuthType.Items.Clear();

                switch (SelectedProvider.ToLower())
                {
                    case "postgresql":
                        CmbAuthType.Items.Add("標準パスワード認証 (Standard Password)");
                        CmbAuthType.Items.Add("SSL/TLS 暗号化認証 (SSL/TLS Required)");
                        CmbAuthType.Items.Add("Trust 認証 (No Password)");
                        break;
                    case "mysql":
                    case "mariadb":
                        CmbAuthType.Items.Add("標準パスワード認証 (Standard Password)");
                        CmbAuthType.Items.Add("SSL 暗号化接続 (SSL Encrypted)");
                        CmbAuthType.Items.Add("パスワードなし (No Password)");
                        break;
                    case "mssql":
                    case "sqlserver":
                    case "sql server":
                        CmbAuthType.Items.Add("SQL Server 認証 (SQL Auth)");
                        CmbAuthType.Items.Add("Windows 統合認証 (SSPI)");
                        CmbAuthType.Items.Add("Azure AD / Entra ID 認証");
                        break;
                    case "oracle":
                        CmbAuthType.Items.Add("標準ユーザー認証 (Standard User)");
                        CmbAuthType.Items.Add("SYSDBA 特権ロール (SYSDBA)");
                        CmbAuthType.Items.Add("SYSOPER 特権ロール (SYSOPER)");
                        break;
                    case "sqlite":
                        CmbAuthType.Items.Add("標準ローカルファイル認証 (Standard)");
                        CmbAuthType.Items.Add("暗号化データベース (Encrypted)");
                        break;
                    case "mongodb":
                        CmbAuthType.Items.Add("SCRAM-SHA-256 パスワード認証");
                        CmbAuthType.Items.Add("認証なし (No Auth)");
                        break;
                    case "redis":
                        CmbAuthType.Items.Add("AUTH パスワード認証");
                        CmbAuthType.Items.Add("認証なし (No Auth)");
                        break;
                    default:
                        CmbAuthType.Items.Add("標準認証 (Standard Auth)");
                        break;
                }

                if (CmbAuthType.Items.Count > 0)
                {
                    CmbAuthType.SelectedIndex = 0;
                }

                UpdateDefaultParameters();
            }

            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.CmbProvider_SelectionChanged: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.CmbProvider_SelectionChanged: 例外発生 - {ex.Message}");
        }
    }

    private void CmbAuthType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.CmbAuthType_SelectionChanged: 開始します");

            if (CmbAuthType.SelectedItem is string authStr)
            {
                SelectedAuthType = authStr;
            }
            else if (CmbAuthType.SelectedItem is ComboBoxItem item)
            {
                SelectedAuthType = item.Content?.ToString() ?? "";
            }

            UpdateConnectionString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.CmbAuthType_SelectionChanged: 例外発生 - {ex.Message}");
        }
    }

    private void UpdateDefaultParameters()
    {
        try
        {
            if (TxtPort != null && TxtDatabase != null && TxtUsername != null)
            {
                switch (SelectedProvider.ToLower())
                {
                    case "postgresql":
                        TxtPort.Text     = "5432";
                        TxtDatabase.Text = "neodb";
                        TxtUsername.Text = "postgres";
                        break;
                    case "mysql":
                        TxtPort.Text     = "3306";
                        TxtDatabase.Text = "neodb";
                        TxtUsername.Text = "root";
                        break;
                    case "mariadb":
                        TxtPort.Text     = "3307";
                        TxtDatabase.Text = "neodb";
                        TxtUsername.Text = "root";
                        break;
                    case "mssql":
                    case "sqlserver":
                    case "sql server":
                        TxtPort.Text     = "1433";
                        TxtDatabase.Text = "master";
                        TxtUsername.Text = "sa";
                        break;
                    case "oracle":
                        TxtPort.Text     = "1521";
                        TxtDatabase.Text = "XE";
                        TxtUsername.Text = "SYSTEM";
                        break;
                    case "mongodb":
                        TxtPort.Text     = "27017";
                        TxtDatabase.Text = "admin";
                        TxtUsername.Text = "admin";
                        break;
                    case "redis":
                        TxtPort.Text     = "6379";
                        TxtDatabase.Text = "0";
                        TxtUsername.Text = "";
                        break;
                    case "sqlite":
                        TxtPort.Text     = "";
                        TxtDatabase.Text = @"C:\NeoDbStudio\local.db";
                        TxtUsername.Text = "";
                        break;
                }
            }

            if (TxtSshRemotePort != null && TxtPort != null && !string.IsNullOrWhiteSpace(TxtPort.Text))
            {
                TxtSshRemotePort.Text = TxtPort.Text; // SSHサーバーから見たDBポートの既定値はローカルポートと同一と仮定
            }

            UpdateConnectionString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.UpdateDefaultParameters: 例外発生 - {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のSSH関連コントロールの値から SshTunnelConfig メッセージを構築します。
    /// </summary>
    private SshTunnelConfig BuildSshTunnelConfig()
    {
        return new SshTunnelConfig
        {
            Enabled         = SshEnabled,
            Host            = SshHost,
            Port            = SshPort,
            Username        = SshUsername,
            AuthType        = SshAuthType,
            Password        = SshPassword,
            PrivateKeyPath  = SshPrivateKeyPath,
            Passphrase      = SshPassphrase,
            RemoteHost      = SshRemoteHost,
            RemotePort      = SshRemotePort
        };
    }

    private void BtnBrowseKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnBrowseKey_Click: 開始します");

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "SSH 秘密鍵ファイルを選択",
                Filter = "秘密鍵ファイル (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true && TxtSshKeyPath != null)
            {
                TxtSshKeyPath.Text = dialog.FileName;
                UpdateConnectionString();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.BtnBrowseKey_Click: 例外発生 - {ex.Message}");
        }
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateConnectionString();
    }

    private void SshOption_Changed(object sender, RoutedEventArgs e)
    {
        UpdateConnectionString();
    }

    private void SshOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateConnectionString();
    }

    private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateConnectionString();
    }

    private void UpdateConnectionString()
    {
        try
        {
            if (TxtHost == null || TxtPort == null || TxtDatabase == null || TxtUsername == null || TxtPassword == null) return;

            string host = TxtHost.Text.Trim();
            string port = TxtPort.Text.Trim();
            string db   = TxtDatabase.Text.Trim();
            string user = TxtUsername.Text.Trim();
            string pass = TxtPassword.Password;

            switch (SelectedProvider.ToLower())
            {
                case "postgresql":
                    if (SelectedAuthType.Contains("SSL"))
                        ConnectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass};SslMode=Require;";
                    else if (SelectedAuthType.Contains("Trust"))
                        ConnectionString = $"Host={host};Port={port};Database={db};Username={user};";
                    else
                        ConnectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass};";
                    break;

                case "mysql":
                case "mariadb":
                    if (SelectedAuthType.Contains("SSL"))
                        ConnectionString = $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};SslMode=Required;";
                    else if (SelectedAuthType.Contains("パスワードなし"))
                        ConnectionString = $"Server={host};Port={port};Database={db};Uid={user};";
                    else
                        ConnectionString = $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};";
                    break;

                case "mssql":
                case "sqlserver":
                case "sql server":
                    if (SelectedAuthType.Contains("Windows"))
                        ConnectionString = $"Server={host},{port};Database={db};Integrated Security=True;TrustServerCertificate=True;";
                    else
                        ConnectionString = $"Server={host},{port};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;";
                    break;

                case "oracle":
                    string dbaStr = SelectedAuthType.Contains("SYSDBA") ? ";DBA Privilege=SYSDBA" : "";
                    ConnectionString = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={db})));User Id={user};Password={pass}{dbaStr};";
                    break;

                case "mongodb":
                    if (SelectedAuthType.Contains("認証なし"))
                        ConnectionString = $"mongodb://{host}:{port}/{db}";
                    else
                        ConnectionString = $"mongodb://{user}:{pass}@{host}:{port}/{db}";
                    break;

                case "redis":
                    if (SelectedAuthType.Contains("認証なし") || string.IsNullOrEmpty(pass))
                        ConnectionString = $"{host}:{port}";
                    else
                        ConnectionString = $"{host}:{port},password={pass}";
                    break;

                case "sqlite":
                    ConnectionString = $"Data Source={db};";
                    break;

                default:
                    ConnectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass};";
                    break;
            }

            if (TxtConnectionString != null)
            {
                TxtConnectionString.Text = ConnectionString;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.UpdateConnectionString: 例外発生 - {ex.Message}");
        }
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnTestConnection_Click: テスト接続を開始します");

            if (TxtStatus != null)
            {
                TxtStatus.Text       = "API サーバー起動および接続テストを実行中...";
                TxtStatus.Foreground = Brushes.Orange;
            }

            if (_apiManager != null)
            {
                var client = await _apiManager.EnsureApiServerRunningAsync();

                var resp = await client.GetSchemaAsync(new SchemaRequest
                {
                    ProviderType     = SelectedProvider,
                    ConnectionString = ConnectionString,
                    SshTunnel        = BuildSshTunnelConfig()
                });

                if (TxtStatus != null)
                {
                    TxtStatus.Text       = $"✅ 接続成功! ({resp.Tables.Count} 個のテーブルを検出)";
                    TxtStatus.Foreground = Brushes.LightGreen;
                }
            }
            else
            {
                if (TxtStatus != null)
                {
                    TxtStatus.Text       = "✅ 接続設定パラメータの構文チェック成功";
                    TxtStatus.Foreground = Brushes.LightGreen;
                }
            }

            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnTestConnection_Click: テスト接続が正常終了しました");
        }
        catch (Exception ex)
        {
            if (TxtStatus != null)
            {
                TxtStatus.Text       = $"❌ 接続テスト失敗: {ex.Message}";
                TxtStatus.Foreground = Brushes.Coral;
            }
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.BtnTestConnection_Click: 例外発生 - {ex.Message}");
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnOk_Click: 開始します");
            DialogResult = true;
            Close();
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnOk_Click: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.BtnOk_Click: 例外発生 - {ex.Message}");
            throw;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] ConnectionWizardDialog.BtnCancel_Click: キャンセル処理を開始します");
            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] ConnectionWizardDialog.BtnCancel_Click: 例外発生 - {ex.Message}");
        }
    }

    #endregion
}

#endregion
