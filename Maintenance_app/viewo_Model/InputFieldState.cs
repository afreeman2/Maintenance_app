using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using DevExpress.Xpf.Editors;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using Binding = System.Windows.Data.Binding;

namespace Maintenance_app.viewo_Model
{
    public static class InputFieldState
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(InputFieldState), new PropertyMetadata(false, EnabledChanged));
        public static readonly DependencyProperty HasValueProperty = DependencyProperty.RegisterAttached(
            "HasValue", typeof(bool), typeof(InputFieldState), new PropertyMetadata(false));

        public static void SetEnabled(DependencyObject target, bool value) { target.SetValue(EnabledProperty, value); }
        public static bool GetEnabled(DependencyObject target) { return (bool)target.GetValue(EnabledProperty); }
        public static bool GetHasValue(DependencyObject target) { return (bool)target.GetValue(HasValueProperty); }
        public static void SetHasValue(DependencyObject target, bool value) { target.SetValue(HasValueProperty, value); }

        private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var border = target as Border;
            if (border == null) return;
            border.Initialized -= Initialized;
            border.Loaded -= Loaded;
            BindingOperations.ClearBinding(border, HasValueProperty);
            if (!(bool)args.NewValue) { SetHasValue(border, false); return; }
            border.Initialized += Initialized;
            border.Loaded += Loaded;
            if (border.IsInitialized) Observe(border);
        }

        private static void Initialized(object sender, EventArgs args) { Observe((Border)sender); }
        private static void Loaded(object sender, RoutedEventArgs args) { Observe((Border)sender); }

        private static void Observe(Border border)
        {
            if (!GetEnabled(border)) return;
            var values = new MultiBinding { Mode = BindingMode.OneWay, Converter = new HasInputConverter() };
            Collect(border.Child, values);
            if (values.Bindings.Count > 0) BindingOperations.SetBinding(border, HasValueProperty, values);
        }

        private static void Collect(DependencyObject node, MultiBinding values)
        {
            if (node == null) return;
            if (node is BaseEdit) { Add(values, node, "EditValue"); return; }
            if (node is TextBox) { Add(values, node, "Text"); return; }
            if (node is DatePicker) { Add(values, node, "SelectedDate"); Add(values, node, "Text"); return; }
            if (node is ComboBox) { Add(values, node, "SelectedItem"); Add(values, node, "Text"); return; }
            if (node is ToggleButton) { Add(values, node, "IsChecked"); return; }
            var panel = node as Panel;
            if (panel != null) { foreach (UIElement child in panel.Children) Collect(child, values); return; }
            var decorator = node as Decorator;
            if (decorator != null) { Collect(decorator.Child, values); return; }
            var content = node as ContentControl;
            if (content != null) Collect(content.Content as DependencyObject, values);
        }

        private static void Add(MultiBinding values, object editor, string path)
        {
            values.Bindings.Add(new Binding(path) { Source = editor, Mode = BindingMode.OneWay });
        }

        private sealed class HasInputConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                return values.Any(value => value != null && value != DependencyProperty.UnsetValue && value != Binding.DoNothing &&
                    (value is string ? !string.IsNullOrWhiteSpace((string)value) : !(value is bool) || (bool)value));
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }

}
