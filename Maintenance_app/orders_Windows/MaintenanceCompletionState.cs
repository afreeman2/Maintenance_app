using Maintenance_app.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Maintenance_app.orders_Windows;

// بيانات العرض لشاشة الإنهاء؛ تحافظ القوائم الثابتة على ترميز الشاشة الأصلية.
public sealed class MaintenanceCompletionState : INotifyPropertyChanged
{
    #region القوائم وبيانات التكليف والتقرير
    public string[] CompletionStatuses { get; } = ["منجزة", "غير منجزة", "معلقة", "قيد الإنجاز"];
    public string[] SearchStatuses { get; } = ["منجزة", "غير منجزة", "معلقة", "قيد الإنجاز", "بدون تقرير إنهاء"];
    public ObservableCollection<T_Employe> Employees { get; } = [];
    public ObservableCollection<T_Service> Services { get; } = [];
    public ObservableCollection<RepairLine> Repairs { get; } = [];
    public ObservableCollection<CompletionSearchRow> SearchResults { get; } = [];
    public string OrderNumber { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OrderDate { get; set; } = string.Empty;
    public string Technicians { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string ReportMode { get; set; } = "تقرير جديد";
    public string StatusText { get; set; } = "اعرض تكليفًا للبدء بتسجيل تقرير الإنهاء.";
    public string SearchStatus { get; set; } = "اختر شروط البحث ثم نفّذ البحث.";
    #endregion

    #region إخطار الواجهة بتحديث البيانات
    // الإخطار العام يُحدّث عناوين التكليف بعد التحميل أو التفريغ في خطوة واحدة.
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    public event PropertyChangedEventHandler? PropertyChanged;
    #endregion
}

#region بيانات الحفظ ونتائج البحث
// null في حالة البحث تعني كل الحالات؛ القيمة 4 تعني عدم وجود تقرير.
public sealed record CompletionSearchFilter(long? Number = null, DateOnly? From = null, DateOnly? To = null,
    T_Employe? Employee = null, T_Service? Service = null, int? Status = null);
public sealed record CompletionSearchRow(MaintenanceOrderKey Key, string Date, string Building, string Employees,
    string Service, string Status, string Scope);
public sealed record CompletionDraft(DateOnly Date, byte Status, string Observer, string Notes);
public sealed record CompletionDocument(T_Emergency_MaintenanceCost Order, T_FinishedMaintenance? Report);
#endregion
