using DevExpress.Xpf.Editors;
using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using MahApps.Metro.Controls;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Maintenance_app.support_Data;

// نافذة بيانات مساعدة مستقلة؛ النوع يحدد مصدرًا واحدًا فقط ولا يجمعه مع تبويبات أخرى.
public partial class MaintenanceSupportWindow : MetroWindow
{
    #region الحقول والتهيئة
    private enum SupportKind { Buildings, Services, Employees, Managements, Sections }

    // نخفي النطاق من الواجهة ونحتفظ بمفاتيحه حتى يُحفظ كل سجل في مكانه الصحيح.
    private sealed record SupportRow(string Name, string Details, string Location, short LocationId, short BranchId, int Identifier, int? ParentId);
    private sealed record ManagerChoice(T_Manage Manager, string Display);
    private T_Location? defaultLocation;
    private List<T_Manage> managers = [];
    private readonly Func<Maintenance_DabaseContext> createContext;
    private readonly SupportKind kind;
    private bool loading;
    private SupportRow? selectedRow;
    private static readonly Brush EmptyInputBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush FilledInputBrush = new SolidColorBrush(Color.FromRgb(232, 240, 252));

    public MaintenanceSupportWindow(Func<Maintenance_DabaseContext>? contextFactory, int initialTab = 0)
    {
        createContext = contextFactory ?? (() => new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString).Options));
        kind = (SupportKind)Math.Clamp(initialTab, 0, 4);
        InitializeComponent();
        ConfigureScreen();
    }
    #endregion

    #region ضبط الواجهة والتحميل
    // تضبط عناوين وحقول النافذة قبل التحميل، حتى تبقى كل واجهة متخصصة في نوع بيانات واحد.
    private void ConfigureScreen()
    {
        (string title, string subtitle, string label, string details) = kind switch
        {
            SupportKind.Buildings => ("المباني", "إدراج وتعديل مباني التكليف وعناوينها", "اسم المبنى", "العنوان"),
            SupportKind.Services => ("أنواع الخدمات", "إدراج وتعديل الخدمات الظاهرة في التكليف", "نوع الخدمات", ""),
            SupportKind.Employees => ("الفنيون", "إدراج وتعديل أسماء الفنيين المكلفين", "اسم الفني", ""),
            SupportKind.Managements => ("الإدارات والأقسام", "إدراج وتعديل الإدارات المستخدمة في التكليف", "اسم الإدارة أو القسم", ""),
            _ => ("الأقسام والوحدات", "إدراج وتعديل الأقسام والوحدات وربطها بالإدارة", "اسم القسم أو الوحدة", "يتبع الإدارة")
        };
        Title = title;
        // تخصص مساحة إضافية للأنواع ذات الحقل الثاني فقط.
        (Width, Height) = kind switch
        {
            SupportKind.Sections or SupportKind.Buildings => (720d, 800d),
            _ => (600d, 800d)
        };
        ScreenTitle.Text = title;
        ScreenSubtitle.Text = subtitle;
        NameLabel.Text = AddNameLabel.Text = label;
        if (kind is not (SupportKind.Buildings or SupportKind.Sections))
            AddDetailsWidth.Width = EditDetailsWidth.Width = new GridLength(0);
        DetailsColumn.Header = details;
        AddAddressPanel.Visibility = AddressPanel.Visibility = kind == SupportKind.Buildings ? Visibility.Visible : Visibility.Collapsed;
        AddManagementPanel.Visibility = ManagementPanel.Visibility = kind == SupportKind.Sections ? Visibility.Visible : Visibility.Collapsed;
        DetailsColumn.Visible = kind is SupportKind.Buildings or SupportKind.Sections;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // نربط الإضافة بنطاق الحساب عندما يطابق موقعًا واحدًا، أو بالموقع الوحيد إذا كانت القاعدة أحادية الموقع.
    private async Task LoadAsync()
    {
        if (loading) return;
        loading = true;
        try
        {
            SupportStatus.Text = "جارِ تحميل البيانات…";
            await using var db = createContext();
            var locations = await db.T_Locations.AsNoTracking().ToListAsync();
            var accountLocation = "";
            var matches = locations.Where(item => string.Equals(item.Location.Trim(), accountLocation, StringComparison.OrdinalIgnoreCase)).ToList();
            var activeBranches = await db.T_Branches.AsNoTracking().Where(branch => branch.st == true)
                .Select(branch => branch.Branch_ID).ToListAsync();
            var candidates = matches.Count > 0 ? matches : locations;
            var activeLocations = candidates.Where(item => activeBranches.Contains(item.Branch_ID)).ToList();
            defaultLocation = matches.Count == 1 ? matches[0] : activeLocations.Count == 1 ? activeLocations[0]
                : locations.Count == 1 ? locations[0] : null;

            if (kind == SupportKind.Sections)
            {
                managers = await db.T_Manages.AsNoTracking().OrderBy(item => item.Manag).ToListAsync();
                var available = defaultLocation is null ? managers : managers.Where(item =>
                    item.Location == defaultLocation.Location && item.Location_ID == defaultLocation.Location_ID &&
                    item.Branch_ID == defaultLocation.Branch_ID).ToList();
                var selected = (AddManagementInput.SelectedItem as ManagerChoice)?.Manager;
                var choices = available.GroupBy(item => item.Manag)
                    .SelectMany(group => group.Select(item => new ManagerChoice(item, group.Count() == 1 ? item.Manag
                        : $"{item.Manag} · {item.Location} / {item.Branch_ID}"))).ToList();
                AddManagementInput.ItemsSource = choices;
                AddManagementInput.SelectedItem = choices.FirstOrDefault(choice => selected is not null &&
                    choice.Manager.Manag_ID == selected.Manag_ID && choice.Manager.Location == selected.Location &&
                    choice.Manager.Location_ID == selected.Location_ID && choice.Manager.Branch_ID == selected.Branch_ID);
            }
            await LoadRowsAsync(db);
            SupportStatus.Text = "تم تحديث البيانات " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            defaultLocation = null;
            AddManagementInput.ItemsSource = null;
            SupportStatus.Text = "تعذّر تحميل البيانات. " + ex.GetBaseException().Message;
        }
        finally { loading = false; }
    }

    // يستعمل الاستعلام الخاص بالنوع فقط، فلا تظهر بيانات مصدر آخر في النافذة الحالية.
    private async Task LoadRowsAsync(Maintenance_DabaseContext db)
    {
        switch (kind)
        {
            case SupportKind.Buildings:
                RowsGrid.ItemsSource = (await db.T_Builds.AsNoTracking().OrderBy(item => item.Build).ToListAsync())
                    .Select(item => new SupportRow(item.Build, item.address ?? string.Empty, item.Location, item.Location_ID, item.Branch_ID, item.Build_ID, null)).ToList();
                break;
            case SupportKind.Services:
                RowsGrid.ItemsSource = (await db.T_Services.AsNoTracking().OrderBy(item => item.Serv_type).ToListAsync())
                    .Select(item => new SupportRow(item.Serv_type, string.Empty, item.Location, item.Location_ID, item.Branch_ID, item.Serv_num, null)).ToList();
                break;
            case SupportKind.Employees:
                RowsGrid.ItemsSource = (await db.T_Employes.AsNoTracking().OrderBy(item => item.Emp_Name).ToListAsync())
                    .Select(item => new SupportRow(item.Emp_Name, string.Empty, item.Location, item.Location_ID, item.Branch_ID, item.Employe_ID, null)).ToList();
                break;
            case SupportKind.Managements:
                RowsGrid.ItemsSource = (await db.T_Manages.AsNoTracking().OrderBy(item => item.Manag).ToListAsync())
                    .Select(item => new SupportRow(item.Manag, string.Empty, item.Location, item.Location_ID, item.Branch_ID, item.Manag_ID, null)).ToList();
                break;
            default:
                RowsGrid.ItemsSource = (await db.T_Sections.AsNoTracking().Include(item => item.T_Manage).OrderBy(item => item.Secti).ToListAsync())
                    .Select(item => new SupportRow(item.Secti, item.T_Manage.Manag, item.Location, item.Location_ID, item.Branch_ID, item.Secti_ID, item.Manag_ID)).ToList();
                break;
        }
    }
    #endregion

    #region اختيار الصف والتحقق
    // نمسح خطأ المسودة المعنية ونميّز قيمة المحرر فور الكتابة أو تغيير الاختيار.
    private void Input_EditValueChanged(object sender, EditValueChangedEventArgs e)
    {
        if (sender is not BaseEdit editor) return;
        editor.Background = editor.EditValue is null || editor.EditValue is string text && string.IsNullOrWhiteSpace(text)
            ? EmptyInputBrush : FilledInputBrush;
        ClearValidation(editor == AddNameInput || editor == AddAddressInput || editor == AddManagementInput);
    }

    // النقر المزدوج يفتح لوحة التعديل فقط؛ يبقى النقر العادي لاختيار الصف للحذف أو المراجعة.
    private void RowsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => Dispatcher.BeginInvoke(OpenSelectedRowForEdit);

    // زر التعديل داخل الجدول يحدد صفه صراحة ثم يستعمل اللوحة نفسها.
    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SupportRow row })
            RowsGrid.SelectedItem = row;
        OpenSelectedRowForEdit();
    }

    // يحمّل السجل المختار في مسودة لوحة Flyout دون تحرير مباشر في الجدول.
    private void OpenSelectedRowForEdit()
    {
        if (RowsGrid.SelectedItem is not SupportRow row)
        {
            SupportStatus.Text = "اختر صفًا من الجدول أولًا لتعديله.";
            return;
        }

        selectedRow = row;
        NameInput.Text = row.Name;
        AddressInput.Text = row.Details;
        if (kind == SupportKind.Sections)
        {
            // عند التعديل تظهر الإدارات في نطاق السجل نفسه حتى لو كان حساب المستخدم في نطاق آخر.
            var available = managers.Where(item => item.Location == row.Location && item.Location_ID == row.LocationId &&
                item.Branch_ID == row.BranchId).ToList();
            ManagementInput.ItemsSource = available;
            ManagementInput.SelectedItem = available.FirstOrDefault(item => item.Manag_ID == row.ParentId);
        }
        ClearValidation(adding: false);
        SupportEditFlyout.Header = "تعديل " + ScreenTitle.Text;
        SupportEditFlyout.IsOpen = true;
        Dispatcher.BeginInvoke(NameInput.Focus);
    }

    // إغلاق لوحة التعديل يمسح مسودتها وحدها، حتى لا تضيع كتابة المستخدم في خانات الإضافة.
    private void CancelEdit_Click(object sender, RoutedEventArgs e) => SupportEditFlyout.IsOpen = false;

    private void SupportEditFlyout_IsOpenChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Flyout { IsOpen: false })
        {
            selectedRow = null;
            NameInput.EditValue = AddressInput.EditValue = ManagementInput.EditValue = null;
            ClearValidation(adding: false);
        }
    }

    private void ClearValidation(bool adding)
    {
        var message = adding ? AddValidationText : ValidationText;
        if (message is null) return;
        message.Visibility = Visibility.Collapsed;
        foreach (var editor in adding ? new BaseEdit[] { AddNameInput, AddAddressInput, AddManagementInput }
                                      : new BaseEdit[] { NameInput, AddressInput, ManagementInput })
        {
            editor.ClearValue(BaseEdit.BorderBrushProperty);
            editor.ClearValue(ToolTipProperty);
        }
    }

    private void ShowValidation(BaseEdit editor, string error, bool adding)
    {
        var message = adding ? AddValidationText : ValidationText;
        editor.BorderBrush = System.Windows.Media.Brushes.Red;
        editor.ToolTip = error;
        message.Text = "⚠ " + error;
        message.Visibility = Visibility.Visible;
        editor.Focus();
    }
    #endregion

    #region الحفظ والحذف
    private async void Add_Click(object sender, RoutedEventArgs e) => await SaveAsync(adding: true);
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(adding: false);

    // نستخدم موقع الإدارة للقسم الجديد وموقع الحساب للأنواع الأخرى، ونحافظ على مفتاح السجل عند التعديل.
    private async Task SaveAsync(bool adding)
    {
        if (loading || !IsEnabled) return;
        var row = adding ? null : selectedRow;
        if (!adding && row is null) return;
        var nameEditor = adding ? AddNameInput : NameInput;
        var managerEditor = adding ? AddManagementInput : ManagementInput;
        var name = nameEditor.Text?.Trim() ?? string.Empty;
        var address = (adding ? AddAddressInput : AddressInput).Text?.Trim();
        var management = adding ? (managerEditor.SelectedItem as ManagerChoice)?.Manager : managerEditor.SelectedItem as T_Manage;
        if (name.Length == 0) { ShowValidation(nameEditor, "أدخل " + NameLabel.Text + ".", adding); return; }
        if (kind == SupportKind.Sections && management is null)
        {
            ShowValidation(managerEditor, "اختر الإدارة التابعة للقسم أو الوحدة.", adding);
            return;
        }
        if (adding && management is null && defaultLocation is null)
        {
            UiMessages.Show(this, Title, "تعذّر تحديد موقع وفرع وحيد لحسابك. راجع مسؤول المنظومة قبل إضافة البيان.", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // جميع المفاتيح الآتية تؤخذ من النطاق الصحيح قبل بدء عملية الحفظ.
        var location = new
        {
            Location = row?.Location ?? management?.Location ?? defaultLocation!.Location,
            Location_ID = row?.LocationId ?? management?.Location_ID ?? defaultLocation!.Location_ID,
            Branch_ID = row?.BranchId ?? management?.Branch_ID ?? defaultLocation!.Branch_ID
        };
        IsEnabled = false;
        try
        {
            await using var db = createContext();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            switch (kind)
            {
                case SupportKind.Buildings:
                    var building = row is null ? null : await db.T_Builds.SingleOrDefaultAsync(item => item.Location == row.Location && item.Location_ID == row.LocationId && item.Branch_ID == row.BranchId && item.Build_ID == row.Identifier);
                    if (!adding && building is null) throw new InvalidOperationException("لم يعد السجل موجودًا. حدّث القائمة وأعد المحاولة.");
                    if (building is null)
                    {
                        if (await db.T_Builds.AnyAsync(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID && item.Build == name)) throw new InvalidOperationException("المبنى موجود بالفعل في هذا النطاق.");
                        building = new T_Build { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID, Build_ID = await NextShortAsync(db.T_Builds.Where(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID), item => item.Build_ID), Build = name };
                        db.T_Builds.Add(building);
                    }
                    building.Build = name; building.address = address; building.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
                    break;
                case SupportKind.Services:
                    var service = row is null ? null : await db.T_Services.SingleOrDefaultAsync(item => item.Location == row.Location && item.Location_ID == row.LocationId && item.Branch_ID == row.BranchId && item.Serv_num == row.Identifier);
                    if (!adding && service is null) throw new InvalidOperationException("لم يعد السجل موجودًا. حدّث القائمة وأعد المحاولة.");
                    if (service is null)
                    {
                        if (await db.T_Services.AnyAsync(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID && item.Serv_type == name)) throw new InvalidOperationException("نوع الخدمات موجود بالفعل في هذا النطاق.");
                        service = new T_Service { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID, Serv_num = await NextShortAsync(db.T_Services.Where(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID), item => item.Serv_num), Serv_type = name };
                        db.T_Services.Add(service);
                    }
                    service.Serv_type = name; service.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
                    break;
                case SupportKind.Employees:
                    var employee = row is null ? null : await db.T_Employes.SingleOrDefaultAsync(item => item.Location == row.Location && item.Location_ID == row.LocationId && item.Branch_ID == row.BranchId && item.Employe_ID == row.Identifier);
                    if (!adding && employee is null) throw new InvalidOperationException("لم يعد السجل موجودًا. حدّث القائمة وأعد المحاولة.");
                    if (employee is null)
                    {
                        if (await db.T_Employes.AnyAsync(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID && item.Emp_Name == name)) throw new InvalidOperationException("الفني موجود بالفعل في هذا النطاق.");
                        int nextId = (await db.T_Employes.Where(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID).MaxAsync(item => (int?)item.Employe_ID) ?? 0) + 1;
                        employee = new T_Employe { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID, Employe_ID = nextId, Emp_Name = name };
                        db.T_Employes.Add(employee);
                    }
                    employee.Emp_Name = name; employee.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
                    break;
                case SupportKind.Managements:
                    var manager = row is null ? null : await db.T_Manages.SingleOrDefaultAsync(item => item.Location == row.Location && item.Location_ID == row.LocationId && item.Branch_ID == row.BranchId && item.Manag_ID == row.Identifier);
                    if (!adding && manager is null) throw new InvalidOperationException("لم يعد السجل موجودًا. حدّث القائمة وأعد المحاولة.");
                    if (manager is null)
                    {
                        if (await db.T_Manages.AnyAsync(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID && item.Manag == name)) throw new InvalidOperationException("الإدارة موجودة بالفعل في هذا النطاق.");
                        manager = new T_Manage { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID, Manag_ID = await NextShortAsync(db.T_Manages.Where(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID), item => item.Manag_ID), Manag = name };
                        db.T_Manages.Add(manager);
                    }
                    manager.Manag = name; manager.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
                    break;
                default:
                    var section = row is null ? null : await db.T_Sections.SingleOrDefaultAsync(item => item.Location == row.Location && item.Location_ID == row.LocationId && item.Branch_ID == row.BranchId && item.Secti_ID == row.Identifier);
                    if (!adding && section is null) throw new InvalidOperationException("لم يعد السجل موجودًا. حدّث القائمة وأعد المحاولة.");
                    if (section is null)
                    {
                        if (await db.T_Sections.AnyAsync(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID && item.Manag_ID == management!.Manag_ID && item.Secti == name)) throw new InvalidOperationException("القسم أو الوحدة موجود بالفعل تحت هذه الإدارة.");
                        section = new T_Section { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID, Secti_ID = await NextShortAsync(db.T_Sections.Where(item => item.Location == location.Location && item.Location_ID == location.Location_ID && item.Branch_ID == location.Branch_ID), item => item.Secti_ID), Secti = name };
                        db.T_Sections.Add(section);
                    }
                    section.Secti = name; section.Manag_ID = management!.Manag_ID; section.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
                    break;
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            if (adding)
                AddNameInput.EditValue = AddAddressInput.EditValue = AddManagementInput.EditValue = null;
            else
                SupportEditFlyout.IsOpen = false;
            await LoadAsync();
            UiMessages.Show(this, Title, adding ? "تمت إضافة البيان بنجاح." : "تم تعديل البيان بنجاح.", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiMessages.Show(this, Title, "تعذّر حفظ البيانات.\n" + ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    // ينفذ الحذف بعد التأكيد؛ القيود المرجعية في قاعدة البيانات تمنع حذف بيانات مستعملة في تكليفات سابقة.
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        // الحذف يعتمد اختيار الجدول فقط؛ لا يفتح لوحة التعديل ولا يظهر تحقق في خانة مخفية.
        if (RowsGrid.SelectedItem is not SupportRow row) { SupportStatus.Text = "اختر صفًا من الجدول أولًا لحذفه."; return; }
        if (loading || !IsEnabled) return;
        if (UiMessages.Show(this, Title, "هل تريد حذف البيان المحدد؟", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsEnabled = false;
        try
        {
            await using var db = createContext();
            switch (kind)
            {
                case SupportKind.Buildings: { var item = await db.T_Builds.SingleOrDefaultAsync(x => x.Location == row.Location && x.Location_ID == row.LocationId && x.Branch_ID == row.BranchId && x.Build_ID == row.Identifier); if (item is not null) db.T_Builds.Remove(item); break; }
                case SupportKind.Services: { var item = await db.T_Services.SingleOrDefaultAsync(x => x.Location == row.Location && x.Location_ID == row.LocationId && x.Branch_ID == row.BranchId && x.Serv_num == row.Identifier); if (item is not null) db.T_Services.Remove(item); break; }
                case SupportKind.Employees: { var item = await db.T_Employes.SingleOrDefaultAsync(x => x.Location == row.Location && x.Location_ID == row.LocationId && x.Branch_ID == row.BranchId && x.Employe_ID == row.Identifier); if (item is not null) db.T_Employes.Remove(item); break; }
                case SupportKind.Managements: { var item = await db.T_Manages.SingleOrDefaultAsync(x => x.Location == row.Location && x.Location_ID == row.LocationId && x.Branch_ID == row.BranchId && x.Manag_ID == row.Identifier); if (item is not null) db.T_Manages.Remove(item); break; }
                default: { var item = await db.T_Sections.SingleOrDefaultAsync(x => x.Location == row.Location && x.Location_ID == row.LocationId && x.Branch_ID == row.BranchId && x.Secti_ID == row.Identifier); if (item is not null) db.T_Sections.Remove(item); break; }
            }
            await db.SaveChangesAsync();
            await LoadAsync();
            UiMessages.Show(this, Title, "تم حذف البيان بنجاح.", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiMessages.Show(this, Title, "تعذّر الحذف؛ قد يكون البيان مستخدمًا في تكليف أو بيان آخر.\n" + ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    // يُجمّع العمود القابل للخلو في SQL حتى تبدأ النطاقات الفارغة بالرقم 1.
    private static async Task<short> NextShortAsync<T>(IQueryable<T> rows, System.Linq.Expressions.Expression<Func<T, short?>> selector)
    {
        var maximum = await rows.MaxAsync(selector) ?? 0;
        return checked((short)(maximum + 1));
    }

    // يمكن تحريك النافذة من الترويسة واستعمال حوافها للتحجيم.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Border or System.Windows.Controls.TextBlock &&
            e.GetPosition(this).Y < 67)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    #endregion
}
