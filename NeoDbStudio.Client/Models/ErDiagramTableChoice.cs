// ファイル名     : ErDiagramTableChoice.cs
// ファイルパス   : F:\OSS\NeoDbStudio_Project\NeoDbStudio.Client\Models\ErDiagramTableChoice.cs
// クラス/概要    : ErDiagramTableChoice (Class)
// 処理概要/目的  : ER図タブのテーブル絞り込みチェックボックス一覧の1項目を表すバインド用モデル。
// 使用方法/適用先: MainViewModel.ErDiagramTableChoices（ListBox + CheckBox でバインド）
// 依存関係       : CommunityToolkit.Mvvm.ComponentModel
// 注意事項       : ER図（MSAGL Graph）の表示専用フィルタであり、TableModels（Undo/Redo対象の完全なモデル）には影響しない。
// 更新履歴       : 2026/07/30 新規作成（ER図の分割表示機能）
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace NeoDbStudio.Client.Models;

#region ErDiagramTableChoice Class

/// <summary>
/// ER図の表示対象テーブルを個別に選択するためのチェックボックス項目モデル。
/// </summary>
public partial class ErDiagramTableChoice : ObservableObject
{
    #region Properties

    /// <summary>テーブル名（スキーマ修飾名を含む場合あり）。</summary>
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    #endregion

    #region Constructors

    /// <summary>
    /// [1. 処理概要]
    /// テーブル名を指定して ErDiagramTableChoice インスタンスを初期化します。
    /// </summary>
    /// <param name="name">[パラメータ] テーブル名を指定します。</param>
    public ErDiagramTableChoice(string name)
    {
        Name = name;
    }

    #endregion
}

#endregion
