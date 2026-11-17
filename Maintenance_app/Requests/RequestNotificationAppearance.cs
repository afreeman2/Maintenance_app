namespace Maintenance.Shared;

// وصف بصري للعملية الفعلية؛ لا نطبّق StableStage هنا حتى يبقى الإلغاء مميزًا عن الحالة التالية.
public sealed record RequestNotificationAppearance(string Title, string AccentColor, string TintColor)
{
    #region عناوين وألوان حالات طلب الصيانة الست
    public static RequestNotificationAppearance For(int operation) => operation switch
    {
        RequestProtocol.Cancelled => new("إلغاء طلب الصيانة", "#B42318", "#FEF3F2"),
        RequestProtocol.Executing => new("جاري تنفيذ الطلب", "#087F8C", "#E2F4F5"),
        RequestProtocol.ExecutionCancelled => new("إلغاء تنفيذ الطلب", "#B65C09", "#FFF2E4"),
        RequestProtocol.Completed or RequestProtocol.Closed => new("تم التنفيذ", "#26735C", "#E8F5EE"),
        RequestProtocol.CompletionCancelled => new("تم إلغاء إنهاء الصيانة", "#7B3DB8", "#F3EAFE"),
        _ => new("طلب جديد", "#3559A0", "#EDF2FA")
    };
    #endregion
}
