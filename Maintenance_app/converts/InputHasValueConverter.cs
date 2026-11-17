using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Maintenance_app.converts
{
    // يحدد وجود قيمة فعلية لتلوين الخانة فقط، دون تغيير قيمتها أو قواعد التحقق منها.
    public sealed class InputHasValueConverter : IValueConverter
    {
        #region تحديد وجود بيانات في الخانة

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // القيمة غير المهيأة أو الفارغة لا تمثل بيانات أدخلها المستخدم.
            if (value == null || value == DBNull.Value || value == DependencyProperty.UnsetValue)
            {
                return false;
            }

            // النص الإرشادي ليس EditValue، والمسافات وحدها تعد خانة فارغة.
            if (value is string text)
            {
                return !string.IsNullOrWhiteSpace(text);
            }

            // الاختيار المتعدد يعد ممتلئًا عند وجود عنصر واحد على الأقل، ويعود فارغًا عند مسح الجميع.
            if (value is IEnumerable selectedItems)
            {
                foreach (object item in selectedItems)
                {
                    return true;
                }

                return false;
            }

            // التاريخ والاختيار المفرد والأرقام، بما فيها الصفر، قيم موجودة؛ صلاحيتها تفحص عند الحفظ.
            return true;
        }

        #endregion

        #region منع تعديل البيانات من التلوين

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // الربط للعرض فقط؛ تغيير اللون لا يعيد كتابة أي قيمة في محرر الإدخال.
            return Binding.DoNothing;
        }

        #endregion
    }
}
