using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CoreX.Models;

// 明确指定使用 WPF 的 Color 类型，避免与 System.Drawing.Color 冲突
using Color = System.Windows.Media.Color;

namespace CoreX.Converters;

/// <summary>
/// 布尔值转可见性转换器（支持null检查和对象检查）
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue;
        
        if (value is bool b)
        {
            boolValue = b;
        }
        else if (value != null)
        {
            // 非null对象视为true
            boolValue = true;
        }
        else
        {
            boolValue = false;
        }
        
        bool invert = parameter?.ToString() == "Invert";
        return (boolValue != invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// 核心类型转颜色转换器
/// </summary>
public class CoreTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CoreType coreType)
        {
            return coreType switch
            {
                CoreType.PCore => new SolidColorBrush(Color.FromRgb(0, 122, 255)),     // #007AFF
                CoreType.ECore => new SolidColorBrush(Color.FromRgb(48, 209, 88)),     // #30D158
                CoreType.VCache => new SolidColorBrush(Color.FromRgb(255, 159, 10)),   // #FF9F0A
                CoreType.Standard => new SolidColorBrush(Color.FromRgb(191, 90, 242)), // #BF5AF2
                _ => new SolidColorBrush(Color.FromRgb(174, 174, 178))                  // #AEAEB2
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 核心选中状态转背景色转换器
/// </summary>
public class CoreSelectedToBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is bool isSelected && values[1] is CoreType coreType)
        {
            if (isSelected)
            {
                return coreType switch
                {
                    CoreType.PCore => new SolidColorBrush(Color.FromRgb(0, 122, 255)),     // #007AFF
                    CoreType.ECore => new SolidColorBrush(Color.FromRgb(48, 209, 88)),     // #30D158
                    CoreType.VCache => new SolidColorBrush(Color.FromRgb(255, 159, 10)),   // #FF9F0A
                    CoreType.Standard => new SolidColorBrush(Color.FromRgb(191, 90, 242)), // #BF5AF2
                    _ => new SolidColorBrush(Color.FromRgb(174, 174, 178))                  // #AEAEB2
                };
            }
            else
            {
                return new SolidColorBrush(Color.FromRgb(255, 255, 255)); // 纯白背景
            }
        }
        return new SolidColorBrush(Colors.White);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 核心选中状态转前景色转换器
/// </summary>
public class CoreSelectedToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected)
        {
            return isSelected 
                ? new SolidColorBrush(Colors.White) 
                : new SolidColorBrush(Color.FromRgb(29, 29, 31));
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 应用状态转颜色转换器
/// </summary>
public class AppStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AppStatus status)
        {
            return status switch
            {
                AppStatus.Running => new SolidColorBrush(Color.FromRgb(48, 209, 88)),   // #30D158
                AppStatus.Error => new SolidColorBrush(Color.FromRgb(255, 59, 48)),     // #FF3B30
                _ => new SolidColorBrush(Color.FromRgb(255, 159, 10))                    // #FF9F0A
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 绑定模式枚举转换器
/// </summary>
public class BindingModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.BindingMode mode && parameter is string targetMode)
        {
            return mode.ToString() == targetMode;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string targetMode)
        {
            return Enum.Parse<Models.BindingMode>(targetMode);
        }
        return Models.BindingMode.Dynamic;
    }
}

/// <summary>
/// 进程优先级转中文转换器
/// </summary>
public class PriorityToChineseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ProcessPriorityLevel priority)
        {
            return priority switch
            {
                ProcessPriorityLevel.Idle => "空闲",
                ProcessPriorityLevel.BelowNormal => "低于正常",
                ProcessPriorityLevel.Normal => "正常",
                ProcessPriorityLevel.AboveNormal => "高于正常",
                ProcessPriorityLevel.High => "高",
                ProcessPriorityLevel.RealTime => "实时",
                _ => priority.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// CPU 厂商转可见性转换器
/// </summary>
public class VendorToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CpuVendor vendor && parameter is string targetVendor)
        {
            bool match = vendor.ToString().Equals(targetVendor, StringComparison.OrdinalIgnoreCase);
            return match ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
