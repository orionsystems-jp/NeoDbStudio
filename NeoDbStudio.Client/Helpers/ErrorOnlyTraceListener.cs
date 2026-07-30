// ファイル名     : ErrorOnlyTraceListener.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\ErrorOnlyTraceListener.cs
// クラス/概要    : ErrorOnlyTraceListener (Class, TraceListener)
// 処理概要/目的  : コーディング規約により全メソッドへ付与されている [INFO] 開始/終了トレースを抑制し、
//                 [ERROR]/[FATAL] のみをデバッグ出力へ通過させる TraceListener。
// 使用方法/適用先: App.xaml.cs の OnStartup 冒頭で Trace.Listeners へ唯一の Listener として登録する。
// 依存関係       : System.Diagnostics
// 注意事項       : Debug.WriteLine は内部的に Trace.Listeners を経由するため、本Listenerを1つ登録するだけで
//                 NeoDbStudio.Client / UndoRedoKit 双方（同一プロセス）の [INFO] トレースを一括抑制できる。
//                 実データ規模（例: 300テーブル超）で [INFO] トレースが数万行に達し、デバッガーアタッチ時に
//                 著しい速度低下を引き起こす問題（ユーザー報告の「無限ループ」の実体）を解消するために新設。
// 更新履歴       : 2026/07/29 新規作成
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Diagnostics;

namespace NeoDbStudio.Client.Helpers;

#region ErrorOnlyTraceListener Class

/// <summary>
/// [ERROR]/[FATAL] を含む行のみを実際の出力（既定のデバッグ出力）へ通過させる TraceListener。
/// </summary>
public sealed class ErrorOnlyTraceListener : TraceListener
{
    #region Fields

    private readonly DefaultTraceListener _inner = new DefaultTraceListener();

    #endregion

    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// 改行なし書き込みを処理します。本Listenerでは改行単位でのフィルタ判定のみをサポートするため何もしません。
    /// </summary>
    public override void Write(string? message)
    {
        // Debug.WriteLine は WriteLine 経由で呼ばれるため、Write 単体は対象外（意図的に無処理）。
    }

    /// <summary>
    /// [1. 処理概要]
    /// 1行分のトレースメッセージを受け取り、[ERROR] または [FATAL] を含む場合のみ実際の出力へ通過させます。
    /// </summary>
    /// <param name="message">[パラメータ] トレースメッセージ本文を指定します。</param>
    public override void WriteLine(string? message)
    {
        if (message == null)
        {
            return;
        }

        if (message.Contains("[ERROR]", StringComparison.Ordinal) || message.Contains("[FATAL]", StringComparison.Ordinal))
        {
            _inner.WriteLine(message);
        }
    }

    #endregion
}

#endregion
