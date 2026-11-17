using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Binding = System.Windows.Data.Binding;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Maintenance_app.converts
{
    public static class FieldFeedback
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(FieldFeedback), new PropertyMetadata(false, Enable));
        public static readonly DependencyProperty HasValueProperty = DependencyProperty.RegisterAttached("HasValue", typeof(bool), typeof(FieldFeedback), new PropertyMetadata(false));
        public static bool GetEnabled(DependencyObject field) => (bool)field.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject field, bool value) => field.SetValue(EnabledProperty, value);
        public static bool GetHasValue(DependencyObject field) => (bool)field.GetValue(HasValueProperty);
        private static void Enable(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is not Control field || e.NewValue is not true) return;
            // ط±ط¨ط· ظ…ط­ط§ظٹط¯ ظ…ط³طھظ‚ظ„ ط¹ظ† ظ‚ظٹظ…ط© ط§ظ„ط­ظ‚ظ„ ظٹط³ظ…ط­ ط¨ط¥ط±ظپط§ظ‚ ط®ط·ط£ ط¹ط±ط¨ظٹ ط¯ظˆظ† طھط؛ظٹظٹط± ط¨ظٹط§ظ†ط§طھ ط§ظ„ظ…ط³طھط®ط¯ظ….
            if (field.GetBindingExpression(FrameworkElement.TagProperty) is null)
                BindingOperations.SetBinding(field, FrameworkElement.TagProperty, new Binding("Name") { Source = field, Mode = BindingMode.OneWay });
            field.PreviewTextInput += (_, _) => Clear(field);
            field.Loaded += (_, _) => Changed(field);
            if (field is TextBox text) text.TextChanged += (_, _) => Changed(field);
            if (field is ComboBox combo) combo.SelectionChanged += (_, _) => Changed(field);
            if (field is PasswordBox password) password.PasswordChanged += (_, _) => Changed(field);
            if (field is DatePicker date) date.SelectedDateChanged += (_, _) => Changed(field);
        }
        private static void Changed(Control field)
        {
            Clear(field);
            bool filled = field switch
            {
                TextBox text => !string.IsNullOrWhiteSpace(text.Text),
                ComboBox combo => combo.SelectedItem is not null,
                PasswordBox password => password.Password.Length > 0,
                DatePicker date => date.SelectedDate.HasValue,
                _ => false
            };
            field.SetValue(HasValueProperty, filled);
        }
        public static bool Reject(Control field, string message)
        {
            if (field.GetBindingExpression(FrameworkElement.TagProperty) is { } binding)
                Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(), binding, message, null));
            field.Focus(); return false;
        }
        public static void Clear(Control field)
        {
            if (field.GetBindingExpression(FrameworkElement.TagProperty) is { } binding) Validation.ClearInvalid(binding);
        }
    }
}
