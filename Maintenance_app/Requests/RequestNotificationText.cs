namespace Maintenance.Shared;

// نصوص الطرفين مستقلة؛ رقم الطلب يبقى مفتاحًا داخليًا ولا يدخل نص التنبيه.
public static class RequestNotificationText
{
    #region الحالة ونص المنظومة ونص تطبيق العميل
    public static string Status(int operation) => operation switch
    {
        4 => "إلغاء طلب الصيانة", 2 => "جاري تنفيذ الطلب", 7 => "إلغاء تنفيذ الطلب",
        3 or 6 => "تم التنفيذ", 8 => "تم إلغاء إنهاء الصيانة", _ => "طلب جديد"
    };
    public static string Manager(int operation, string? customer, string? management, string? service)
    {
        string name = Value(customer, "العميل"), department = Value(management, "الجهة المسجلة"), type = Value(service, "الخدمة المطلوبة");
        string prefix = operation switch
        {
            4 => "تم إلغاء الطلب من ", 2 => "جاري تنفيذ طلب الصيانة الخاص بـ ",
            7 => "تم إلغاء تنفيذ طلب الصيانة الخاص بـ ", 3 or 6 => "تم تنفيذ طلب الصيانة الخاص بـ ",
            8 => "تم إلغاء إنهاء طلب الصيانة الخاص بـ ", _ => "ورود طلب صيانة جديد من "
        };
        return prefix + name + " بـ" + department + " بشأن " + type;
    }
    public static string Client(int operation) => operation switch
    {
        4 => "تم إلغاء طلب الصيانة الخاص بكم",
        2 => "جاري الآن تنفيذ طلب الصيانة الخاص بكم",
        7 => "تم إلغاء طلب الصيانة الخاص بكم",
        3 or 6 => "تم تنفيذ طلب الصيانة الخاص بكم",
        8 => "تم إلغاء تنفيذ طلب الصيانة الخاص بكم",
        _ => "تم إرسال طلبك إلى القسم المختص"
    };
    private static string Value(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    #endregion
}
