using DevExpress.Data;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using Maintenance_app.Accounts;
using Maintenance_app.Dashboard;
using Maintenance_app.orders_Windows;
using System.Windows;
using System.Windows.Controls;
using AggregatedContext = Maintenance_app.dbContext_Aggregated.Maintenance_Aggregated_DabaseContext;

namespace Maintenance_app.reports;

public partial class MaintenanceReportsWindow
{
    #region تهيئة عرض الفروع وتجميع الجدول
    private readonly Func<AggregatedContext>? createBranchContext;
    private readonly CancellationTokenSource windowLifetime = new();
    private readonly Dictionary<short, string> branchNames = new();
    private Dictionary<string, MaintenanceReportRow> branchAssignmentsByKey = [];
    private CancellationTokenSource? branchScope;
    private bool IsBranchReport => createBranchContext is not null;
    private bool IsBranchFilterEnabled => BranchFilterEnabled.IsChecked == true;
    private short? SelectedBranchId => IsBranchFilterEnabled ? BranchFilter.EditValue as short? : null;

    // تشترك تقارير الفروع في شكل التكليف وتضيف مستوى تجميع وإجماليات لكل فرع.
    internal MaintenanceReportsWindow(Func<AggregatedContext> branchContextFactory, bool branchAccess)
    {
        ArgumentNullException.ThrowIfNull(branchContextFactory);
        if (!branchAccess) throw new UnauthorizedAccessException("عرض تقارير الفروع يتطلب صلاحية تقارير الفروع.");
        createContext = null;
        createBranchContext = branchContextFactory;
        InitializeReportWindow();
        Title = ReportHeading.Text = "تقارير الفروع";
        ReportDescription.Text = "عرض تقارير الصيانة لجميع الفروع أو حسب الفرع";
        BranchFilterRow.Visibility = Visibility.Visible;
        // خيارات الفرع معرفة في XAML وتظهر فقط في وضع تقارير الفروع المصرح به.
        BranchPrintChoice.Visibility = BranchHeadersPrintChoice.Visibility = Visibility.Visible;
        // قوائم الفروع تحوي أسماء مباشرة، وتُعرض بنود التكليف في الأعمدة المشتركة.
        foreach (var editor in new[] { BuildingFilter, ServiceFilter, TechnicianFilter })
            editor.DisplayMember = editor.ValueMember = string.Empty;
        AssignmentScopeColumn.FieldName = nameof(BranchReportLine.Scope);
        // بعد تجميع الفرع والتكليف، يُعرض البيان بالترتيب الذي سُجلت به بنوده.
        BranchDetailOrderColumn.SortOrder = ColumnSortOrder.Ascending;
        ReportsGrid.MouseDoubleClick += ReportsGrid_MouseDoubleClick;
        ReportsGrid.CustomSummaryExists += (_, e) => e.Exists = e.GroupLevel == 0;
        ReportsGrid.CustomSummary += ReportsGrid_BranchSummary;
        ReportsGrid.GroupSummary.Add(new GridSummaryItem
        {
            FieldName = nameof(BranchReportLine.Number),
            SummaryType = SummaryItemType.Custom,
            DisplayFormat = "إجمالي عدد الصيانة {0:N0}"
        });
        ReportsGrid.GroupSummary.Add(new GridSummaryItem
        {
            FieldName = nameof(BranchReportLine.Cost),
            SummaryType = SummaryItemType.Custom,
            DisplayFormat = "إجمالي تكاليف الصيانة {0:N3} د.ل"
        });
    }

    // المستوى الأول لاسم الفرع وإجمالياته، والمستوى الثاني لبطاقات التكليف المعتادة.
    private sealed class BranchGroupTemplateSelector(DataTemplate branchTemplate, DataTemplate assignmentTemplate) : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) =>
            item is GroupRowData { GroupLevel: 0 } ? branchTemplate : assignmentTemplate;
    }

    // لا يتكرر عدد التكليف أو مبلغه في إجمالي الفرع مهما تعددت بنوده الظاهرة بعد التصفية.
    private void ReportsGrid_BranchSummary(object sender, CustomSummaryEventArgs e)
    {
        switch (e.SummaryProcess)
        {
            case CustomSummaryProcess.Start:
                e.TotalValue = new Dictionary<MaintenanceOrderKey, decimal>();
                break;
            case CustomSummaryProcess.Calculate when e.TotalValue is Dictionary<MaintenanceOrderKey, decimal> costs
                && e.Row is BranchReportLine line:
                costs.TryAdd(line.Order.Key, line.Cost);
                break;
            case CustomSummaryProcess.Finalize when e.TotalValue is Dictionary<MaintenanceOrderKey, decimal> costs:
                e.TotalValue = ((GridSummaryItem)e.Item).FieldName == nameof(BranchReportLine.Cost)
                    ? costs.Values.Sum() : costs.Count;
                break;
        }
    }

    // لكل بند صف مستقل، مع صف توضيحي للتكليفات التي لم تسجل لها بنود حتى تبقى بطاقتها متاحة.
    private void ShowBranchAssignments()
    {
        branchAssignmentsByKey = displayedRows.ToDictionary(row => row.AssignmentKey);
        ReportsGrid.ItemsSource = displayedRows.SelectMany(row => row.BranchDetails is { Count: > 0 } details
            ? details.Select(detail => new BranchReportLine(row, detail)) : [new BranchReportLine(row, null)]).ToArray();
    }

    // يحتفظ الجدول برقم الفرع مفتاحًا للتجميع ويُظهر الاسم في عنوان المجموعة.
    private void ReportsGrid_CustomGroupDisplayText(object sender, CustomGroupDisplayTextEventArgs e)
    {
        if (e.Column == BranchGroupColumn && e.Value is short id && branchNames.TryGetValue(id, out var name))
            e.DisplayText = name;
    }
    #endregion

    #region اختيار نطاق البحث وتحميل خياراته
    private async Task LoadBranchesAsync()
    {
        var token = windowLifetime.Token;
        await using var db = createBranchContext!();
        var branches = await QueryBranchesAsync(db, token);
        if (token.IsCancellationRequested) return;
        BranchFilter.ItemsSource = branches;
        // يصبح خيار الفرع متاحًا بعد تجهيز النطاق الافتراضي حتى لا يتداخل مع التحميل الأول.
        await RefreshBranchScopeAsync();
        if (!token.IsCancellationRequested) BranchFilterEnabled.IsEnabled = branches.Count > 0;
    }

    // إلغاء التحديد يفرّغ القوائم التابعة ويتيح البحث في جميع الفروع.
    private async void BranchFilterEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || fillingFilters) return;
        if (!IsBranchFilterEnabled)
        {
            fillingFilters = true;
            try { ClearFilterValue(BranchFilter); }
            finally { fillingFilters = false; }
        }
        await RefreshBranchScopeAsync();
    }

    private async void BranchFilter_EditValueChanged(object sender, EditValueChangedEventArgs e)
    {
        AccountFieldValidation.Clear(BranchFilter);
        if (IsLoaded && IsBranchFilterEnabled && !fillingFilters) await RefreshBranchScopeAsync();
    }

    // يُلغى استعلام النطاق السابق وتُمسح نتائجه وخياراته التابعة قبل بدء النطاق الجديد.
    private async Task RefreshBranchScopeAsync()
    {
        branchScope?.Cancel();
        branchScope?.Dispose();
        branchScope = null;
        if (windowLifetime.IsCancellationRequested) return;

        fillingFilters = true;
        try
        {
            BuildingFilterEnabled.IsChecked = ServiceFilterEnabled.IsChecked = TechnicianFilterEnabled.IsChecked = false;
            ClearFilterValue(BuildingFilter);
            ClearFilterValue(ServiceFilter);
            ClearFilterValue(TechnicianFilter);
            BuildingFilter.ItemsSource = ServiceFilter.ItemsSource = TechnicianFilter.ItemsSource = null;
        }
        finally { fillingFilters = false; }

        MaintenancePrintFlyout.IsOpen = false;
        BranchDetailsFlyout.IsOpen = false;
        BranchDetailsFlyout.DataContext = null;
        ReportsGrid.SelectedItem = null;
        allRows = [];
        ApplyFilters();
        SetBranchFiltersAvailable(false);
        ShowReportButton.IsEnabled = true;
        // يظل العرض متاحًا لكل الفروع، وتبقى القوائم التابعة فارغة حتى اختيار فرع.
        if (SelectedBranchId is not short branchId) return;

        branchScope = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        var token = branchScope.Token;
        ShowReportButton.IsEnabled = false;
        ReportsStatus.Text = "جارٍ تحميل خيارات البحث…";
        try
        {
            await using var db = createBranchContext!();
            var choices = await QueryBranchFiltersAsync(db, branchId, token);
            if (token.IsCancellationRequested) return;
            BuildingFilter.ItemsSource = choices.Buildings;
            ServiceFilter.ItemsSource = choices.Services;
            TechnicianFilter.ItemsSource = choices.Technicians;
            SetBranchFiltersAvailable(true);
            ReportsStatus.Text = BranchSearchPrompt;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ReportsStatus.Text = "تعذّر تحميل خيارات البحث.";
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested) ShowReportButton.IsEnabled = true;
        }
    }

    private string BranchSearchPrompt => SelectedBranchId is null
        ? "اضغط «عرض» لإظهار تقارير جميع الفروع."
        : "اضغط «عرض» لإظهار تقارير الفرع.";

    private void SetBranchFiltersAvailable(bool available)
    {
        BuildingFilterEnabled.IsEnabled = ServiceFilterEnabled.IsEnabled = TechnicianFilterEnabled.IsEnabled = available;
    }
    #endregion

    #region تنفيذ البحث في قاعدة البيانات المجمّعة
    // القيمة الخالية تعني عرض جميع الفروع، ويُعتمد الاستعلام فقط إذا بقي نطاقه محددًا.
    private async Task LoadBranchReportsAsync(short? branchId)
    {
        // نحفظ الفترة المعتمدة عند الضغط على «عرض» قبل بدء القراءة غير المتزامنة.
        DateOnly? from = DateFilter.IsChecked == true ? DateOnly.FromDateTime(FromDate.DateTime) : null;
        DateOnly? to = DateFilter.IsChecked == true ? DateOnly.FromDateTime(ToDate.DateTime) : null;
        branchScope ??= CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        var token = branchScope.Token;
        ReportsStatus.Text = "جارٍ تحميل تقارير الصيانة…";
        ShowReportButton.IsEnabled = false;
        try
        {
            await using var db = createBranchContext!();
            var rows = await QueryBranchReportsAsync(db, branchId, from, to, token);
            if (token.IsCancellationRequested) return;
            branchNames.Clear();
            foreach (var row in rows) branchNames[row.BranchId] = row.BranchName;
            allRows = rows;
            ApplyFilters();
            if (displayedRows.Count == 0)
                ReportsStatus.Text = "لا توجد تكليفات صيانة تطابق نطاق البحث والفلاتر المختارة.";
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ReportsStatus.Text = "تعذّر تحميل تقارير الصيانة.";
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested) ShowReportButton.IsEnabled = true;
        }
    }
    #endregion
}
