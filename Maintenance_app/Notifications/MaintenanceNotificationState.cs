using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;

namespace Maintenance_app.Notifications;

// قوائم العرض وحالة المشاهدة فقط؛ لا يعني فتح إشعار أو قراءته إنهاء الصيانة.
public sealed class MaintenanceNotificationState : INotifyPropertyChanged
{
    #region بيانات العرض والبحث والتصفية

    public ObservableCollection<MaintenanceNotice> Items { get; } = new ObservableCollection<MaintenanceNotice>();
    public ICollectionView View { get; }
    // إضافة الفلترين في النهاية تحافظ على فهارس الفلاتر السابقة وعملها.
    public string[] Filters { get; } = { "كل الإشعارات", "غير المقروءة", "بلا تقرير إنهاء", "معلّقة أو قيد الإنجاز", "منذ 7 أيام فأكثر", MaintenanceNotice.SuspendedGroup, MaintenanceNotice.InProgressGroup, "طلبات الصيانة", "طلبات صيانة واردة", "طلبات جاري تنفيذها", "طلبات تم تنفيذها", "طلبات ألغيت من العميل" };
    public int Total => Items.Count;
    public int Unread => Items.Count(item => item.IsUnread);
    public int NeedsReportCount => Items.Count(item => !item.IsRequest && item.NeedsReport);
    public int PendingReportCount => Items.Count(item => !item.IsRequest && !item.NeedsReport);
    public int SuspendedReportCount => Items.Count(item => !item.IsRequest && !item.NeedsReport && item.ReportState == 2);
    public int InProgressReportCount => Items.Count(item => !item.IsRequest && !item.NeedsReport && item.ReportState == 3);
    private bool canManageRequests;
    public bool CanManageRequests
    {
        get => canManageRequests;
        set { if (canManageRequests == value) return; canManageRequests = value; Changed(nameof(CanManageRequests)); }
    }
    public string BadgeText => Unread > 99 ? "99+" : Unread.ToString();
    public bool HasUnread => Unread > 0;
    public string ResultLabel => "عرض " + View.Cast<object>().Count() + " من " + Total;
    public string Status { get; private set; } = "افتح المركز لتحميل إشعارات الصيانة.";
    public bool IsBusy { get; private set; }
    public bool HasError { get; private set; }
    public bool CanView { get; private set; }
    public bool ToastsEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool HasSnapshot { get; private set; }
    private string search = string.Empty;
    private int filter;
    private MaintenanceNotice? selected;
    private HashSet<string> read = new HashSet<string>();
    private string? readFile;
    private int userId;
    public string Search
    {
        get => search;
        set { search = value ?? string.Empty; RefreshView(); Changed(nameof(Search)); }
    }
    public int Filter
    {
        get => filter;
        set { filter = value; RefreshView(); Changed(nameof(Filter)); }
    }
    public MaintenanceNotice? Selected
    {
        get => selected;
        set { selected = value; Changed(nameof(Selected)); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MaintenanceNotificationState()
    {
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Matches;
        // ترتيب المجموعات ثابت، ثم الأقدم فالأحدث داخل كل مجموعة، دون تغيير هوية القراءة.
        View.SortDescriptions.Add(new SortDescription(nameof(MaintenanceNotice.GroupOrder), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(MaintenanceNotice.Date), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(MaintenanceNotice.Number), ListSortDirection.Ascending));
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MaintenanceNotice.GroupName)));
    }

    private bool Matches(object value)
    {
        MaintenanceNotice row = (MaintenanceNotice)value;
        if (Filter == 1 && !row.IsUnread || Filter == 2 && !row.NeedsReport
            || Filter == 3 && row.NeedsReport || Filter == 4 && row.DaysWaiting < 7
            || Filter == 5 && (row.NeedsReport || row.ReportState != 2)
            || Filter == 6 && (row.NeedsReport || row.ReportState != 3)
            || Filter is 2 or 3 or 5 or 6 && row.IsRequest
            || Filter == 7 && !row.IsRequest || Filter == 8 && row.GroupName != "طلبات صيانة واردة"
            || Filter == 9 && row.GroupName != "طلبات جاري تنفيذها" || Filter == 10 && row.GroupName != "طلبات تم تنفيذها"
            || Filter == 11 && row.GroupName != "طلبات ألغيت من العميل")
        {
            return false;
        }
        string text = Search.Trim();
        return text.Length == 0 || (row.Number + " " + row.Building + " " + row.Service + " " + row.Employees
            + " " + row.Location + " " + row.Branch + " " + row.GroupName).Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshView()
    {
        View.Refresh();
        Changed(nameof(ResultLabel));
    }

    private void Changed(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion

    #region دورة التحميل وإزالة الإشعارات المنتهية دون فقد حالة المشاهدة

    public void Configure(int id, bool allowed, string? storageDirectory = null)
    {
        bool previouslyAllowed = CanView;
        CanView = allowed;
        bool reconfigured = userId != id || !allowed || !previouslyAllowed;
        if (reconfigured)
        {
            userId = id;
            Items.Clear();
            Selected = null;
            HasSnapshot = false;
            IsBusy = false;
            HasError = false;
            read.Clear();
            readFile = null;
            if (allowed)
            {
                string directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maintenance_app", "Notifications");
                readFile = Path.Combine(directory, "read-" + id + ".json");
                try
                {
                    if (File.Exists(readFile)) read = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(readFile)) ?? new HashSet<string>();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { }
            }
        }
        if (!allowed) Status = "لا توجد صلاحية للاطلاع على إشعارات الصيانة.";
        // الجلسة نفسها لا تعيد تجميع القائمة كل ثانية أثناء فحص الاتصال.
        if (reconfigured) NotifyCounts();
    }

    public void BeginLoad()
    {
        IsBusy = true;
        Changed(nameof(IsBusy));
    }

    public List<MaintenanceNotice> Apply(IEnumerable<MaintenanceNotice> snapshot)
    {
        HashSet<string> previous = Items.Select(item => item.Identity).ToHashSet();
        HashSet<string> previousDelivery = Items.Select(item => item.DeliveryIdentity).ToHashSet();
        string? selectedId = Selected?.Identity;
        List<MaintenanceNotice> added = new List<MaintenanceNotice>();
        // هوية كاملة لكل مرحلة تمنع دمج تكليفين من موقعين مختلفين أو تكرار الإشعار نفسه.
        var source = snapshot.Where(item => !read.Contains("hidden:" + item.DeliveryIdentity)).ToList();
        // العناصر الموسومة صراحة بأنها عابرة مستقلة عن بطاقات الإلغاء التي تبقى للحذف اليدوي.
        var transient = source.Where(item => item.IsTransient && !read.Contains(item.Identity)).DistinctBy(item => item.Identity).ToList();
        List<MaintenanceNotice> next = source.Where(item => !item.IsTransient).DistinctBy(item => item.Identity).OrderBy(item => item.Date).ThenBy(item => item.Number)
            .ThenBy(item => item.Location, StringComparer.Ordinal).ThenBy(item => item.LocationId).ThenBy(item => item.BranchId).ToList();
        // التحديث الدوري لا يعيد إنشاء البطاقات والمجموعات عندما لم تتغير بيانات العمل.
        bool unchanged = Items.Count == next.Count && Items.Zip(next).All(pair =>
            pair.First.Identity == pair.Second.Identity && pair.First.Date == pair.Second.Date
            && pair.First.Building == pair.Second.Building && pair.First.Service == pair.Second.Service
            && pair.First.Branch == pair.Second.Branch && pair.First.Employees == pair.Second.Employees
            && pair.First.Notes == pair.Second.Notes && pair.First.RequestOperation == pair.Second.RequestOperation
            && pair.First.EventOperation == pair.Second.EventOperation && pair.First.RequestRevision == pair.Second.RequestRevision
            && pair.First.RequestHasOrder == pair.Second.RequestHasOrder && pair.First.AssignedOrderNumber == pair.Second.AssignedOrderNumber);
        if (unchanged && HasSnapshot)
        {
            for (int i = 0; i < Items.Count; i++) Items[i].RequestDeliveryState = next[i].RequestDeliveryState;
            // المشاهدة تخص هذه الجلسة والمستخدم حتى لو أعاد مصدر خارجي استخدام كائنات الإشعار نفسها.
            bool readChanged = false;
            foreach (var item in Items)
            {
                bool unread = !read.Contains(item.Identity);
                if (item.IsUnread == unread) continue;
                item.IsUnread = unread; readChanged = true;
            }
            HasError = false; IsBusy = false; UpdateLoadStatus();
            if (readChanged) NotifyCounts();
            else { Changed(nameof(Status)); Changed(nameof(HasError)); Changed(nameof(IsBusy)); }
            return transient;
        }
        Items.Clear();
        foreach (MaintenanceNotice item in next)
        {
            item.IsUnread = !read.Contains(item.Identity);
            Items.Add(item);
            // إذا توقف التطبيق قبل نهاية تنبيه الإنجاز، نعيد عرضه ليكتمل الإخفاء بدل ترك بطاقة عالقة.
            bool needsFinalDisplay = item.EventOperation is 3 or 6;
            bool newRequestEvent = item.IsRequest && !previousDelivery.Contains(item.DeliveryIdentity) && (needsFinalDisplay || !WasTransportDelivered(item));
            if (newRequestEvent || !item.IsRequest && item.IsUnread && !previous.Contains(item.Identity)) added.Add(item);
        }
        Selected = Items.FirstOrDefault(item => item.Identity == selectedId);
        HasSnapshot = true;
        HasError = false;
        IsBusy = false;
        UpdateLoadStatus();
        NotifyCounts();
        added.AddRange(transient);
        return added;
    }

    // التوقيت المعلن يطابق دورة القراءة الفعلية، والقراءة لا تغيّر حالة إنجاز التكليف.
    private void UpdateLoadStatus() => Status = Total == 0 ? "لا توجد طلبات أو تكليفات تحتاج متابعة حاليًا."
        : "آخر تحديث " + DateTime.Now.ToString("HH:mm:ss") + " • تحديث تلقائي كل ثانية";

    public void Fail()
    {
        IsBusy = false;
        HasError = true;
        Status = "تعذّر التحديث. البيانات السابقة قديمة؛ تحقق من الاتصال ثم أعد المحاولة.";
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        foreach (string name in new[] { nameof(Total), nameof(Unread), nameof(NeedsReportCount), nameof(PendingReportCount), nameof(SuspendedReportCount), nameof(InProgressReportCount), nameof(BadgeText), nameof(HasUnread), nameof(Status), nameof(IsBusy), nameof(HasError), nameof(CanView) }) Changed(name);
        RefreshView();
    }

    #endregion

    #region حفظ المشاهدة محليًا لكل مستخدم دون إخفاء التكليف أو تعديل القاعدة

    public void MarkRead(MaintenanceNotice row)
    {
        read.Add(row.Identity);
        row.IsUnread = false;
        SaveRead();
        NotifyCounts();
    }

    // يسجل التنبيه الملغى بعد بدء عرضه فقط؛ القراءة العادية لبقية البطاقات تبقى مستقلة.
    public void MarkTransientDelivered(IEnumerable<MaintenanceNotice> displayed)
    {
        foreach (var row in displayed.Where(x => x.IsTransient)) read.Add(row.Identity);
        SaveRead();
    }
    // إيصال عرض التنبيه مستقل عن قراءة البطاقة؛ يبقى لإعادة تأكيد الاستلام عند انقطاع الشبكة.
    public void MarkTransportDelivered(IEnumerable<MaintenanceNotice> displayed)
    {
        foreach (var row in displayed.Where(x => x.IsRequest)) read.Add("delivered:" + row.DeliveryIdentity);
        SaveRead();
    }
    public bool WasTransportDelivered(MaintenanceNotice row) => read.Contains("delivered:" + row.DeliveryIdentity);

    // تنتهي بطاقة الإنجاز في هذا الحساب بعد انتهاء تنبيهها؛ تبقى بيانات الطلب للعميل ولإلغاء الإنجاز لاحقًا.
    public void HideCompletedRequests(IEnumerable<MaintenanceNotice> displayed)
    {
        foreach (var row in displayed.Where(x => x.IsRequest && x.EventOperation is 3 or 6))
        {
            read.Add("hidden:" + row.DeliveryIdentity);
            foreach (var item in Items.Where(x => x.DeliveryIdentity == row.DeliveryIdentity).ToArray()) Items.Remove(item);
            if (Selected?.DeliveryIdentity == row.DeliveryIdentity) Selected = null;
        }
        SaveRead(); NotifyCounts();
    }

    public void MarkAllRead()
    {
        foreach (MaintenanceNotice row in Items)
        {
            read.Add(row.Identity);
            row.IsUnread = false;
        }
        SaveRead();
        NotifyCounts();
    }

    private void SaveRead()
    {
        if (readFile == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(readFile)!);
            string temporary = readFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(read));
            File.Move(temporary, readFile, true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Status = "تمت المشاهدة في هذه الجلسة؛ تعذّر حفظها محليًا.";
        }
    }

    #endregion
}
