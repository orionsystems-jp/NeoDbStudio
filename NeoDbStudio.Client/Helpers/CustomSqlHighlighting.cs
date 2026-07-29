// ファイル名     : CustomSqlHighlighting.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\CustomSqlHighlighting.cs
// クラス/概要    : CustomSqlHighlighting (Static Class)
// 処理概要/目的  : AvalonEdit の標準 TSQL 構文強調ルールの暗青色キーワード (Dark Blue) をダークテーマ用高コントラストカラー (VS Code #569CD6 シアンブルー) へ上書き変換
// 使用方法/適用先: MainWindow の AvalonEdit セットアップ時に適用し、暗黒背景と同化する視認性問題を完全解消
// 依存関係       : ICSharpCode.AvalonEdit.Highlighting
// 注意事項       : Nullability アナライザー警告 (CS8603) を完全に解消しています。
// 更新履歴       : 2026/07/29 CS8603 警告解消・Nullability 厳格化
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace NeoDbStudio.Client.Helpers;

#region CustomSqlHighlighting Class

/// <summary>
/// AvalonEdit SQL 構文強調のカスタム視認性ヘルパー。
/// </summary>
public static class CustomSqlHighlighting
{
    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// AvalonEdit の TSQL 構文強調定義内の全キーワード色を、ダークモードでくっきり読める高コントラストカラーへ変換します。
    /// </summary>
    public static IHighlightingDefinition? GetDarkThemeSqlHighlighting()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] CustomSqlHighlighting: ダークテーマ用 SQL 高コントラストハイライトを構築します");

            var baseDefinition = HighlightingManager.Instance.GetDefinition("TSQL");
            if (baseDefinition == null)
            {
                return null;
            }

            var cyanBrush   = new SimpleHighlightingBrush(Color.FromRgb(86, 156, 214));  // #569CD6 (VS Code 鮮やかシアン)
            var yellowBrush = new SimpleHighlightingBrush(Color.FromRgb(220, 220, 170)); // #DCDCAA (関数用明るい黄色)
            var stringBrush = new SimpleHighlightingBrush(Color.FromRgb(206, 145, 122)); // #CE9178 (文字列用ライトオレンジ)

            foreach (var color in baseDefinition.NamedHighlightingColors)
            {
                if (color.Name.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                    color.Name.Equals("DataTypeKeywords", StringComparison.OrdinalIgnoreCase))
                {
                    color.Foreground = cyanBrush;
                }
                else if (color.Name.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                {
                    color.Foreground = yellowBrush;
                }
                else if (color.Name.Equals("String", StringComparison.OrdinalIgnoreCase))
                {
                    color.Foreground = stringBrush;
                }
            }

            foreach (var rule in baseDefinition.MainRuleSet.Rules)
            {
                if (rule.Color != null)
                {
                    rule.Color.Foreground = cyanBrush;
                }
            }

            System.Diagnostics.Debug.WriteLine("[INFO] CustomSqlHighlighting: 正常終了しました");
            return baseDefinition;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] CustomSqlHighlighting: 例外発生 - {ex.Message}");
            return HighlightingManager.Instance.GetDefinition("TSQL");
        }
    }

    #endregion
}

#endregion
