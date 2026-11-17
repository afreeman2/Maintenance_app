using MahApps.Metro.IconPacks;
using System.Globalization;
using System.Windows.Data;

namespace Maintenance_app.converts;

// يختار رمزًا للخيارات الثابتة دون استبدال النص أو تغيير ترتيب القيم المخزنة.
public sealed class StaticChoiceIconConverter : IValueConverter
{
    #region رموز الخيارات الثابتة
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value?.ToString() switch
        {
            "دورية" => "CalendarClock", "وقائية" => "ShieldCheckOutline", "طارئة" => "AlertOutline",
            "صيانة" => "Wrench", "تغيير" => "SwapHorizontal", "تركيب" => "Tools", "مناولة" => "HandTruck",
            "قطعة" => "CubeOutline", "علبة" => "PackageVariantClosed", "كيلو غرام" => "WeightKilogram",
            "كيس" => "Sack", "متر" => "Ruler", "سارية" => "FlagOutline", "أعمال" => "ClipboardTextOutline", "لتر" => "WaterOutline",
            "منجزة" => "CheckCircleOutline", "غير منجزة" => "CloseCircleOutline", "معلقة" => "PauseCircleOutline", "معلّقة" => "PauseCircleOutline",
            "قيد الإنجاز" => "ProgressWrench", "بدون تقرير إنهاء" => "FileDocumentOutline",
            "كل الإشعارات" => "BellOutline", "غير المقروءة" => "BellBadgeOutline", "بلا تقرير إنهاء" => "FileDocumentOutline",
            "معلّقة أو قيد الإنجاز" => "ProgressWrench", "منذ 7 أيام فأكثر" => "ClockAlertOutline",
            "إنهاء صيانة معلّق" => "PauseCircleOutline", "إنهاء صيانة قيد الإنجاز" => "ProgressWrench",
            "طلبات الصيانة" => "ClipboardTextOutline", "طلبات صيانة واردة" => "InboxArrowDown",
            "طلبات جاري تنفيذها" => "ProgressWrench", "طلبات تم تنفيذها" => "ClipboardCheckOutline", "طلبات ألغيت من العميل" => "Cancel",
            "PDF" => "FilePdfBox", "Excel" => "FileExcelOutline", "Word" => "FileWordOutline",
            "طباعة التكليف المحدد" => "PrinterOutline", "طباعة تقرير الإنهاء المحدد" => "FileDocumentCheckOutline",
            "كشف تكاليف الصيانة الحالي" => "PrinterOutline", "كشف تكاليف الصيانة بحسب التاريخ" => "CalendarSearch",
            "كشف تكاليف الصيانة بحسب المبنى" => "OfficeBuildingOutline", "ربع السنة" => "CalendarRange",
            "طباعة رؤوس التكليفات كجدول واحد" => "Table",
            "طباعة كشف مقسم حسب الفرع" => "SourceBranch",
            "طباعة رؤوس التكليفات مقسمة حسب الفرع" => "Table",
            "نصف السنة" => "CalendarRange", "ثلاث أربع السنة" => "CalendarRange", "سنة كاملة" => "CalendarCheckOutline",
            _ => "FormatListBulleted"
        };
        // رمز احتياطي يحافظ على ظهور القائمة إذا تغيرت مكتبة الأيقونات لاحقًا.
        return Enum.TryParse<PackIconMaterialKind>(name, out var kind) ? kind : PackIconMaterialKind.Wrench;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    #endregion
}
