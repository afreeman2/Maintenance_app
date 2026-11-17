using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using Maintenance_app.Requests;

namespace Maintenance_app.orders_Windows;

#region مفتاح التكليف ونموذج بيانات الحفظ

// المفتاح المركب الذي يميز التكليف؛ رقم التكليف وحده قد يتكرر بين المواقع والفروع.
public sealed record MaintenanceOrderKey(string Location, short LocationId, short BranchId, long Number);

// البيانات المجمعة من الواجهة قبل الحفظ، بما فيها الفنيون والبنود والمواد ورقم الطلب الاختياري.
public sealed record MaintenanceOrderDraft(
    T_Build Building, DateOnly Date, T_Employe Inspector, short Type, T_Service Service,
    IReadOnlyList<T_Employe> Technicians, IReadOnlyList<RepairLine> Repairs, IReadOnlyList<MaterialLine> Parts,
    int UserId, long? RequestId = null);

#endregion


/// <summary>
/// عمليات حفظ التكليف الأصلية باستخدام EF Core، وتشمل الرأس والملحقات والتنبيهات وحالة الطلب.
/// تنشئ كل عملية سياقًا مستقلًا، وتستخدم عمليات الكتابة معاملة لاعتماد التغييرات معًا أو التراجع عنها.
/// </summary>
public sealed class MaintenanceOrderEfStore(Func<Maintenance_DabaseContext> createContext)
{

    #region استعلامات الرأس والملحقات بالمفتاح المركب

    // تقييد استعلام الرأس بأجزاء المفتاح الأربعة؛ يعيد استعلامًا ولا ينفذ القراءة حتى await أو التعداد.
    public static IQueryable<T_Emergency_MaintenanceCost> ByKey(IQueryable<T_Emergency_MaintenanceCost> query, MaintenanceOrderKey key) =>
        query.Where(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.Emergency_MaintenanceCosts_No == key.Number);

    // بنود بيان الصيانة التابعة للتكليف داخل الموقع والفرع المحددين.
    private static IQueryable<T_Emergency_MaintenanceCosts_Service> Repairs(Maintenance_DabaseContext db, MaintenanceOrderKey key) =>
        db.T_Emergency_MaintenanceCosts_Services.Where(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.Emergency_MaintenanceCosts_No == key.Number);
    // مواد التكليف نفسه؛ استخدام النطاق الكامل يمنع تعديل مواد تكليف في موقع آخر.
    private static IQueryable<T_MaintenanceCosts_Part> Parts(Maintenance_DabaseContext db, MaintenanceOrderKey key) =>
        db.T_MaintenanceCosts_Parts.Where(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.MaintenanceCosts_No == key.Number);
    // روابط الفنيين بالتكليف، لا سجلات الموظفين الأساسية.
    private static IQueryable<T_Emp_Maint> Technicians(Maintenance_DabaseContext db, MaintenanceOrderKey key) =>
        db.T_Emp_Maints.Where(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.MaintenanceCosts_No == key.Number);

    #endregion

    #region التحقق من بيانات التكليف

    // فحص قواعد الحفظ قبل الكتابة: المستخدم والتاريخ والاختيارات ونطاق الموقع وصحة البنود والأسعار.
    public static void Validate(MaintenanceOrderDraft draft)
    {
        if (draft.UserId <= 0) throw new InvalidOperationException("يجب تسجيل الدخول قبل حفظ التكليف.");
        if (draft.Date > DateOnly.FromDateTime(DateTime.Today))
            throw new InvalidOperationException("خطأ تاريخ التكليف في المستقبل");
        if (draft.Type is < 0 or > 2) throw new InvalidOperationException("يجب إختيار نوع الصيانة");
        if (draft.Technicians.Count == 0) throw new InvalidOperationException("يجب إختار أسم الفني");
        if (draft.Repairs.Count == 0) throw new InvalidOperationException("يجب إدراج بيان صيانة واحد علي الأقل بالتكليف");
        // توحيد مقارنة الموقع والفرع لكل الاختيارات بدل قبول عناصر تحمل أرقامًا متشابهة من نطاق آخر.
        bool SameScope(string location, short locationId, short branchId) =>
            location == draft.Building.Location && locationId == draft.Building.Location_ID && branchId == draft.Building.Branch_ID;
        if (!SameScope(draft.Service.Location, draft.Service.Location_ID, draft.Service.Branch_ID)
            || !SameScope(draft.Inspector.Location, draft.Inspector.Location_ID, draft.Inspector.Branch_ID)
            || draft.Technicians.Any(x => !SameScope(x.Location, x.Location_ID, x.Branch_ID)))
            throw new InvalidOperationException("المبنى والفاحص والفنيون والخدمة يجب أن تتبع الموقع والفرع نفسيهما.");
        foreach (var line in draft.Repairs)
        {
            if (line.Manage is null || line.Section is null || string.IsNullOrWhiteSpace(line.Details))
                throw new InvalidOperationException("أكمل الإدارة والقسم وتفاصيل الصيانة.");
            if (!SameScope(line.Manage.Location, line.Manage.Location_ID, line.Manage.Branch_ID)
                || !SameScope(line.Section.Location, line.Section.Location_ID, line.Section.Branch_ID)
                || line.Section.Manag_ID != line.Manage.Manag_ID)
                throw new InvalidOperationException("القسم لا يتبع الإدارة أو موقع التكليف.");
            if (line.Details.Length > 200) throw new InvalidOperationException("تفاصيل الصيانة تتجاوز 200 حرف.");
            _ = ActionIndex(line.Action);
        }
        foreach (var line in draft.Parts)
        {
            if (string.IsNullOrWhiteSpace(line.Name) || line.Name.Length > 150 || line.Quantity <= 0 || line.Price < 0)
                throw new InvalidOperationException("راجع اسم المادة والكمية وسعر الوحدة.");
            _ = UnitIndex(line.Unit);
        }
    }

    #endregion

    #region توليد أرقام التكليفات

    // فتح سياق قراءة لعرض الرقم المتوقع في الواجهة؛ الرقم النهائي يولد مرة أخرى وقت الإدراج.
    public async Task<long> NextNumberAsync(int userId, int year)
    {
        await using var db = createContext();
        return await NextNumberAsync(db, userId, year);
    }
    // صيغة الرقم: معرّف المستخدم ثم السنة ثم تسلسل من خمس خانات؛ مثال التسلسل الأول 00001.
    private static async Task<long> NextNumberAsync(Maintenance_DabaseContext db, int userId, int year)
    {
        if (userId <= 0) throw new InvalidOperationException("يجب تسجيل الدخول قبل ترقيم التكليف.");
        string prefix = userId.ToString(CultureInfo.InvariantCulture) + year.ToString(CultureInfo.InvariantCulture);
        long? last = await db.T_Emergency_MaintenanceCosts.Where(x => x.User_ID == userId && x.Date_Modify.Year == year)
            .MaxAsync(x => (long?)x.Emergency_MaintenanceCosts_No);
        long sequence = 1;
        if (last is not null)
        {
            string text = last.Value.ToString(CultureInfo.InvariantCulture);
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length != prefix.Length + 5)
                throw new InvalidOperationException("صيغة آخر رقم تكليف لا تطابق ترقيم المستخدم والسنة؛ راجع الرقم قبل الحفظ.");
            sequence = long.Parse(text[prefix.Length..], CultureInfo.InvariantCulture) + 1;
        }
        if (sequence > 99999) throw new InvalidOperationException("تم بلوغ الحد السنوي لترقيم التكليفات.");
        return long.Parse(prefix + sequence.ToString("00000", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    #endregion

    #region تحميل التكليف والتحقق من عدم إنهائه

    // تحميل التكليف وعلاقاته للعرض دون تتبع تغييرات.
    // allowFinished يسمح بالتحميل للطباعة؛ التحميل للتعديل يرفض التكليف المنتهي.
    public async Task<T_Emergency_MaintenanceCost> LoadAsync(MaintenanceOrderKey key, bool allowFinished = false)
    {
        await using var db = createContext();
        if (!allowFinished) await EnsureUnfinishedAsync(db, key);
        return await ByKey(db.T_Emergency_MaintenanceCosts.AsNoTracking(), key)
            .Include(x => x.T_Build).Include(x => x.T_Service).Include(x => x.Branch)
            .Include(x => x.T_Emp_Maints).ThenInclude(x => x.T_Employe)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services).ThenInclude(x => x.T_Manage)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services).ThenInclude(x => x.T_Section)
            .Include(x => x.T_MaintenanceCosts_Parts)
            .AsSplitQuery().SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("رقم التكليف غير موجود.");
    }

    // رفض التعديل والحذف إذا وُجد تقرير إنهاء أو كانت حالة رأس التكليف منتهية.
    private static async Task EnsureUnfinishedAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key)
    {
        if (await db.T_FinishedMaintenances.AnyAsync(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.MaintenanceCosts_No == key.Number))
            throw new InvalidOperationException("لا يمكنك تعديل البيانات تم إنجاز الصيانة للتكليف");
        if (await ByKey(db.T_Emergency_MaintenanceCosts, key).AnyAsync(x => x.Emergency_MaintenanceCosts_ST == true))
            throw new InvalidOperationException("تم إنجاز الصيانة لهذا التكليف.");
    }

    #endregion

    #region معاملات إدراج التكليف وتعديله وحذفه

    // إدراج تكليف جديد وكل ملحقاته وتنبيهاته وربط الطلب داخل معاملة واحدة.
    public async Task<MaintenanceOrderKey> InsertAsync(MaintenanceOrderDraft draft)
    {
        Validate(draft);
        await using var db = createContext();
        // Serializable يعزل القراءة والكتابة داخل المعاملة؛ لا يعتمد أي جزء نهائيًا قبل CommitAsync.
        // التخلص من المعاملة دون Commit عند الخطأ يلغي التغييرات غير المعتمدة.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // حساب الرقم داخل معاملة الإدراج، وليس الاكتفاء بالرقم المبدئي الظاهر في النافذة.
        long number = await NextNumberAsync(db, draft.UserId, DateTime.Today.Year);
        var key = new MaintenanceOrderKey(draft.Building.Location, draft.Building.Location_ID, draft.Building.Branch_ID, number);
        if (await ByKey(db.T_Emergency_MaintenanceCosts, key).AnyAsync())
            throw new InvalidOperationException("خطأ تم إدراج تكليف صيانة بنفس الرقم");
        var order = new T_Emergency_MaintenanceCost
        {
            Emergency_MaintenanceCosts_No = number, Location = key.Location, Location_ID = key.LocationId,
            Branch_ID = key.BranchId, Emergency_MaintenanceCosts_ST = false
        };
        ApplyHeader(order, draft);
        order.User_ID = draft.UserId;
        order.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
        AddChildren(order, draft);
        db.T_Emergency_MaintenanceCosts.Add(order);
        await db.SaveChangesAsync();

        // التنبيه العام وتفاصيله جزء من عملية الإدراج نفسها؛ فشل أي خطوة قبل الاعتماد يلغي الكل.
        var notice = new T_ManNofiuser
        {
            id = (await db.T_ManNofiusers.MaxAsync(x => (long?)x.id) ?? 0) + 1,
            Branch_ID = key.BranchId, eno = number, edate = draft.Date.ToDateTime(TimeOnly.MinValue),
            estate = 1, etype = draft.Type, mtype = 0, group_type = "تكلبف الصيانة",
            dicrb = NotificationText(key, draft)
        };
        db.T_ManNofiusers.Add(notice);
        db.T_ManNotifs.Add(new T_ManNotif
        {
            Branch_ID = key.BranchId, eno = number, edate = notice.edate, estate = 1, etype = draft.Type,
            mtype = 0, not_id = notice.id, dicrb = notice.dicrb
        });
        if (draft.RequestId is not null) await MaintenanceRequestWorkflow.LinkAsync(db, draft.RequestId.Value, key);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
        return key;
    }

    // تعديل تكليف غير منتهٍ داخل نطاقه الأصلي، واستبدال ملحقاته بقوائم المسودة الحالية.
    public async Task UpdateAsync(MaintenanceOrderKey key, MaintenanceOrderDraft draft)
    {
        Validate(draft);
        if (draft.Building.Location != key.Location || draft.Building.Location_ID != key.LocationId || draft.Building.Branch_ID != key.BranchId)
            throw new InvalidOperationException("لا يمكن نقل تكليف موجود إلى موقع أو فرع آخر.");
        await using var db = createContext();
        // Serializable يعزل القراءة والكتابة داخل المعاملة؛ لا يعتمد أي جزء نهائيًا قبل CommitAsync.
        // التخلص من المعاملة دون Commit عند الخطأ يلغي التغييرات غير المعتمدة.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var order = await ByKey(db.T_Emergency_MaintenanceCosts, key).SingleAsync();
        ApplyHeader(order, draft);
        // حذف ملحقات هذا التكليف فقط؛ لا تمس هذه الأوامر جداول الإدارات أو المواد الأساسية.
        db.T_Emergency_MaintenanceCosts_Services.RemoveRange(await Repairs(db, key).ToListAsync());
        db.T_MaintenanceCosts_Parts.RemoveRange(await Parts(db, key).ToListAsync());
        db.T_Emp_Maints.RemoveRange(await Technicians(db, key).ToListAsync());
        AddChildren(order, draft);
        var notices = await db.T_ManNofiusers.Where(x => x.Branch_ID == key.BranchId && x.eno == key.Number && x.estate == 1).ToListAsync();
        foreach (var notice in notices)
        {
            notice.edate = draft.Date.ToDateTime(TimeOnly.MinValue);
            notice.etype = draft.Type; notice.mtype = 0; notice.dicrb = NotificationText(key, draft);
            foreach (var detail in await db.T_ManNotifs.Where(x => x.not_id == notice.id && x.Branch_ID == key.BranchId).ToListAsync())
            {
                detail.edate = notice.edate; detail.estate = 1; detail.etype = draft.Type;
                detail.mtype = 0; detail.dicrb = notice.dicrb;
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }

    // حذف الرأس والبنود والمواد وروابط الفنيين والتنبيهات، وإعادة الطلبات المرتبطة لحالة ما قبل الربط.
    public async Task DeleteAsync(MaintenanceOrderKey key)
    {
        await using var db = createContext();
        // Serializable يعزل القراءة والكتابة داخل المعاملة؛ لا يعتمد أي جزء نهائيًا قبل CommitAsync.
        // التخلص من المعاملة دون Commit عند الخطأ يلغي التغييرات غير المعتمدة.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var order = await ByKey(db.T_Emergency_MaintenanceCosts, key).SingleAsync();
        // حذف ملحقات هذا التكليف فقط؛ لا تمس هذه الأوامر جداول الإدارات أو المواد الأساسية.
        db.T_Emergency_MaintenanceCosts_Services.RemoveRange(await Repairs(db, key).ToListAsync());
        db.T_MaintenanceCosts_Parts.RemoveRange(await Parts(db, key).ToListAsync());
        db.T_Emp_Maints.RemoveRange(await Technicians(db, key).ToListAsync());
        db.T_ManNotifs.RemoveRange(await db.T_ManNotifs.Where(x => x.Branch_ID == key.BranchId && x.eno == key.Number).ToListAsync());
        db.T_ManNofiusers.RemoveRange(await db.T_ManNofiusers.Where(x => x.Branch_ID == key.BranchId && x.eno == key.Number).ToListAsync());
        await MaintenanceRequestWorkflow.CancelExecutionAsync(db, key);
        db.T_Emergency_MaintenanceCosts.Remove(order);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }

    #endregion

    #region إضافة وتعديل وحذف بنود الصيانة

    // حفظ بيان جديد فورًا لتكليف موجود وإعادة معرّفه لتحتفظ به الواجهة في السطر.
    public async Task<long> AddRepairAsync(MaintenanceOrderKey key, RepairLine line)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var order = await ByKey(db.T_Emergency_MaintenanceCosts, key).SingleAsync();
        var entity = ToRepair(key, line);
        order.T_Emergency_MaintenanceCosts_Services.Add(entity);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
        return entity.ID;
    }
    // تعديل سطر البيان بمعرّفه داخل التكليف المحدد بعد التحقق من عدم إنهائه.
    public async Task UpdateRepairAsync(MaintenanceOrderKey key, long id, RepairLine line)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var entity = await Repairs(db, key).SingleAsync(x => x.ID == id);
        var values = ToRepair(key, line);
        entity.Manag_ID = values.Manag_ID; entity.Secti_ID = values.Secti_ID;
        entity.Maint_Type = values.Maint_Type; entity.Statement = values.Statement; entity.Date_Modify = values.Date_Modify;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }
    // حذف سطر بيان واحد فقط، مع التحقق من تبعيته للتكليف وحالة الإنجاز.
    public async Task DeleteRepairAsync(MaintenanceOrderKey key, long id)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        db.T_Emergency_MaintenanceCosts_Services.Remove(await Repairs(db, key).SingleAsync(x => x.ID == id));
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }

    #endregion

    #region إضافة وتعديل وحذف المواد

    // حفظ مادة جديدة لتكليف موجود وإعادة المعرّف الذي ولّدته قاعدة البيانات.
    public async Task<long> AddPartAsync(MaintenanceOrderKey key, MaterialLine line)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var order = await ByKey(db.T_Emergency_MaintenanceCosts, key).SingleAsync();
        var entity = ToPart(key, line);
        order.T_MaintenanceCosts_Parts.Add(entity);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
        return entity.ID;
    }
    // التحقق من قيم المادة ثم نقل الحقول المعدلة إلى سجلها المتتبع في EF.
    public async Task UpdatePartAsync(MaintenanceOrderKey key, long id, MaterialLine line)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        var entity = await Parts(db, key).SingleAsync(x => x.ID == id);
        var values = ToPart(key, line);
        entity.Part = values.Part; entity.Quantity = values.Quantity; entity.Unit = values.Unit;
        entity.price = values.price; entity.Date_Modify = values.Date_Modify;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }
    // حذف المادة المطلوبة داخل نطاق التكليف، لا أي سجل آخر يحمل معرّفًا مشابهًا.
    public async Task DeletePartAsync(MaintenanceOrderKey key, long id)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await EnsureUnfinishedAsync(db, key);
        db.T_MaintenanceCosts_Parts.Remove(await Parts(db, key).SingleAsync(x => x.ID == id));
        await db.SaveChangesAsync(); await tx.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }

    #endregion

    #region تعبئة الرأس وإنشاء كيانات الملحقات

    // نقل حقول الرأس القابلة للتعديل؛ المستخدم وتاريخ الإدراج لا يتغيران أثناء التعديل.
    private static void ApplyHeader(T_Emergency_MaintenanceCost order, MaintenanceOrderDraft draft)
    {
        order.Build_ID = draft.Building.Build_ID; order.Date_com = draft.Date;
        order.Employe_ID = draft.Inspector.Employe_ID;
        order.Type = draft.Type; order.Serv_num = draft.Service.Serv_num;
    }
    // إضافة البنود والمواد وروابط الفنيين إلى علاقات الرأس ليتولى EF حفظها بالمفتاح نفسه.
    private static void AddChildren(T_Emergency_MaintenanceCost order, MaintenanceOrderDraft draft)
    {
        var key = new MaintenanceOrderKey(order.Location, order.Location_ID, order.Branch_ID, order.Emergency_MaintenanceCosts_No);
        foreach (var line in draft.Repairs) order.T_Emergency_MaintenanceCosts_Services.Add(ToRepair(key, line));
        foreach (var line in draft.Parts) order.T_MaintenanceCosts_Parts.Add(ToPart(key, line));
        // منع إنشاء رابطين للفني نفسه إذا تكرر اختياره في البيانات المرسلة.
        foreach (var employee in draft.Technicians.DistinctBy(x => x.Employe_ID))
            order.T_Emp_Maints.Add(new T_Emp_Maint
            {
                Location = key.Location, Location_ID = key.LocationId, Branch_ID = key.BranchId,
                MaintenanceCosts_No = key.Number, Employe_ID = employee.Employe_ID
            });
    }
    // تحويل صف البيان المعروض إلى كيان قاعدة بيانات بعد فحص التفاصيل وتبعية الإدارة والقسم.
    private static T_Emergency_MaintenanceCosts_Service ToRepair(MaintenanceOrderKey key, RepairLine line)
    {
        if (line.Manage is null || line.Section is null || string.IsNullOrWhiteSpace(line.Details) || line.Details.Length > 200)
            throw new InvalidOperationException("أكمل الإدارة والقسم وتفاصيل الصيانة (حتى 200 حرف).");
        if (line.Manage.Location != key.Location || line.Manage.Location_ID != key.LocationId || line.Manage.Branch_ID != key.BranchId
            || line.Section.Location != key.Location || line.Section.Location_ID != key.LocationId || line.Section.Branch_ID != key.BranchId
            || line.Section.Manag_ID != line.Manage.Manag_ID)
            throw new InvalidOperationException("القسم لا يتبع الإدارة أو موقع التكليف.");
        return new()
        {
        Location = key.Location, Location_ID = key.LocationId, Branch_ID = key.BranchId,
        Emergency_MaintenanceCosts_No = key.Number, Date_Modify = DateOnly.FromDateTime(DateTime.Today),
        Maint_Type = ActionIndex(line.Action), Statement = line.Details, Manag_ID = line.Manage?.Manag_ID,
        Secti_ID = line.Section?.Secti_ID, Serv_status = false
        };
    }
    // تحويل صف المادة إلى سجل حفظ والتحقق من الاسم والكمية والسعر وترميز الوحدة.
    private static T_MaintenanceCosts_Part ToPart(MaintenanceOrderKey key, MaterialLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Name) || line.Name.Length > 150 || line.Quantity <= 0 || line.Price < 0)
            throw new InvalidOperationException("راجع اسم المادة والكمية وسعر الوحدة.");
        return new()
        {
        Location = key.Location, Location_ID = key.LocationId, Branch_ID = key.BranchId,
        MaintenanceCosts_No = key.Number, Date_Modify = DateOnly.FromDateTime(DateTime.Today),
        Part = line.Name, Quantity = line.Quantity, price = line.Price, Unit = UnitIndex(line.Unit)
        };
    }

    #endregion

    #region ترميز الإجراءات والوحدات

    // ترجمة نص الإجراء إلى ترتيبه الرقمي المعتمد في القاعدة؛ تغيير ترتيب القائمة يغير معنى البيانات.
    private static short ActionIndex(string action)
    {
        string[] actions = ["صيانة", "تغيير", "تركيب", "مناولة"];
        int index = Array.IndexOf(actions, action);
        return index >= 0 ? (short)index : throw new InvalidOperationException("يجب إختيار الإجراء");
    }
    // تحويل الوحدة العربية إلى رمزها المخزن؛ ترتيب الوحدات جزء من توافق البيانات القديمة.
    private static byte UnitIndex(string unit)
    {
        string[] units = ["قطعة", "علبة", "كيلو غرام", "كيس", "متر", "سارية", "أعمال", "لتر"];
        int index = Array.IndexOf(units, unit);
        return index >= 0 ? (byte)index : throw new InvalidOperationException("يجب إختيار الوحدة");
    }

    #endregion

    #region نصوص التنبيهات وربط طلب الصيانة

    // تكوين نص التنبيه الذي يطلب تقرير إنهاء، اعتمادًا على رأس التكليف والخدمة ونوع الصيانة.
    private static string NotificationText(MaintenanceOrderKey key, MaintenanceOrderDraft draft) =>
        "   تكليف صيانة يحتاج إلي تقرير إنهاء صيانة رقم (( " + key.Number + " )) بتاريخ (("
        + draft.Date.ToString("dd-MM-yyyy") + " )) لمبني (( " + draft.Building.Build + " )) نوع الخدمات (( "
        + draft.Service.Serv_type + " )) نوع تكليف الصيانة (( " + new[] { "دورية", "وقائية", "طارئة" }[draft.Type] + " ))";

    #endregion

}
