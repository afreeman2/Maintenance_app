namespace Maintenance_app.Notifications;

// إشارة محلية بعد اعتماد الكتابة؛ تحديث الأجهزة الأخرى يأتي من قراءة القاعدة كل ثانية.
public static class MaintenanceNotificationUpdates
{
    #region نشر التغيير المعتمد دون التأثير على نتيجة الحفظ
    public static event EventHandler? Changed;

    public static void Publish()
    {
        if (Changed is null) return;
        foreach (EventHandler listener in Changed.GetInvocationList())
        {
            // فشل تحديث نافذة بعد Commit لا يحول عملية حفظ ناجحة إلى رسالة فشل مضللة.
            try { listener(null, EventArgs.Empty); }
            catch (Exception) { }
        }
    }
    #endregion
}
