// ファイル名     : ValueConverters.cs
// ファイルパス   : F:\OrionSystems\NeoDbStudio_Project\NeoDbStudio.Client\Converters\ValueConverters.cs
// クラス/概要    : InverseBoolConverter, InverseBoolToVisibilityConverter, DebugStateColorConverter, DebugStateTextConverter (Classes)
// 処理概要/目的  : XAML バインディング用 WPF 値コンバーター群。デバッグ状態の表示文字・背景色変換、および bool 値の非表示/表示反転変換
// 使用方法/適用先: MainWindow.xaml の Resources リソースディクショナリへ登録してコンバーターバインディングとして使用
// 依存関係       : System.Windows.Data.IValueConverter, System.Windows.Visibility, System.Windows.Media.Brush
// 注意事項       : 特記事項なし
// 更新履歴       : 2026/07/28 InverseBoolToVisibilityConverter 追加およびコーディング規約全適用
// 著作権         : Copyright (c) 2026 オリオンシステムズ. All Rights Reserved.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NeoDbStudio.Client.Converters;

#region InverseBoolConverter Class

/// <summary>
/// boolean 値を反転 (true -> false, false -> true) する WPF IValueConverter。
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    #region IValueConverter Methods

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] InverseBoolConverter.Convert: 変換を実行します");
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InverseBoolConverter.Convert: 例外発生 - {ex.Message}");
            return true;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] InverseBoolConverter.ConvertBack: 逆変換を実行します");
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InverseBoolConverter.ConvertBack: 例外発生 - {ex.Message}");
            return true;
        }
    }

    #endregion
}

#endregion

#region InverseBoolToVisibilityConverter Class

/// <summary>
/// boolean 値の偽 (false) を Visible、真 (true) を Collapsed へ変換する WPF IValueConverter。
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    #region IValueConverter Methods

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] InverseBoolToVisibilityConverter.Convert: 変換を実行します");
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InverseBoolToVisibilityConverter.Convert: 例外発生 - {ex.Message}");
            return Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] InverseBoolToVisibilityConverter.ConvertBack: 逆変換を実行します");
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] InverseBoolToVisibilityConverter.ConvertBack: 例外発生 - {ex.Message}");
            return false;
        }
    }

    #endregion
}

#endregion

#region DebugStateColorConverter Class

/// <summary>
/// デバッグ状態 boolean 値を対応する背景色ブラシ (Gold / Gray) へ変換する WPF IValueConverter。
/// </summary>
public class DebugStateColorConverter : IValueConverter
{
    #region IValueConverter Methods

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] DebugStateColorConverter.Convert: 変換を実行します");
            if (value is bool isDebugging && isDebugging)
            {
                return new SolidColorBrush(Color.FromRgb(218, 165, 32)); // Goldenrod (アクティブ)
            }
            return new SolidColorBrush(Color.FromRgb(80, 80, 80)); // DarkGray (停止)
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DebugStateColorConverter.Convert: 例外発生 - {ex.Message}");
            return new SolidColorBrush(Color.FromRgb(80, 80, 80));
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] DebugStateColorConverter.ConvertBack: 未サポートです");
            throw new NotImplementedException();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DebugStateColorConverter.ConvertBack: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion

#region DebugStateTextConverter Class

/// <summary>
/// デバッグ状態 boolean 値を対応する状態テキスト ("Running" / "Idle") へ変換する WPF IValueConverter。
/// </summary>
public class DebugStateTextConverter : IValueConverter
{
    #region IValueConverter Methods

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] DebugStateTextConverter.Convert: 変換を実行します");
            if (value is bool isDebugging && isDebugging)
            {
                return "Running (Active)";
            }
            return "Idle (Stopped)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DebugStateTextConverter.Convert: 例外発生 - {ex.Message}");
            return "Idle (Stopped)";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[INFO] DebugStateTextConverter.ConvertBack: 未サポートです");
            throw new NotImplementedException();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] DebugStateTextConverter.ConvertBack: 例外発生 - {ex.Message}");
            throw;
        }
    }

    #endregion
}

#endregion
