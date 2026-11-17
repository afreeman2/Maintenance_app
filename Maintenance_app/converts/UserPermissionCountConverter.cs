using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Maintenance_app.converts
{
    public sealed class UserPermissionCountConverter : IMultiValueConverter
    {
        #region عد الصلاحيات المعروضة

        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            // الربط الأخير خاص بتقارير الفروع، ويُحسب في نسخة الإدارة فقط.
            int availableCount = DeploymentEdition.IncludesBranchReports ? values.Length : Math.Max(0, values.Length - 1);
            int enabledCount = values.Take(availableCount).Count(value => value is bool isEnabled && isEnabled);
            return $"الصلاحيات المفعّلة: {enabledCount} من {availableCount}";
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        #endregion
    }

}
