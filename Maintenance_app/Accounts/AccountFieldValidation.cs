using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Maintenance_app.Accounts;

internal static class AccountFieldValidation
{
    #region رسائل التحقق داخل الخانات
    // ربط مستقل على Tag يسمح بعرض قالب الخطأ المشترك دون تخزين قيمة كلمة المرور في الربط.
    internal static void Show(FrameworkElement field, string message)
    {
        var binding = field.GetBindingExpression(FrameworkElement.TagProperty) ??
            BindingOperations.SetBinding(field, FrameworkElement.TagProperty, new Binding { Source = string.Empty });
        Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(), binding, message, null));
        field.Focus();
    }

    internal static void Clear(FrameworkElement field)
    {
        if (field.GetBindingExpression(FrameworkElement.TagProperty) is { } binding)
            Validation.ClearInvalid(binding);
    }
    #endregion
}
