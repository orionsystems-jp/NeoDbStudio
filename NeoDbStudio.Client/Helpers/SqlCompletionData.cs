// ファイル名     : SqlCompletionData.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Helpers\SqlCompletionData.cs
// クラス/概要    : SqlCompletionData (Class), SqlCompletionProvider (Static Class)
// 処理概要/目的  : AvalonEdit の CompletionWindow へ渡す補完候補データ、および接続中DBMSの
//                  実スキーマ（テーブル名・カラム名）とSQLキーワードから候補一覧を構築するヘルパー
// 使用方法/適用先: MainWindow の SQL エディタ（AvalonEdit TextEditor）の TextArea.TextEntered イベントから利用
// 依存関係       : ICSharpCode.AvalonEdit.CodeCompletion, NeoDbStudio.Client.Models.DbObjectNode
// 注意事項       : カラム名の対象テーブルは特定せず、接続中スキーマ全体のテーブル名・カラム名を候補に含める
//                  （シンプルな全体候補方式。テーブルエイリアスに応じた絞り込みは将来拡張）。
// 更新履歴       : 2026/07/29 新規作成（SQL補完機能の追加）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using NeoDbStudio.Client.Models;

namespace NeoDbStudio.Client.Helpers;

#region SqlCompletionData Class

/// <summary>
/// AvalonEdit CompletionWindow 用の単一補完候補データ。
/// </summary>
public class SqlCompletionData : ICompletionData
{
    #region Properties

    /// <summary>補完候補アイコン（本実装ではアイコンレスのため常に null）。</summary>
    public ImageSource? Image => null;

    /// <summary>補完リストへ挿入されるテキスト。</summary>
    public string Text { get; }

    /// <summary>補完リストに表示される内容（種別バッジ付き）。</summary>
    public object Content { get; }

    /// <summary>選択時に表示される説明文。</summary>
    public object Description { get; }

    /// <summary>候補の並び順優先度（数値が小さいほど上位表示）。</summary>
    public double Priority { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// 補完候補テキスト・分類・並び順優先度を指定して SqlCompletionData インスタンスを初期化します。
    /// </summary>
    /// <param name="text">[パラメータ] 補完挿入テキストを指定します。</param>
    /// <param name="category">[パラメータ] 候補種別（"Keyword" / "Table" / "View" / "Column"）を指定します。</param>
    /// <param name="priority">[パラメータ] 並び順優先度を指定します。</param>
    public SqlCompletionData(string text, string category, double priority)
    {
        Text        = text;
        Priority    = priority;
        Content     = $"{text}  [{category}]";
        Description = $"{category}: {text}";
    }

    #endregion

    #region Methods

    /// <summary>
    /// 補完確定時に呼び出され、入力中のテキスト範囲を候補テキストへ置換します。
    /// </summary>
    /// <param name="textArea">[パラメータ] 対象の TextArea を指定します。</param>
    /// <param name="completionSegment">[パラメータ] 置換対象の文字範囲を指定します。</param>
    /// <param name="insertionRequestEventArgs">[パラメータ] 挿入要求イベント引数を指定します。</param>
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }

    #endregion
}

#endregion

#region SqlCompletionProvider Class

/// <summary>
/// 接続中DBMSの実スキーマ（DbObjectTree）とSQLキーワードから、SQL補完候補一覧を構築する静的ヘルパー。
/// </summary>
public static class SqlCompletionProvider
{
    #region Fields

    /// <summary>主要SQLキーワード一覧（DBMS共通の基本語彙）。</summary>
    private static readonly string[] Keywords =
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "ON", "GROUP", "BY", "ORDER",
        "HAVING", "AND", "OR", "NOT", "NULL", "IS", "AS", "DISTINCT", "LIMIT", "OFFSET",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "CREATE", "TABLE", "ALTER", "DROP", "ADD",
        "COLUMN", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "INDEX", "VIEW", "PROCEDURE",
        "FUNCTION", "BEGIN", "END", "IF", "ELSE", "CASE", "WHEN", "THEN", "UNION", "ALL",
        "EXISTS", "IN", "LIKE", "BETWEEN", "TOP", "FETCH", "NEXT", "FIRST", "ROWS", "ONLY",
        "DEFAULT", "UNIQUE", "CHECK", "CONSTRAINT", "CASCADE", "TRUNCATE", "WITH"
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// [1. 処理概要]
    /// SQLキーワードおよび DbObjectTree（Tables/Views フォルダ配下）から取得した実テーブル名・カラム名を
    /// 統合した補完候補一覧を構築します。
    /// </summary>
    /// <param name="dbObjectTree">[パラメータ] 接続中のオブジェクトツリー（MainViewModel.DbObjectTree）を指定します。</param>
    /// <returns>並び順優先度でソート済みの補完候補一覧を返却します。</returns>
    public static List<SqlCompletionData> BuildCandidates(ObservableCollection<DbObjectNode> dbObjectTree)
    {
        var candidates = new List<SqlCompletionData>();

        foreach (var keyword in Keywords) // 優先度0: SQLキーワード
        {
            candidates.Add(new SqlCompletionData(keyword, "Keyword", 0));
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dbRoot in dbObjectTree) // Database > Schema > Folder(Tables/Views) > Table/View > Column の階層を走査
        {
            foreach (var schemaNode in dbRoot.Children)
            {
                foreach (var folderNode in schemaNode.Children)
                {
                    if (folderNode.Type != DbObjectType.Folder)
                    {
                        continue;
                    }

                    foreach (var objNode in folderNode.Children) // テーブル/ビュー本体
                    {
                        if ((objNode.Type == DbObjectType.Table || objNode.Type == DbObjectType.View)
                            && seenNames.Add(objNode.Name))
                        {
                            candidates.Add(new SqlCompletionData(objNode.Name, objNode.Type == DbObjectType.View ? "View" : "Table", 1));
                        }

                        foreach (var colNode in objNode.Children) // カラム（"colname (datatype)" 形式から名前部のみ抽出）
                        {
                            string colName = ExtractColumnName(colNode.Name);
                            if (colName.Length > 0 && seenNames.Add(colName))
                            {
                                candidates.Add(new SqlCompletionData(colName, "Column", 2));
                            }
                        }
                    }
                }
            }
        }

        return candidates.OrderBy(c => c.Priority).ThenBy(c => c.Text, StringComparer.OrdinalIgnoreCase).ToList();
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// "colname (datatype)" 形式のカラム表示名から、カラム名部分のみを抽出します。
    /// </summary>
    /// <param name="label">[パラメータ] カラムノードの表示名を指定します。</param>
    /// <returns>抽出したカラム名を返却します。</returns>
    private static string ExtractColumnName(string label)
    {
        int openParen = label.IndexOf('(');
        return (openParen > 0 ? label.Substring(0, openParen) : label).Trim();
    }

    #endregion
}

#endregion
