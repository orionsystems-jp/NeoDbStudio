// ファイル名     : AvalonEditLineHighlighter.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\AvalonEditLineHighlighter.cs
// クラス/概要    : AvalonEditLineHighlighter (Class)
// 処理概要/目的  : AvalonEdit テキストエディタ上でストアドプロシージャのデバッグ実行中の現在行を黄色のハイライト枠・背景で強調表示するカスタム背景レンダラー
// 使用方法/適用先: SqlEditor.TextArea.TextView.BackgroundRenderers へ追加してデバッグ行強調表示に使用
// 依存関係       : ICSharpCode.AvalonEdit.Rendering.IBackgroundRenderer, System.Windows.Media.DrawingContext
// 注意事項       : Layer プロパティは Background を返し、エディタ背景レイヤーへ描画を行います。
// 更新履歴       : 2026/07/28 CA1416 サポート属性追加および CS1038 構文修正
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace NeoDbStudio.Client.Helpers;

#region AvalonEditLineHighlighter Class

/// <summary>
/// AvalonEdit 用デバッグ行ハイライト背景レンダラー。
/// </summary>
[SupportedOSPlatform("windows7.0")]
public class AvalonEditLineHighlighter : IBackgroundRenderer
{
    #region Fields & Properties

    private int                 _lineNumber = -1; // 強調表示対象の行番号バッキングフィールド
    private readonly Brush      _highlightBrush;  // ハイライト背景ブラシ
    private readonly Pen        _borderPen;       // ハイライト外枠ペン

    /// <summary>レンダリングレイヤー（背景）。</summary>
    public KnownLayer Layer
    {
        get
        {
            return KnownLayer.Background; // 背景レイヤーを指定
        }
    }

    /// <summary>強調表示する 1 始まりの行番号（0 以下の場合は非表示）。</summary>
    public int LineNumber
    {
        get
        {
            return _lineNumber; // 行番号の取得
        }
        set
        {
            _lineNumber = value; // 行番号の設定
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// AvalonEditLineHighlighter インスタンスの初期化を行い、ハイライト用のフリーズ済みブラシおよびペンを構築します。
    /// </summary>
    public AvalonEditLineHighlighter()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] AvalonEditLineHighlighter.ctor: 開始します");

            _highlightBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0)); // 黄色半透明ブラシ
            _highlightBrush.Freeze(); // スレッド共有用のフリーズ処理

            _borderPen = new Pen(Brushes.Gold, 1); // 金色枠線ペン
            _borderPen.Freeze(); // フリーズ処理

            System.Diagnostics.Debug.WriteLine("[INFO] AvalonEditLineHighlighter.ctor: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] AvalonEditLineHighlighter.ctor: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region IBackgroundRenderer Methods

    /// <summary>
    /// [1. 処理概要]
    /// 指定された TextView 上に、LineNumber で指定された行の幅全域にわたるハイライト矩形を描画します。
    /// </summary>
    /// <param name="textView">[パラメータ] 描画対象の AvalonEdit TextView を指定します。</param>
    /// <param name="drawingContext">[パラメータ] 描画コンテキストを指定します。</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] AvalonEditLineHighlighter.Draw: 描画チェックを開始します");

            if (textView == null || drawingContext == null) return; // NULL チェック

            if (_lineNumber <= 0 || textView.Document == null) return; // 無効行判定
            if (_lineNumber > textView.Document.LineCount) return;      // 範囲外行判定

            var line = textView.Document.GetLineByNumber(_lineNumber); // 対象行オブジェクトの取得
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line)) // 画面内セグメント矩形のループ
            {
                var fullLineRect = new Rect(0, rect.Top, textView.ActualWidth, rect.Height); // 行全幅の矩形計算
                drawingContext.DrawRectangle(_highlightBrush, _borderPen, fullLineRect);      // 矩形描画実行
            }

            System.Diagnostics.Debug.WriteLine("[INFO] AvalonEditLineHighlighter.Draw: 描画が正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] AvalonEditLineHighlighter.Draw: 描画例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Static Helper Methods

    /// <summary>
    /// [1. 処理概要]
    /// 指定された TextEditor のデバッグハイライト行を設定・更新します。
    /// </summary>
    /// <param name="editor">[パラメータ] 対象の AvalonEdit TextEditor を指定します。</param>
    /// <param name="lineNumber">[パラメータ] 強調表示する 1 始まりの行番号を指定します。</param>
    public static void SetHighlightLine(TextEditor editor, int lineNumber)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] AvalonEditLineHighlighter.SetHighlightLine: 行 {lineNumber} をセットします");

            if (editor == null || editor.TextArea == null) return;

            AvalonEditLineHighlighter? existing = null;
            foreach (var r in editor.TextArea.TextView.BackgroundRenderers)
            {
                if (r is AvalonEditLineHighlighter h)
                {
                    existing = h;
                    break;
                }
            }

            if (existing == null)
            {
                existing = new AvalonEditLineHighlighter();
                editor.TextArea.TextView.BackgroundRenderers.Add(existing);
            }

            existing.LineNumber = lineNumber;
            editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

            System.Diagnostics.Debug.WriteLine("[INFO] AvalonEditLineHighlighter.SetHighlightLine: 正常終了しました");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] AvalonEditLineHighlighter.SetHighlightLine: 例外発生 - {ex.Message}");
        }
    }

    #endregion
}

#endregion
