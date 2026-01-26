/// <author>
/// AI Assistant
/// </author>

using System;
using System.Globalization;
using System.Windows.Data;

namespace SnVerify
{
    /// <summary>
    /// 布尔值取反转换器，用于将 true 转换为 false，false 转换为 true
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true; // 默认返回 true（按钮可用）
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
