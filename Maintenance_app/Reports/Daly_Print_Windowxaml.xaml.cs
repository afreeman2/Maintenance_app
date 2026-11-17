using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Printing;
using DevExpress.Xpf.Printing.PreviewControl.Bars;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance_app.Accounts;
using Maintenance_app.Dashboard;
using Maintenance_app.DatabContext;
using Maintenance_app.dataSet;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Reporting.WinForms;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Maintenance_app.reports;

public partial class Daly_Print_Windowxaml : MetroWindow
{
    #region تهيئة النافذة وتحميل المباني والفاحصين
    // يتيح مصنع السياق فحص النافذة ببيانات معزولة، ويستخدم اتصال المنظومة في التشغيل العادي.
    private readonly Func<Maintenance_DabaseContext> createContext;
    private readonly CancellationTokenSource lifetime = new();
    private bool loaded, busy;

    public Daly_Print_Windowxaml(Func<Maintenance_DabaseContext>? contextFactory = null)
    {
        
        // يبقى اتصال التشغيل الحالي هو الافتراضي، ويمكن تمرير سياق معزول للفحص.
        createContext = contextFactory ?? (() => new Maintenance_DabaseContext(DbContextHelper.Options));
        InitializeComponent();
     
        date_br_begin.EditValue = DateTime.Today;
      
    }

  

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await LoadChoicesAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = "تعذر تحميل خيارات النموذج.";
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task LoadChoicesAsync()
    {
        if (loaded || busy) return;
        busy = true;
        LoadingIndicator.DeferedVisibility = true;
        PrintButton.IsEnabled = false;
        try
        {
            await using var db = createContext();
            var buildings = await db.T_Builds.AsNoTracking().OrderBy(item => item.Build).ToListAsync(lifetime.Token);
            // يطابق اختيار الفاحص المصدر القديم، فاحص أو فاحص وفني فقط.
            var inspectors = await db.T_Employes.AsNoTracking().Where(item => item.Adje == 0 || item.Adje == 2)
                .OrderBy(item => item.Emp_Name).ToListAsync(lifetime.Token);
          
            Combo_Buildings.ItemsSource = buildings;
            Combo_Check_Man.ItemsSource = inspectors;
            loaded = true;
            StatusText.Text = "اختر المبنى والتاريخ والفاحص ثم افتح معاينة الطباعة.";
            Combo_Buildings.Focus();
        }
        finally
        {
            busy = false;
            LoadingIndicator.DeferedVisibility = false;
            PrintButton.IsEnabled = loaded;
        }
    }
    #endregion

    #region التحقق وتجهيز بيانات قالب الفحص اليومي الأصلي
    public async void CreateReportAsync()
    {
        AccountFieldValidation.Clear(Combo_Buildings);
        AccountFieldValidation.Clear(date_br_begin);
        AccountFieldValidation.Clear(Combo_Check_Man);
        if (Combo_Buildings.SelectedItem is not T_Build building)
        {
            AccountFieldValidation.Show(Combo_Buildings, "اختر المبنى.");
            return ;
        }
        if (!date_br_begin.DoValidate() || date_br_begin.EditValue is not DateTime date || date == DateTime.MinValue)
        {
            AccountFieldValidation.Show(date_br_begin, "أدخل تاريخ الفحص الصحيح.");
            return ;
        }
        if (Combo_Check_Man.SelectedItem is not T_Employe inspector)
        {
            AccountFieldValidation.Show(Combo_Check_Man, "اختر الفاحص.");
            return ;
        }

        await using var db = createContext();
        var services = await db.T_Services.AsNoTracking().OrderBy(item => item.Serv_type).ToListAsync(lifetime.Token);
        if (services.Count == 0) throw new InvalidOperationException("لا توجد أنواع خدمات لإعداد نموذج الفحص اليومي.");
        // يحدد مفتاح المبنى فرعه وموقعه، فلا تؤخذ ترويسة فرع آخر من جلسة المستخدم.
        var branch = await db.T_Branches.AsNoTracking().Where(item => item.Branch_ID == building.Branch_ID)
            .Select(item => item.Branch).FirstOrDefaultAsync(lifetime.Token);
        DataSet1 dataSet = new DataSet1();
        dataSet.DataTable1.Rows.Clear();
        DataRow row;

        
            row = dataSet.DataTable1.NewRow();

          
            row = dataSet.DataTable1.NewRow();
        row["DataColumn29"] = App.location.Branch_ID == 21 ? "ادارة الشؤون الادارية والخدمات" : "قسم الشؤون الادارية والخدمات";
        row["DataColumn27"] = App.location.Branch_ID == 21 ? "قسم الصيانة والخدمات / وحدة شؤون الخدمات" : "وحدة الخدمات والعلاقات";

        row["DataColumn14"] = branch ?? App.Branch_Name ?? "";
            row["DataColumn7"] = building.Location ?? "";
            row["DataColumn8"] = building.Build ?? "";
            row["DataColumn9"] = inspector.Emp_Name ?? "";
            row["DataColumn6"] = date.ToString("dddd", CultureInfo.GetCultureInfo("ar-LY"));
            row["DataColumn20"] = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            dataSet.DataTable1.Rows.Add(row);
        //}
        XtraReport rb = XtraReport.FromFile(Path.Combine(AppContext.BaseDirectory, "Reports", "new_Daly_Check.repx"), true);
        rb.DataSource = dataSet;
        rb.DataMember = "DataTable1";

        rb.RequestParameters = false;

        var preview = new DocumentPreviewWindow();






        preview.WindowState = WindowState.Maximized;
        preview.Title = "نموذج الفحص اليومي";
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
        ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

        preview.PreviewControl.DocumentSource = rb;
        preview.Show();

        await Task.Run(() =>
        {
            rb.CreateDocument(false); // أسرع
        });
        preview.Closed += pr_close;

        //var report = new LocalReport { ReportPath = Path.Combine(AppContext.BaseDirectory, "Reports", "new_Daly_Check.rdlc") };
        //report.DataSources.Add(new ReportDataSource("DataSet1", table.DefaultView));
        //return report;
    }
    private void pr_close(object? sender, EventArgs e)
    {
        ApplicationThemeHelper.ApplicationThemeName = "None";

    }
    #endregion

    #region الطباعة والتنقل وتنظيف أخطاء الإدخال
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!loaded || busy) return;
        busy = true;
        PrintButton.IsEnabled = false;
        try
        {
              CreateReportAsync();
          
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            busy = false;
            PrintButton.IsEnabled = loaded;
        }
    }

    // تمسح أحداث التغيير والكتابة الخطأ فورًا، بما يشمل الاختيار والقائمة مفتوحة.
    private void Field_Changed(object sender, EditValueChangedEventArgs e) => AccountFieldValidation.Clear((FrameworkElement)sender);
    private void Field_Typing(object sender, TextCompositionEventArgs e) => AccountFieldValidation.Clear((FrameworkElement)sender);
    private void Building_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        date_br_begin.Focus();
        e.Handled = true;
    }
    private void Date_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Combo_Check_Man.Focus();
        e.Handled = true;
    }
    private void Inspector_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Combo_Check_Man.IsPopupOpen) return;
        Print_Click(PrintButton, new RoutedEventArgs());
        e.Handled = true;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    #endregion
}
