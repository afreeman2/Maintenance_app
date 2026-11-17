namespace Maintenance.Shared;

// ترميز الاتفاق بين تطبيق الموظف ومنظومة الصيانة؛ state للاستلام وOP_ID لمرحلة الطلب.
public static class RequestProtocol
{
    #region العمليات الثابتة واستقرار الحالة بعد الاستلام
    public const int New = 1, Executing = 2, Completed = 3, Cancelled = 4, NoChange = 5,
        Closed = 6, ExecutionCancelled = 7, CompletionCancelled = 8;
    // يعود إلغاء الإنجاز إلى جاري التنفيذ؛ حالات التقرير 0 و1 نهائية، و2 و3 مستمرة.
    public static int AfterReceipt(int operation) => operation switch
    {
        Completed => Closed, ExecutionCancelled => New, CompletionCancelled => Executing, _ => operation
    };
    public static bool IsFinalReport(byte? state) => state is 0 or 1;
    // يمكن إعادة التكليف فور إلغاء التنفيذ إذا فُك ارتباط الطلب بالتكليف السابق.
    public static bool CanAssign(int operation, long? orderNumber) => StableStage(operation) == New && orderNumber is not > 0;
    public static int StableStage(int operation) => operation switch
    {
        ExecutionCancelled => New, CompletionCancelled => Executing, Closed => Completed, _ => operation
    };
    #endregion
}
