// ファイル名     : AppTheme.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Models\AppTheme.cs
// クラス/概要    : AppTheme (Class), ThemeMode (Enum)
// 処理概要/目的  : NeoDB Studio のマルチカラーテーマパレットモデル定義（WCAG 2.1 高コントラスト基準）
// 使用方法/適用先: MainViewModel でコレクション管理し、MainWindow の動的スタイル・テーマ切り替えに使用
// 依存関係       : System.Windows.Media.Color
// 注意事項       : 各テーマのメイン文字色およびサブ文字色は、アクセシビリティ視認性基準（コントラスト比）を満たす色を設定しています。
// 更新履歴       : 2026/07/28 高コントラスト文字色プロパティ拡張およびコーディング規約全適用
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows.Media;

namespace NeoDbStudio.Client.Models;

#region ThemeMode Enum

/// <summary>
/// アプリケーションテーマモード列挙型。
/// </summary>
public enum ThemeMode
{
    Dark,         // ダーク (VS Code スタイル)
    Light,        // ライト (モダン)
    MidnightBlue, // ミッドナイトブルー
    Nord,         // ノードダーク
    Cyberpunk     // サイバーパンクパープル
}

#endregion

#region AppTheme Class

/// <summary>
/// マルチカラーテーマ用カラーパレットデータモデルクラス。
/// </summary>
public class AppTheme
{
    #region Fields & Properties

    private string    _name                    = string.Empty;              // テーマ表示名称
    private ThemeMode _mode                    = ThemeMode.Dark;            // テーマモード
    private string    _icon                    = "🌙";                      // 表示アイコン
    private Color     _backgroundColor         = Colors.Black;              // ベース背景色
    private Color     _foregroundColor         = Colors.White;              // メイン文字色
    private Color     _secondaryForegroundColor = Color.FromRgb(200, 200, 200); // 補足文字色 (コントラスト重視)
    private Color     _cardBackgroundColor     = Color.FromRgb(37, 37, 38); // カード背景色
    private Color     _headerBackgroundColor   = Color.FromRgb(45, 45, 48); // ヘッダー背景色
    private Color     _accentColor             = Colors.DodgerBlue;         // アクセントカラー
    private Color     _borderColor             = Color.FromRgb(60, 60, 60); // 境界線色
    private Color     _editorBackgroundColor   = Color.FromRgb(30, 30, 30); // エディタ背景色
    private Color     _editorForegroundColor   = Color.FromRgb(220, 220, 220); // エディタ文字色
    private Color     _editorLineNumberColor   = Color.FromRgb(100, 100, 100); // エディタ行番号色

    /// <summary>テーマ表示名称。</summary>
    public string Name
    {
        get
        {
            return _name;
        }
        set
        {
            _name = value ?? string.Empty;
        }
    }

    /// <summary>テーマモード。</summary>
    public ThemeMode Mode
    {
        get
        {
            return _mode;
        }
        set
        {
            _mode = value;
        }
    }

    /// <summary>表示用絵文字アイコン。</summary>
    public string Icon
    {
        get
        {
            return _icon;
        }
        set
        {
            _icon = value ?? "🎨";
        }
    }

    /// <summary>ベース背景色。</summary>
    public Color BackgroundColor
    {
        get
        {
            return _backgroundColor;
        }
        set
        {
            _backgroundColor = value;
        }
    }

    /// <summary>メイン文字色。</summary>
    public Color ForegroundColor
    {
        get
        {
            return _foregroundColor;
        }
        set
        {
            _foregroundColor = value;
        }
    }

    /// <summary>補足文字色 (説明文用・コントラスト保証)。</summary>
    public Color SecondaryForegroundColor
    {
        get
        {
            return _secondaryForegroundColor;
        }
        set
        {
            _secondaryForegroundColor = value;
        }
    }

    /// <summary>スタート画面・各種カードの背景色。</summary>
    public Color CardBackgroundColor
    {
        get
        {
            return _cardBackgroundColor;
        }
        set
        {
            _cardBackgroundColor = value;
        }
    }

    /// <summary>ヘッダーバー背景色。</summary>
    public Color HeaderBackgroundColor
    {
        get
        {
            return _headerBackgroundColor;
        }
        set
        {
            _headerBackgroundColor = value;
        }
    }

    /// <summary>アクセントカラー。</summary>
    public Color AccentColor
    {
        get
        {
            return _accentColor;
        }
        set
        {
            _accentColor = value;
        }
    }

    /// <summary>枠線・境界線色。</summary>
    public Color BorderColor
    {
        get
        {
            return _borderColor;
        }
        set
        {
            _borderColor = value;
        }
    }

    /// <summary>SQL エディタ背景色。</summary>
    public Color EditorBackgroundColor
    {
        get
        {
            return _editorBackgroundColor;
        }
        set
        {
            _editorBackgroundColor = value;
        }
    }

    /// <summary>SQL エディタ文字色。</summary>
    public Color EditorForegroundColor
    {
        get
        {
            return _editorForegroundColor;
        }
        set
        {
            _editorForegroundColor = value;
        }
    }

    /// <summary>SQL エディタ行番号色。</summary>
    public Color EditorLineNumberColor
    {
        get
        {
            return _editorLineNumberColor;
        }
        set
        {
            _editorLineNumberColor = value;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// AppTheme インスタンスの初期化を行います。
    /// </summary>
    public AppTheme()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] AppTheme.ctor: 初期化を開始します");
            System.Diagnostics.Debug.WriteLine("[INFO] AppTheme.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] AppTheme.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
