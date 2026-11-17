using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using MessageBoxOptions = System.Windows.MessageBoxOptions;
using Point = System.Windows.Point;

namespace Maintenance_app.Dashboard
{
    public static class UiMessages
    {
        private static bool themeInitialized;
        public static void InitializeTheme()
        {
            if (themeInitialized) return;
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            themeInitialized = true;
        }
        public static MessageBoxResult Show(string text, string title = "منظومة المعاملات الواردة",
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
        {
            var owner = Application.Current == null ? null : Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);
            return Display(owner, title, text, buttons, icon, null, null);
        }

        #region رسائل النوافذ ورسائل بدء التشغيل
        // تحديد النافذة المالكة عند توفرها، مع استعمال الأزرار العربية حتى قبل فتح النافذة الرئيسية.
        public static MessageBoxResult Show(Window? owner, string title, string text,
            MessageBoxButton buttons, MessageBoxImage icon) =>
            Display(owner, title, text, buttons, icon, null, null);
        #endregion

        public static void Notify(Window owner, object text, Brush foreground = null)
        {
            var color = foreground as SolidColorBrush;
            bool error = color != null && color.Color.R > 150 && color.Color.G < 100 && color.Color.B < 100;
            Display(owner, owner.Title, Convert.ToString(text), MessageBoxButton.OK, error ? MessageBoxImage.Warning : MessageBoxImage.Information, null, null);
        }

        public static bool Confirm(Window owner, string text, bool closing = false)
        {
            return Display(owner, closing ? "إغلاق المنظومة" : "تأكيد العملية", text,
                MessageBoxButton.YesNo, closing ? MessageBoxImage.Question : MessageBoxImage.Warning,
                closing ? "إغلاق المنظومة" : "نعم، متابعة", closing ? "متابعة العمل" : "إلغاء") == MessageBoxResult.Yes;
        }

        private static MessageBoxResult Display(Window? owner, string title, string? text,
            MessageBoxButton buttons, MessageBoxImage icon, string? acceptCaption, string? cancelCaption)
        {
            // Keep the live owner's theme unchanged. Reapplying an application or
            // owner theme here rebuilds ItemsControl containers and detaches their
            // automation peers while the modal dialog is opening.
            var commands = new System.Collections.Generic.List<UICommand>();
            if (buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel)
            {
                commands.Add(new UICommand { Id = MessageBoxResult.Yes, Caption = acceptCaption ?? "نعم" });
                commands.Add(new UICommand { Id = MessageBoxResult.No, Caption = cancelCaption ?? "لا", IsDefault = true, IsCancel = buttons == MessageBoxButton.YesNo });
            }
            else commands.Add(new UICommand { Id = MessageBoxResult.OK, Caption = "موافق", IsDefault = true });
            if (buttons == MessageBoxButton.OKCancel || buttons == MessageBoxButton.YesNoCancel)
                commands.Add(new UICommand { Id = MessageBoxResult.Cancel, Caption = "إلغاء", IsCancel = true });
            if (buttons == MessageBoxButton.OK) commands[0].IsCancel = true;

            var content = new TextBlock
            {
                Text = text ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 16,
                LineHeight = 28,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 63, 67)),
                MinWidth = 330,
                MaxWidth = 510,
                Margin = new Thickness(12, 8, 12, 8),
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right
            };
            var body = new ScrollViewer
            {
                Content = content,
                MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height * 0.55),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            ThemeManager.SetThemeName(body, "Win11Light");
            var selected = ThemedMessageBox.Show(messageBoxButtons: commands, owner: owner != null && owner.IsVisible ? owner : null,
                title: title, messageContent: body, image: CreateIcon(icon),
                options: MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign,
                windowStartupLocation: owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                titleAlignment: WindowTitleAlignment.Right);
            return selected == null ? MessageBoxResult.Cancel : (MessageBoxResult)selected.Id;
        }

        private static ImageSource CreateIcon(MessageBoxImage icon)
        {
            bool error = icon == MessageBoxImage.Error;
            bool warning = icon == MessageBoxImage.Warning;
            var ink = (SolidColorBrush)new BrushConverter().ConvertFromString(error ? "#AF3C42" : warning ? "#8B6C2E" : "#3559A0");
            var tint = (SolidColorBrush)new BrushConverter().ConvertFromString(error ? "#FDEDEC" : warning ? "#FBF5E7" : "#EDF1FA");
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(tint, null, new RectangleGeometry(new Rect(0, 0, 48, 48), 14, 14)));
            string symbol = error || warning ? "!" : icon == MessageBoxImage.Question ? "?" : "✓";
            var text = new FormattedText(symbol, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 30, ink);
            drawing.Children.Add(new GeometryDrawing(ink, null, text.BuildGeometry(new Point((48 - text.Width) / 2, (48 - text.Height) / 2))));
            return new DrawingImage(drawing);
        }
    }

}
