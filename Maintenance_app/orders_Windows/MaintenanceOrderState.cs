using Maintenance_app.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Maintenance_app.orders_Windows
{
    // حاوية بيانات العرض المرتبطة بعناصر XAML؛ لا تنفذ عمليات حفظ في القاعدة.
    // ObservableCollection تبلغ الجداول بتغير الصفوف، وINotifyPropertyChanged يبلغها بتغير القيم المفردة.
    public sealed class MaintenanceOrderState : INotifyPropertyChanged
    {

        #region قوائم الاختيار وصفوف الجداول

        // قوائم الخيارات المحملة من قاعدة البيانات؛ Employees للإدخال وSearchEmployees لجميع موظفي البحث.
        public ObservableCollection<T_Build> Buildings { get; } = [];
        public ObservableCollection<T_Employe> Employees { get; } = [];
        public ObservableCollection<T_Employe> SearchEmployees { get; } = [];
        public ObservableCollection<T_Service> Services { get; } = [];
        public ObservableCollection<T_Manage> Managements { get; } = [];
        // القائمة الكاملة للأقسام؛ Departments تعرض الجزء الموافق للإدارة المختارة.
        public ObservableCollection<T_Section> AllDepartments { get; } = [];
        public ObservableCollection<T_Section> Departments { get; } = [];
        // صفوف البيان والمواد الحالية؛ تبقى مسودة محلية حتى الحفظ، أو تعكس التكليف المفتوح.
        public ObservableCollection<RepairLine> Repairs { get; } = [];
        public ObservableCollection<MaterialLine> Parts { get; } = [];
        public ObservableCollection<OrderSearchRow> SearchResults { get; } = [];

        #endregion

        #region أنواع الصيانة والإجراءات والوحدات

        // ترتيب النصوص يقابل الرموز الرقمية المخزنة في القاعدة؛ يجب الحفاظ على ترتيبها.
        public string[] DocumentTypes { get; } = ["دورية", "وقائية", "طارئة"];
        public string[] RepairActions { get; } = ["صيانة", "تغيير", "تركيب", "مناولة"];
        public string[] Units { get; } = ["قطعة", "علبة", "كيلو غرام", "كيس", "متر", "سارية", "أعمال", "لتر"];

        #endregion

        #region إجمالي المواد وحالة البحث

        // مجموع الكمية × سعر الوحدة لكل المواد؛ decimal يحافظ على دقة القيم المالية.
        public decimal TotalParts => Parts.Sum(part => part.Total);
        private string searchStatus = string.Empty;
        // النص المعروض أسفل نتائج البحث؛ إرسال PropertyChanged يجعل الواجهة تعرض النص الجديد.
        public string SearchStatus
        {
            get => searchStatus;
            set { searchStatus = value; PropertyChanged?.Invoke(this, new(nameof(SearchStatus))); }
        }

        #endregion

        #region تحديث العرض وتصفية الأقسام وإشعارات التغيير

        // عند الإضافة أو الحذف أو استبدال صف مادة، إبلاغ الواجهة بأن إجمالي المواد يحتاج إلى قراءة جديدة.
        public MaintenanceOrderState()
        {
            Parts.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(TotalParts)));
        }
        // تصفية الأقسام بمعرّف الإدارة والموقع والفرع معًا؛ null يعرض جميع الأقسام.
        public void FilterDepartments(T_Manage? management)
        {
            Departments.Clear();
            foreach (var department in AllDepartments.Where(d => management is null
                || (d.Manag_ID == management.Manag_ID && d.Location_ID == management.Location_ID
                    && d.Branch_ID == management.Branch_ID && d.Location == management.Location)))
                Departments.Add(department);
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

    }

    #region نماذج صفوف الصيانة والمواد ونتائج البحث والأحداث

    // صف بيان الصيانة؛ Id يساوي صفرًا قبل الحفظ، ثم يحمل معرّف السجل الحقيقي في القاعدة.
    public sealed record RepairLine(T_Manage? Manage, T_Section? Section, string Action, string Details, string Service, long Id = 0)
    {
        public string Management => Manage?.Manag ?? string.Empty;
        public string Department => Section?.Secti ?? string.Empty;
    }
    // صف مادة ثابت القيم؛ يستبدل بصف جديد عند التعديل حتى يصدر إشعار تحديث القائمة والإجمالي.
    public sealed record MaterialLine(string Name, decimal Quantity, string Unit, decimal Price, long Id = 0)
    {
        public decimal Total => Quantity * Price;
    }
    // صف نتيجة البحث يجمع النصوص الظاهرة مع أجزاء المفتاح اللازمة لفتح التكليف الصحيح.
    public sealed record OrderSearchRow(long Number, DateTime Date, string Building, string Employees, string Service,
        string Location, short LocationId, short BranchId);
    // نوع الإضافة المطلوبة في الحدث الاختياري LookupRequested، مثل مبنى أو إدارة أو قسم.
    public sealed class OrderActionEventArgs(string action) : EventArgs
    {
        public string Action { get; } = action;
    }

    #endregion

}
