using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace Maintenance_app.converts
{
    public class BooleanOrConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null)
                return false;

            foreach (var value in values)
            {
                // تجاهل القيم الفارغة
                if (value == null || value == DependencyProperty.UnsetValue)
                    continue;

                // لو Boolean
                if (value is bool b && b)
                    return true;

                // لو Visibility
                if (value is Visibility v && v == Visibility.Visible)
                    return true;
            }

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
