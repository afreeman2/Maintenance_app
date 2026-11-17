using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Printing;
using DevExpress.Xpf.Printing.PreviewControl.Bars;
using DevExpress.XtraCharts;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance_app.Dashboard;
using Maintenance_app.dataSet;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Reports;
using Maintenance_app.Reports.charts_work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Reporting.WinForms;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Maintenance_app.reports;

// شاشة مستقلة للإحصائية حتى تبقى منفصلة عن شاشة تقارير الصيانة.
public partial class MaintenanceStatisticsWindow : MetroWindow
{
    #region التحميل وعرض الأعداد
    private readonly Func<Maintenance_DabaseContext> createContext;
    private readonly AnnualStatisticsViewModel statistics = new();
    private bool loadingFilters;
    private string displayedBuilding = "جميع المباني";
    // يحفظ المعرّف والنص معًا كي يبقى اختيار المبنى واضحًا للمستخدم ودقيقًا للاستعلام.
    private sealed record StatisticsBuildingChoice(short Id, string Name);

    public MaintenanceStatisticsWindow(Func<Maintenance_DabaseContext>? contextFactory)
    {
        createContext = contextFactory ?? (() => new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString).Options));
        DataContext = this;
        InitializeComponent();
        StatisticsYear.EditValue = FromStatisticsYear.EditValue = ToStatisticsYear.EditValue = DateTime.Today.Year;

    }
    private void SetWindowPhysicalSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        double scaleX = dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY;

        Width = 660 / scaleX;
        Height = 415 / scaleY;

        MinWidth = 0;
        MinHeight = 0;

        SizeToContent = SizeToContent.Manual;
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetWindowPhysicalSize();
        await LoadBuildingChoicesAsync();
        //await LoadAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
    }

    #endregion

    #region مستند طباعة الإحصائية ومعاينته
    // ننشئ صفحة A4 من آخر قراءة معروضة، ثم نربطها بعارض المستندات للطباعة.



    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // ==========================================
            // 1. تحديد نوع الإحصائية
            // ==========================================

            bool byOne = SingleYearMode.IsChecked == true;
            bool byMany = RangeYearsMode.IsChecked == true;
            bool byBuilding = StatisticsBuildingEnabled.IsChecked == true;

            if (byOne == byMany)
            {
                UiMessages.Show(
                    this,
                    "إحصائية الصيانة",
                    "يرجى تحديد سنة واحدة أو عدة سنوات.",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // ==========================================
            // 2. تحديد المبنى
            // ==========================================

            short? buildId = null;
            string buildingName = "جميع المباني";

            if (byBuilding)
            {
                var building = StatisticsBuilding.SelectedItem
                    as StatisticsBuildingChoice;

                if (building == null)
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "يرجى اختيار المبنى.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    StatisticsBuilding.Focus();
                    return;
                }

                buildId = building.Id;
                buildingName = building.Name;
            }

            // ==========================================
            // 3. تحديد السنوات والفترة
            // ==========================================

            int fromYear;
            int toYear;
            int? endMonth = null;
            string title;

            if (byOne)
            {
                if (!int.TryParse(
                        StatisticsYear.EditValue?.ToString(),
                        out fromYear) ||
                    fromYear < 1 ||
                    fromYear > 9998)
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "يرجى إدخال سنة صحيحة.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    StatisticsYear.Focus();
                    return;
                }

                toYear = fromYear;

                switch (StatisticsPeriodType.SelectedIndex)
                {
                    case 0:
                        endMonth = 3;
                        title =
                            $"إحصائية ربع سنوية لسنة ({fromYear})";
                        break;

                    case 1:
                        endMonth = 6;
                        title =
                            $"إحصائية نصف سنوية لسنة ({fromYear})";
                        break;

                    case 2:
                        endMonth = 9;
                        title =
                            $"إحصائية ثلاثة أرباع السنة لسنة ({fromYear})";
                        break;

                    case 3:
                        endMonth = 12;
                        title =
                            $"إحصائية سنوية لسنة ({fromYear})";
                        break;

                    default:
                        UiMessages.Show(
                            this,
                            "إحصائية الصيانة",
                            "يرجى اختيار نوع الفترة.",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        StatisticsPeriodType.Focus();
                        return;
                }
            }
            else
            {
                if (!int.TryParse(
                        FromStatisticsYear.EditValue?.ToString(),
                        out fromYear))
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "يرجى إدخال سنة البداية.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    FromStatisticsYear.Focus();
                    return;
                }

                if (!int.TryParse(
                        ToStatisticsYear.EditValue?.ToString(),
                        out toYear))
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "يرجى إدخال سنة النهاية.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ToStatisticsYear.Focus();
                    return;
                }

                if (fromYear < 1 ||
                    toYear > 9998 ||
                    fromYear > toYear)
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "مدى السنوات غير صحيح.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (toYear - fromYear > 12)
                {
                    UiMessages.Show(
                        this,
                        "إحصائية الصيانة",
                        "أدخل مدى سنوات لا يزيد الفرق فيه عن 12 سنة.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                title =
                    $"إحصائية سنوية من سنة ({fromYear}) " +
                    $"إلى سنة ({toYear})";
            }

            // ==========================================
            // 4. إضافة اسم المبنى
            // ==========================================

            if (byBuilding)
                title += $" للمبنى ({buildingName})";

            // ==========================================
            // 5. تحديد مسار التقرير
            // ==========================================

            string reportPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Reports",
                "chart_report.repx");

            if (!File.Exists(reportPath))
            {
                UiMessages.Show(
                    this,
                    "إحصائية الصيانة",
                    $"ملف التقرير غير موجود:\n{reportPath}",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // ==========================================
            // 6. تحميل البيانات من EF Core
            // ==========================================



            await using var db = createContext();

            var chartService =
                new MaintenanceChartService(db);

            List<MaintenanceChartResult> results =
                await chartService.GetChartAsync(
                    fromYear: fromYear,
                    toYear: toYear,
                    groupByMonth: byOne,
                    buildId: buildId,
                    endMonth: endMonth);

            if (results == null || results.Count == 0)
            {
                UiMessages.Show(
                    this,
                    "إحصائية الصيانة",
                    "لا توجد إحصائيات خلال الفترة المحددة.",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);



                return;
            }

            // ==========================================
            // 7. تجهيز بيانات المخطط
            // ==========================================

            var arabicCulture =
                CultureInfo.GetCultureInfo("ar-LY");

            var chartData = results
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .Select(x => new
                {
                    Period = byOne
                        ? new DateTime(x.Year, x.Month, 1)
                            .ToString("MMMM", arabicCulture)
                        : x.Year.ToString(),

                    x.Don,
                    x.Temp,
                    x.Undon,
                    x.Moveing,

                    x.DonPrice,
                    x.TempPrice,
                    x.UndonPrice,
                    x.MoveingPrice,

                    x.MCount,
                    x.MPrice
                })
                .ToList();

            // ==========================================
            // 8. تحميل تصميم التقرير
            // ==========================================

            using XtraReport report =
                XtraReport.FromFile(reportPath, true);

            report.DataSource = null;
            report.DataMember = "";

            XRChart chart = report.FindControl(
                "xrChart1",
                true) as XRChart;

            if (chart == null)
            {
                throw new InvalidOperationException(
                    "لم يتم العثور على xrChart1 داخل التقرير.");
            }

            // ==========================================
            // 9. ربط بيانات المخطط
            // ==========================================

            chart.DataSource = chartData;
            chart.DataMember = "";

            string[] seriesNames =
            {
            "منجزة",
            "معلقة",
            "غير منجزة",
            "قيد التنفيذ"
        };

            string[] fieldNames =
            {
            "Don",
            "Temp",
            "Undon",
            "Moveing"
        };

            // إزالة عمود الإجمالي القديم
            var oldTotalSeries = chart.Series
                .Cast<Series>()
                .FirstOrDefault(s => s.Name == "الإجمالي");

            if (oldTotalSeries != null)
                chart.Series.Remove(oldTotalSeries);

            // ==========================================
            // 10. ربط السلاسل الأربع
            // ==========================================

            for (int i = 0; i < seriesNames.Length; i++)
            {
                Series series = chart.Series
                    .Cast<Series>()
                    .FirstOrDefault(
                        s => s.Name == seriesNames[i]);

                if (series == null)
                {
                    throw new InvalidOperationException(
                        $"السلسلة '{seriesNames[i]}' غير موجودة.");
                }

                series.DataSource = null;
                series.ArgumentDataMember = "Period";

                series.ValueDataMembers.Clear();
                series.ValueDataMembers.AddRange(
                    fieldNames[i]);

                series.ArgumentScaleType =
                    ScaleType.Qualitative;

                series.ValueScaleType =
                    ScaleType.Numerical;

                series.LabelsVisibility =
                    DevExpress.Utils.DefaultBoolean.True;

                if (series.Label is SideBySideBarSeriesLabel label)
                {
                    label.Position =
                        BarSeriesLabelPosition.Top;

                    label.TextPattern = "{V:0}";
                }
            }

            // ==========================================
            // 11. عرض العدد وقيمة الصيانة لكل حالة
            // ==========================================

            chart.CustomDrawSeriesPoint += (sender, args) =>
            {
                int index = Array.IndexOf(
                    seriesNames,
                    args.Series.Name);

                if (index < 0)
                    return;

                var item = chartData.FirstOrDefault(
                    x => x.Period ==
                         args.SeriesPoint.Argument);

                if (item == null)
                    return;

                int count;
                decimal price;

                switch (index)
                {
                    case 0:
                        count = item.Don;
                        price = item.DonPrice;
                        break;

                    case 1:
                        count = item.Temp;
                        price = item.TempPrice;
                        break;

                    case 2:
                        count = item.Undon;
                        price = item.UndonPrice;
                        break;

                    default:
                        count = item.Moveing;
                        price = item.MoveingPrice;
                        break;
                }

                if (count <= 0)
                {
                    args.LabelText = "";
                    return;
                }

                args.LabelText =
                    $"العدد: {count:N0}\n" +
                    $"{price:N2} د.ل";
            };

            // ==========================================
            // 12. ضبط المحاور والإجمالي العام
            // ==========================================

            chart.Legend.Visibility =
                DevExpress.Utils.DefaultBoolean.True;

            if (chart.Diagram is XYDiagram diagram)
            {
                diagram.AxisX.Title.Text =
                    byOne ? "الأشهر" : "السنوات";

                diagram.AxisX.Title.Visibility =
                    DevExpress.Utils.DefaultBoolean.True;

                diagram.AxisY.Title.Text =
                    "عدد أوامر الصيانة";

                diagram.AxisY.Title.Visibility =
                    DevExpress.Utils.DefaultBoolean.True;
            }

            // حدث رسم تسميات المحاور تابع للمخطط
            chart.CustomDrawAxisLabel += (sender, args) =>
            {
                if (chart.Diagram is not XYDiagram diagram)
                    return;

                // تعديل تسميات المحور الأفقي فقط
                if (args.Item.Axis != diagram.AxisX)
                    return;

                string period =
                    args.Item.AxisValue?.ToString();

                if (string.IsNullOrWhiteSpace(period))
                    return;

                var item = chartData.FirstOrDefault(
                    x => x.Period == period);

                if (item == null)
                    return;

                args.Item.Text =
                    $"{period}\n" +
                    $"الإجمالي: {item.MCount:N0}\n" +
                    $"{item.MPrice:N2} د.ل";
            };

            // ==========================================
            // 13. عنوان التقرير
            // ==========================================

            XRLabel titleLabel = report.FindControl(
                "xrLabel4",
                true) as XRLabel;

            if (titleLabel != null)
            {
                titleLabel.ExpressionBindings.Clear();
                titleLabel.Text = title;
            }

            // ==========================================
            // 14. تاريخ التقرير
            // ==========================================

            XRLabel dateLabel = report.FindControl(
                "xrLabel3",
                true) as XRLabel;

            if (dateLabel != null)
            {
                dateLabel.ExpressionBindings.Clear();

                dateLabel.Text =
                    DateTime.Now.ToString("dd/MM/yyyy");
            }

            // ==========================================
            // 15. اسم الفرع
            // ==========================================

            XRLabel branchLabel = report.FindControl(
                "xrLabel6",
                true) as XRLabel;

            if (branchLabel != null)
            {
                branchLabel.ExpressionBindings.Clear();
                branchLabel.Text = App.Branch_Name;
            }

            // ==========================================
            // 16. اسم المستخدم
            // ==========================================

            XRLabel userLabel = report.FindControl(
                "xrLabel5",
                true) as XRLabel;

            if (userLabel != null)
            {
                userLabel.ExpressionBindings.Clear();
                userLabel.Text = App.login_user.User_Name;
            }

            // ==========================================
            // 17. معاينة الطباعة
            // ==========================================

            report.RequestParameters = false;

            // نحافظ على أوامر شريط الإحصائية عند عرضه في نافذة التقارير المشتركة.
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
            #endregion
            show_report.ShowPreview(this, report, "إحصائية الصيانة", provider);


        }
        catch (Exception ex)
        {


            UiMessages.Show(
                this,
                "خطأ أثناء إعداد إحصائية الصيانة",
                ex.ToString(),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    #endregion

    #region تحميل المرشحات ورسم المخططات
    // تحميل قائمة المباني مرة واحدة للنموذج، ولا تدخل في قراءة المخطط إلا عند تفعيل مربعها.
    private async Task LoadBuildingChoicesAsync()
    {
        try
        {
            loadingFilters = true;
            await using var db = createContext();
            StatisticsBuilding.ItemsSource = await db.T_Builds.AsNoTracking().OrderBy(x => x.Build)
                .Select(x => new StatisticsBuildingChoice(x.Build_ID, x.Build)).ToListAsync();
            StatisticsBuilding.DisplayMember = nameof(StatisticsBuildingChoice.Name);
        }
        finally { loadingFilters = false; }
    }

    private void StatisticsFilter_Changed(object sender, RoutedEventArgs e)
    {
        // تعطيل شرط المبنى يمسح اختياره حتى لا يؤثر في القراءة اللاحقة دون أن يكون ظاهرًا.
        if (!loadingFilters && StatisticsBuildingEnabled.IsChecked != true) StatisticsBuilding.EditValue = null;
    }
    private void StatisticsFilter_Changed(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e) { }
    private void StatisticsPeriod_Changed(object sender, RoutedEventArgs e) { }


    // الحالات نفسها تُستخدم في المخططات والمستند الورقي.

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    #endregion
}

#region نقطة بيانات مخططات الإحصائية
// تمثل عنوان الحالة وعددها في مخططي التوزيع والأعمدة الثابتين.
public sealed record StatisticsChartPoint(string Title, long Count);
#endregion
