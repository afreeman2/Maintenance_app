using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Maintenance_app.viewo_Model
{
    public static class RoundedSurface
    {
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
            "Radius", typeof(double), typeof(RoundedSurface), new PropertyMetadata(0d, Changed));
        public static void SetRadius(DependencyObject target, double value) { target.SetValue(RadiusProperty, value); }
        public static double GetRadius(DependencyObject target) { return (double)target.GetValue(RadiusProperty); }
        private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var element = target as FrameworkElement;
            if (element == null) return;
            element.SizeChanged -= Resize;
            element.SizeChanged += Resize;
            Clip(element);
        }
        private static void Resize(object sender, SizeChangedEventArgs args) { Clip((FrameworkElement)sender); }
        private static void Clip(FrameworkElement element)
        {
            var radius = GetRadius(element);
            element.Clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        }
    }
}
