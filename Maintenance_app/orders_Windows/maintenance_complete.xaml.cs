using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Printing;
using DevExpress.Xpf.Printing.PreviewControl.Bars;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance_app.Dashboard;
using Maintenance_app.dataSet;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Maintenance_app.orders_Windows;

public partial class maintenance_complete : MetroWindow
{
    #region حالة النافذة والتهيئة والتحجيم
    private readonly Func<Maintenance_DabaseContext> createContext;
    private readonly MaintenanceCompletionEfStore store;
    private readonly bool customContext;
    private readonly HashSet<Border> invalidFields = [];
    private CompletionDocument? document;
    private bool busy, loaded, closed;
    // يمنع نشر نتيجة بحث قديمة بعد إغلاق اللوحة وتنظيفها.
    private long searchRevision;
    public MaintenanceCompletionState State { get; } = new();
    // يمرره مركز الإشعارات لفتح التكليف المقصود مباشرة، بلا بحث بالرقم وحده.
    public MaintenanceOrderKey? InitialOrderKey { get; set; }
    // مصدر بيانات اختياري لفحص النافذة على قاعدة معزولة عن بيانات العمل.
    public maintenance_complete() : this(null) { }
    public maintenance_complete(Func<Maintenance_DabaseContext>? contextFactory)
    {
        createContext = contextFactory ?? (() => new Maintenance_DabaseContext(
            new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString).Options));
        customContext = contextFactory is not null; store = new(createContext);
        InitializeComponent(); DataContext = State; ApplyLayout(1080);
        WindowWorkArea.Attach(this); UpdateButtons(); Closed += (_, _) => closed = true;
    }
    private bool HasConnection => customContext || !string.IsNullOrWhiteSpace(App.ConnectionString);
    private bool CanMaintain => (App.appuser ?? App.login_user) is { User_ID: > 0, fordon: true };
    private static MaintenanceOrderKey KeyOf(T_Emergency_MaintenanceCost order) => new(order.Location, order.Location_ID, order.Branch_ID, order.Emergency_MaintenanceCosts_No);
    private async void Complete_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        if (!HasConnection)
        {
            if (InitialOrderKey is not null)
            {
                UiMessages.Notify(this, "تعذّر تحميل تقرير الإنهاء؛ إعداد الاتصال غير متاح.", System.Windows.Media.Brushes.Red);
                Close();
            }
            return;
        }
        await RunAsync(async () =>
        {
            await using var db = createContext();
            foreach (var employee in await db.T_Employes.AsNoTracking().OrderBy(x => x.Emp_Name).ToListAsync()) State.Employees.Add(employee);
            foreach (var service in await db.T_Services.AsNoTracking().OrderBy(x => x.Serv_type).ToListAsync()) State.Services.Add(service);
            if (InitialOrderKey is { } key) await LoadAsync(key);
        });
        // إذا لم يعد السجل موجودًا فلا تبقى شاشة فارغة توحي بأن تكليف الإشعار حُمّل.
        if (InitialOrderKey is not null && document is null && !closed) Close();
    }
    private void CompleteLayout_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Height);
    private void ApplyLayout(double height)
    {
        // التخطيط المضغوط يبقي الجدول والخانات ظاهرة على المقاسات الأصغر.
        bool compact = height < 900;
        Resources["Order.FieldHeight"] = compact ? 32d : 40d;
        Resources["Order.FieldFont"] = compact ? 13d : 15d;
        Resources["Order.LabelFont"] = compact ? 12d : 13d;
        Resources["Order.LabelGap"] = new Thickness(0, 0, 0, compact ? 2 : 5);
        Resources["Order.GridHeaderHeight"] = compact ? 30d : 36d;
        Resources["Order.GridRowHeight"] = compact ? 30d : 38d;
    }
    private void UpdateButtons()
    {
        // صلاحية إنهاء الصيانة مستقلة عن صلاحية إنشاء التكليف.
        Insert.IsEnabled = !busy && CanMaintain && document?.Report is null;
        Edite_button.IsEnabled = delete_button.IsEnabled = !busy && CanMaintain && document?.Report is not null;
        Print_Button.IsEnabled = !busy && document is not null;
        ReportCard.IsEnabled = !busy && CanMaintain;
    }
    private async Task RunAsync(Func<Task> action, string? failureMessage = null)
    {
        if (busy) return;
        busy = true; BusyCover.Visibility = Visibility.Visible; UpdateButtons();
        try { await action(); }
        catch (Exception ex)
        {
            // يظل سبب العطل متاحًا للتشخيص، بينما تظهر رسالة عربية مناسبة للطباعة.
            System.Diagnostics.Trace.TraceError("{0}", ex);
            if (!closed) UiMessages.Notify(this, failureMessage ?? ex.GetBaseException().Message, System.Windows.Media.Brushes.Red);
        }
        finally { busy = false; if (!closed) { BusyCover.Visibility = Visibility.Collapsed; UpdateButtons(); } }
    }
    #endregion

    #region عرض التكليف والتقرير والتفريغ
    // تعبئة الشاشة من التكليف المحمل؛ تبقى بياناته الأساسية للقراءة فقط.
    public void DisplayDocument(CompletionDocument value)
    {
        ClearInputErrors(); document = value;
        var order = value.Order;
        State.OrderNumber = order.Emergency_MaintenanceCosts_No.ToString();
        State.Building = order.T_Build?.Build ?? string.Empty; State.OrderDate = order.Date_com.ToString("dd/MM/yyyy");
        State.Type = order.Type is >= 0 and < 3 ? new MaintenanceOrderState().DocumentTypes[order.Type.Value] : string.Empty;
        State.Service = order.T_Service?.Serv_type ?? string.Empty;
        State.Technicians = string.Join(" - ", order.T_Emp_Maints.OrderBy(x => x.id).Select(x => x.T_Employe?.Emp_Name));
        State.Repairs.Clear(); var actions = new MaintenanceOrderState().RepairActions;
        foreach (var line in order.T_Emergency_MaintenanceCosts_Services.OrderBy(x => x.ID))
            State.Repairs.Add(new(line.T_Manage, line.T_Section, line.Maint_Type >= 0 && line.Maint_Type < actions.Length ? actions[line.Maint_Type] : "", line.Statement, State.Service, line.ID));
        date_check.EditValue = value.Report?.DateM.ToDateTime(TimeOnly.MinValue);
        Doc_Type.EditValue = value.Report?.State is byte s && s < State.CompletionStatuses.Length ? State.CompletionStatuses[s] : null;
        Text_man.EditValue = value.Report?.observer; Text_notes.EditValue = value.Report?.notes;
        Text_emng_Search.EditValue = State.OrderNumber;
        State.ReportMode = value.Report is null ? "تقرير جديد" : CanMaintain ? "تقرير محفوظ — متاح للتعديل" : "تقرير محفوظ — للعرض";
        State.StatusText = !CanMaintain ? "عرض بيانات التكليف والتقرير؛ الحفظ والتعديل يحتاجان صلاحية إنهاء الصيانة."
            : value.Report is null ? "أكمل بيانات تقرير الإنهاء ثم اضغط حفظ." : "تم عرض التقرير المحفوظ لهذا التكليف.";
        State.Refresh(); UpdateButtons();
    }
    public async Task LoadAsync(MaintenanceOrderKey key)
    {
        // مفتاح التعديل يؤخذ من السجل المعروض، وليس من نص البحث الذي قد يغيره المستخدم.
        var result = await store.LoadAsync(key); if (!closed) DisplayDocument(result);
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        document = null;
        State.OrderNumber = State.Building = State.Type = State.OrderDate = State.Technicians = State.Service = string.Empty;
        State.Repairs.Clear(); State.SearchResults.Clear(); State.ReportMode = "تقرير جديد";
        State.StatusText = "اعرض تكليفًا للبدء بتسجيل تقرير الإنهاء.";
        foreach (var editor in new BaseEdit[] { date_check, Doc_Type, Text_man, Text_notes, Text_emng_Search }) editor.EditValue = null;
        ClearInputErrors(); State.Refresh(); UpdateButtons(); Text_emng_Search.Focus();
    }
    #endregion

    #region البحث السريع والمتقدم
    private async void Number_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        if (!e.IsRepeat) await SearchByNumberAsync(openAdvancedIfEmpty: false);
    }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await SearchByNumberAsync(openAdvancedIfEmpty: true);
    }
    // زر مستقل يبقي البحث المتقدم متاحًا حتى مع وجود رقم سابق في خانة البحث.
    private void OpenAdvanced_Click(object sender, RoutedEventArgs e) { adv_searsh.IsOpen = true; ClearInputErrors(); }
    private Task SearchByNumberAsync(bool openAdvancedIfEmpty)
    {
        // يُقرأ النص من المحرر الداخلي أثناء الكتابة؛ قد تبقى قيمة DevExpress الخارجية فارغة حتى مغادرة الخانة.
        string input = Text_emng_Search.IsKeyboardFocusWithin && Text_emng_Search.EditCore is System.Windows.Controls.TextBox editor
            ? editor.Text
            : Convert.ToString(Text_emng_Search.EditValue, CultureInfo.InvariantCulture) ?? string.Empty;
        // تُزال علامات الاتجاه المخفية التي قد ترافق الرقم المنسوخ.
        string text = string.Concat(input
            .Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.Format)
            .Select(character => character switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                _ => character
            })).Trim();
        if (openAdvancedIfEmpty && text.Length == 0)
        {
            ClearInputErrors();
            adv_searsh.IsOpen = true;
            return Task.CompletedTask;
        }
        return RunAsync(() => SearchNumberAsync(text));
    }

    private async Task SearchNumberAsync(string text)
    {
        ClearInputErrors();
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number) || number <= 0) { RejectInput(Text_emng_Search, "أدخل رقم تكليف صحيحًا."); return; }
        if (!HasConnection) { State.StatusText = "لم يتم إعداد الاتصال بقاعدة البيانات."; State.Refresh(); return; }
        long revision = searchRevision;
        var matches = await store.SearchAsync(new(Number: number));
        if (closed || revision != searchRevision) return;
        if (matches.Count == 0) { RejectInput(Text_emng_Search, "رقم التكليف غير موجود."); return; }
        if (matches.Count == 1) { await LoadAsync(matches[0].Key); return; }
        // تكرار الرقم بين المواقع يستلزم اختيار السجل صراحة من النتائج.
        SetSearchResults(matches); adv_searsh.IsOpen = true;
        State.SearchStatus = "اختر التكليف حسب الموقع والفرع، فالرقم له أكثر من نتيجة."; State.Refresh();
    }
    private async void ExecuteSearch_Click(object sender, RoutedEventArgs e) => await RunAsync(SearchAsync);
    public async Task SearchAsync()
    {
        ClearInputErrors(); DateOnly? from = null, to = null;
        if (By_Main.IsChecked == true)
        {
            if (from_date.EditValue is not DateTime start) { RejectInput(from_date, "أدخل تاريخ بداية البحث."); return; }
            if (To_date.EditValue is not DateTime end || end.Date < start.Date) { RejectInput(To_date, "اختر نهاية لا تسبق بداية البحث."); return; }
            from = DateOnly.FromDateTime(start); to = DateOnly.FromDateTime(end);
        }
        if (By_Emp.IsChecked == true && Combo_emp_Search.SelectedItem is not T_Employe) { RejectInput(Combo_emp_Search, "اختر الفني."); return; }
        if (By_Service.IsChecked == true && Combo_Services_Search.SelectedItem is not T_Service) { RejectInput(Combo_Services_Search, "اختر الخدمة."); return; }
        if (By_State.IsChecked == true && Doc_Type_Searsh.SelectedIndex < 0) { RejectInput(Doc_Type_Searsh, "اختر حالة الصيانة."); return; }
        if (!HasConnection) { State.SearchStatus = "لم يتم إعداد الاتصال بقاعدة البيانات."; State.Refresh(); return; }
        long revision = searchRevision;
        var results = await store.SearchAsync(new(From: from, To: to,
            Employee: By_Emp.IsChecked == true ? (T_Employe?)Combo_emp_Search.SelectedItem : null,
            Service: By_Service.IsChecked == true ? (T_Service?)Combo_Services_Search.SelectedItem : null,
            Status: By_State.IsChecked == true ? Doc_Type_Searsh.SelectedIndex : null));
        if (!closed && revision == searchRevision) SetSearchResults(results);
    }
    private void SetSearchResults(IEnumerable<CompletionSearchRow> rows)
    {
        State.SearchResults.Clear(); foreach (var row in rows) State.SearchResults.Add(row);
        State.SearchStatus = State.SearchResults.Count == 0 ? "لا توجد نتائج مطابقة." : $"عدد النتائج: {State.SearchResults.Count} — اختر التكليف لعرضه."; State.Refresh();
    }
    private async void OpenSearchResult_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();
    private async void SearchResult_DoubleClick(object sender, MouseButtonEventArgs e) => await OpenSelectedAsync();
    private async Task OpenSelectedAsync()
    {
        if (Search_Grid.CurrentItem is not CompletionSearchRow row) return;
        await RunAsync(async () =>
        {
            await LoadAsync(row.Key);
            if (!closed) { ResetAdvancedSearch(); adv_searsh.IsOpen = false; }
        });
    }
    // جميع وسائل الإغلاق تنظف البحث وحده وتبقي التكليف والتقرير المحمّلين.
    private void InternalFlyout_IsOpenChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Flyout { IsOpen: false } flyout && ReferenceEquals(flyout, adv_searsh)) ResetAdvancedSearch();
    }
    // تفريغ أدوات البحث ونتائجه فقط؛ لا نلمس بيانات تقرير الإنهاء أو مصادر قوائم الاختيار.
    private void ResetAdvancedSearch()
    {
        searchRevision++;
        foreach (BaseEdit field in new BaseEdit[] { from_date, To_date, Combo_emp_Search, Combo_Services_Search, Doc_Type_Searsh })
            field.EditValue = null;
        By_Main.IsChecked = By_Emp.IsChecked = By_Service.IsChecked = By_State.IsChecked = false;
        State.SearchResults.Clear(); Search_Grid.CurrentItem = null;
        State.SearchStatus = string.Empty; State.Refresh(); ClearInputErrors();
    }
    private void DismissSearch_Click(object sender, RoutedEventArgs e) { ResetAdvancedSearch(); adv_searsh.IsOpen = false; }
    // تعطيل فلتر أو إعادة تفعيله يمسح تنبيه المحاولة السابقة؛ التحقق يعاد عند تنفيذ البحث فقط.
    private void SearchFilter_Changed(object sender, RoutedEventArgs e) => ClearInputErrors();
    #endregion

    #region التحقق الفوري وأوامر الحفظ والتعديل والحذف
    // تظهر الأخطاء عند الأمر وتزول عند بدء التصحيح، دون حجز الكتابة أو التنقل.
    public CompletionDraft? ReadDraft()
    {
        ClearInputErrors();
        if (document is null) { RejectInput(Text_emng_Search, "اعرض بيانات التكليف أولًا."); return null; }
        if (date_check.EditValue is not DateTime date || date.Date == DateTime.MinValue.Date || date.Date > DateTime.Today) { RejectInput(date_check, "أدخل تاريخًا صحيحًا لا يتجاوز اليوم."); return null; }
        if (Doc_Type.SelectedIndex is < 0 or > 3) { RejectInput(Doc_Type, "اختر حالة الصيانة."); return null; }
        if (string.IsNullOrWhiteSpace(Text_man.Text) || Text_man.Text.Trim().Length > 50) { RejectInput(Text_man, "أدخل اسم الموظف المعني بالصيانة، حتى 50 حرفًا."); return null; }
        var draft = new CompletionDraft(DateOnly.FromDateTime(date), (byte)Doc_Type.SelectedIndex, Text_man.Text.Trim(), Text_notes.Text ?? "");
        MaintenanceCompletionEfStore.Validate(draft); return draft;
    }
    private async void Insert_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void Edit_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private async Task SaveAsync(bool update)
    {
        if (busy || ReadDraft() is not CompletionDraft draft || document is null) return;
        if (!CanMaintain) { UiMessages.Notify(this, "ليست لديك صلاحية إنهاء الصيانة.", System.Windows.Media.Brushes.Red); return; }
        if ((document.Report is not null) != update) { UiMessages.Notify(this, update ? "اعرض تقريرًا محفوظًا قبل التعديل." : "التقرير محفوظ بالفعل؛ استخدم تعديل."); return; }
        if (update && !UiMessages.Confirm(this, "هل تريد اعتماد تعديلات تقرير إنهاء الصيانة؟")) return;
        if (!HasConnection) { UiMessages.Notify(this, "لم يتم إعداد الاتصال بقاعدة البيانات.", System.Windows.Media.Brushes.Red); return; }
        var key = KeyOf(document.Order); long? id = document.Report?.ID;
        await RunAsync(async () =>
        {
            await store.SaveAsync(key, draft, id); await LoadAsync(key);
            if (!closed) UiMessages.Notify(this, update ? "تم تعديل تقرير إنهاء الصيانة." : "تم حفظ تقرير إنهاء الصيانة.");
        });
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || document?.Report is not T_FinishedMaintenance report || !CanMaintain) return;
        if (!UiMessages.Confirm(this, "هل تريد حذف تقرير الإنهاء وإعادة التكليف إلى حالة يحتاج تقريرًا؟")) return;
        var key = KeyOf(document.Order);
        await RunAsync(async () => { await store.DeleteAsync(key, report.ID); await LoadAsync(key); if (!closed) UiMessages.Notify(this, "تم حذف تقرير الإنهاء وإعادة فتح التكليف."); });
    }
    private static Border? InputBorder(BaseEdit editor) => (editor.Parent as FrameworkElement)?.Parent as Border;
    private void RejectInput(BaseEdit editor, string message)
    {
        var border = InputBorder(editor); var binding = border?.GetBindingExpression(TagProperty);
        if (border is not null && binding is not null) { Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(), binding, message, null)); invalidFields.Add(border); }
        // إخفاء غطاء الانتظار يسمح بوصول التركيز إلى أول خانة تحتاج تصحيحًا.
        BusyCover.Visibility = Visibility.Collapsed; editor.Focus();
    }
    private void ClearInputError(object sender)
    {
        if (sender is BaseEdit editor && InputBorder(editor) is Border border && border.GetBindingExpression(TagProperty) is { } binding) { Validation.ClearInvalid(binding); invalidFields.Remove(border); }
    }
    private void ClearInputErrors()
    {
        foreach (var border in invalidFields) if (border.GetBindingExpression(TagProperty) is { } binding) Validation.ClearInvalid(binding);
        invalidFields.Clear();
    }
    private void Input_EditValueChanged(object sender, EditValueChangedEventArgs e) => ClearInputError(sender);
    private void Input_EditValueChanging(object sender, EditValueChangingEventArgs e) => ClearInputError(sender);
    private void Input_SelectionChanged(object sender, RoutedEventArgs e) => ClearInputError(sender);
    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e) => ClearInputError(sender);
    #endregion

    #region الطباعة وأحداث النافذة والاختصارات
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (document is not { } current || busy) return;
        await RunAsync(async () =>
        {
            #region print
            // للتكليف الجديد نستخدم بيانات المسودة، وللتقرير المحفوظ نستخدم البيانات المعتمدة.
            var order = current.Order;
            var savedReport = current.Report;
            var observer = (savedReport is null ? Text_man.Text : savedReport.observer) ?? string.Empty;
            var notes = (savedReport is null ? Text_notes.Text : savedReport.notes) ?? string.Empty;
            var state = new MaintenanceOrderState();

            var technicians = order.T_Emp_Maints
                .OrderBy(x => x.id)
                .Select(x => x.T_Employe?.Emp_Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            DataSet1 dataSet = new DataSet1();

            foreach (var line in order.T_Emergency_MaintenanceCosts_Services
                                      .OrderBy(x => x.ID))
            {
                DataRow row = dataSet.DataTable1.NewRow();

                row["DataColumn29"] = order.Branch_ID == 21 ? "ادارة الشؤون الادارية والخدمات" : "قسم الشؤون الادارية والخدمات";
                row["DataColumn27"] = order.Branch_ID == 21 ? "قسم الصيانة والخدمات / وحدة شؤون الخدمات" : "وحدة الخدمات والعلاقات";
                row["DataColumn15"] = order.Branch_ID == 21 ? "رئيس وحدة شؤون الخدمات" : "رئيس وحدة الخدمات والعلاقات";
                row["DataColumn16"] = order.Branch_ID == 21 ? "رئيس قسم الخدمات والصيانة" : "رئيس قسم الشؤون الإدارية والخدمات";
                row["DataColumn26"] = order.Branch_ID == 21 ? "الادارة او القسم" : "القسم او الوحدة";
                row["DataColumn25"] = order.Branch_ID == 21 ? "القسم او الوحدة" : "القسم او الوحدة";
                row["DataColumn24"] = observer;
                row["DataColumn1"] = order.T_Service?.Serv_type ?? string.Empty;
                row["DataColumn28"] = notes;
                row["DataColumn30"] = order.T_Employe?.Emp_Name ?? string.Empty;
              row["DataColumn32"] =
                   order.Emergency_MaintenanceCosts_No;

                row["DataColumn12"] =
                   order.Type is >= 0 and < 3
                        ? state.DocumentTypes[order.Type.Value]
                        : string.Empty;

                row["DataColumn14"] =
                   order.Branch?.Branch ??
                    App.Branch_Name ??
                    string.Empty;

                row["DataColumn7"] = order.Location;

                row["DataColumn8"] = order.T_Build?.Build ?? string.Empty;

                row["DataColumn9"] =
                    technicians.Count == 0
                        ? ""
                        : $"{(technicians.Count > 1 ? "الفنيون:" : "الفني:")} " +
                          $"{string.Join(" و ", technicians)} "
                           ;

                row["DataColumn2"] =
                    line.Maint_Type is >= 0 and < 4
                        ? state.RepairActions[line.Maint_Type]
                        : string.Empty;

                row["DataColumn3"] = line.Statement;

                row["DataColumn4"] =
                    line.T_Section?.Secti ?? string.Empty;

                row["DataColumn5"] =
                    line.T_Manage?.Manag ?? string.Empty;

                row["DataColumn20"] =
                   order.Date_com.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture);

                // إضافة الصف إلى الجدول
                dataSet.DataTable1.Rows.Add(row);
            }


            // مسار التقرير الرئيسي
            string mainReportPath = Path.Combine(
                AppContext.BaseDirectory,
                "Reports",
                "Done_Mainte_Report.repx"
            );

          

            // التحقق من وجود التقرير الرئيسي
            if (!File.Exists(mainReportPath))
            {
                throw new FileNotFoundException(
                    "ملف تقرير إنهاء الصيانة غير موجود",
                    mainReportPath
                );
            }

            // تحميل التقرير الرئيسي
            XtraReport rb = XtraReport.FromFile(
                mainReportPath,
                true
            );

            // تعبئة التقرير الرئيسي
            rb.DataSource = dataSet;
            rb.DataMember = "DataTable1";
            rb.RequestParameters = false;

        

          

              

               

            
            


            var preview = new DocumentPreviewWindow();






            preview.WindowState = WindowState.Maximized;
            preview.Title = "نموذج انهاء صيانة";
            var provider = new DocumentCommandProvider();

            #region إزالة
            provider.RibbonActions.Add(
                new RemoveAction()
                {
                    ElementName = DefaultPreviewBarItemNames.Find
                }
            );
            provider.RibbonActions.Add(
              new RemoveAction()
              {
                  ElementName = DefaultPreviewBarItemNames.DocumentMap
              }
            );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.Watermark
             }
           );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.NavigationPane
             }
           );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.Open
             }
           );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.PrintDirect
             }
           );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.PageLayoutSplit
             }
           );
            provider.RibbonActions.Add(
             new RemoveAction()
             {
                 ElementName = DefaultPreviewBarItemNames.Parameters
             }
           );
            provider.RibbonActions.Add(
            new RemoveAction()
            {
                ElementName = DefaultPreviewBarItemNames.Scale
            }
          );
            provider.RibbonActions.Add(
           new RemoveAction()
           {
               ElementName = DefaultPreviewBarItemNames.ShowCoverPage
           }
         );


            provider.RibbonActions.Add(
          new RemoveAction()
          {
              ElementName = DefaultPreviewBarItemNames.PageSetup
          }
        );
            provider.RibbonActions.Add(
        new RemoveAction()
        {
            ElementName = DefaultPreviewBarItemNames.EditingFields
        }
      );
            provider.RibbonActions.Add(
     new RemoveAction()
     {
         ElementName = DefaultPreviewBarItemNames.ContinuousScrolling
     }
    );
            // تعيينه للـ PreviewControl
            preview.PreviewControl.CommandProvider = provider;
            #endregion
            // تجهّز الصفحات قبل عرضها ويُحرر التقرير عند إغلاق نافذته.
            try
            {
                await Task.Run(() => rb.CreateDocument(false));
                if (closed)
                {
                    rb.Dispose();
                    return;
                }

                if (IsVisible) preview.Owner = this;
                ThemeManager.SetThemeName(preview, "Win11Light");
                preview.PreviewControl.DocumentSource = rb;
                preview.Closed += (_, _) =>
                {
                    preview.PreviewControl.DocumentSource = null;
                    rb.Dispose();
                };
                BusyCover.Visibility = Visibility.Collapsed;
                preview.Show();
            }
            catch
            {
                rb.Dispose();
                throw;
            }
            #endregion
        }, "تعذّرت معاينة تقرير إنهاء الصيانة.");
    }

    private void Complete_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !busy) { DismissSearch_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.F3 && !busy) { adv_searsh.IsOpen = true; e.Handled = true; }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) { if (!busy) Close(); }
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) Maximize_Click(sender, e);
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    #endregion
}
