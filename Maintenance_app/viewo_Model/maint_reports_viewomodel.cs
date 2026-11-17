    using DevExpress.Data.Linq;
    using DevExpress.Mvvm;
    using DevExpress.Mvvm.DataAnnotations;
    using DevExpress.Mvvm.Xpf;
    using DevExpress.Xpf.Core;
using Maintenance_app.converts;
using Maintenance_app.Dashboard;
using Maintenance_app.DatabContext;
using Maintenance_app.dbContext;
    using Maintenance_app.Models;
    using Microsoft.EntityFrameworkCore;
    using System;
    using System.Collections;
    using System.Collections.ObjectModel;
    using System.Configuration;
    using System.Linq;
    using System.Windows;
    using MessageBox = System.Windows.MessageBox;

namespace Maintenance_app.viewo_Model
{

    
        public partial class maint_reports_viewomodel : ViewModelBase, IDisposable
        {
            #region إدارة مصدر البحث
            // =========================================================
            // Fields
            // =========================================================

            private EntityInstantFeedbackSource? _ItemsSource;
            private bool _disposed;


            private IList _listing;


            // =========================================================
            // Constructor
            // =========================================================

            public maint_reports_viewomodel()
            {
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                CancelReportTotals();
                var source = _ItemsSource;
                _ItemsSource = null;
                source?.Dispose();
            }

            public void ClearSearch()
            {
                // إلغاء تفعيل جميع الفلاتر
                isbuilding = false;
                isservice = false;
                ismen = false;
                isdate = false;
                isstate = false;
                istype = false;
            
                // مسح قيم البحث
                serv_num = null;
                build_num = null;
                typeid = null;
                stateid = null;
                datefrom = null;
                dateto = null;
                man_id = null;
                // إزالة مصدر النتائج الحالي
                var oldSource = _ItemsSource;

                _ItemsSource = null;

                RaisePropertyChanged(nameof(ItemsSource));
                CancelReportTotals();
                currentReportSearch = null;
                Totals = ReportTotals.Empty;
                TotalsStatus = "";

                // التخلص من المصدر القديم إذا كان قابلًا للتخلص
                if (oldSource is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            private bool _searchExecuted;

            public bool SearchExecuted
            {
                get => _searchExecuted;
                private set
                {
                    if (_searchExecuted == value)
                        return;

                    _searchExecuted = value;
                    RaisePropertyChanged(nameof(SearchExecuted));
                }
            }

            public void ExecuteSearch()
            {
                if (_disposed)
                    return;

                var query = CaptureReportSearch();
                var replacement = CreateItemsSource(query);
                var oldSource = _ItemsSource;
                _ItemsSource = replacement;
                RaisePropertyChanged(nameof(ItemsSource));
                oldSource?.Dispose();
                currentReportSearch = query;
                _ = UpdateReportTotalsAsync(query);
            }
            #endregion
            #region متغيرات البحث

            #region للفرع
            private short? _branch;
            public short? branch
            {
                get
                {
                    return _branch;
                }

                set
                {
                    if (_branch == value)
                        return;

                    _branch = value;

                    RaisePropertyChanged(nameof(branch));
                }
            }
            #endregion

            #region رقم الخدمات
            private short? _serv_num;
            public short? serv_num
            {
                get
                {
                    return _serv_num;
                }

                set
                {
                    if (_serv_num == value)
                        return;

                    _serv_num = value;

                    RaisePropertyChanged(nameof(serv_num));
                }
            }


            #endregion


            #region رقم المبني
            private short? _build_num;
 
            public short? build_num
            {
                get
                {
                    return _build_num   ;
                }

                set
                {
                    if (_build_num == value)
                        return;

                    _build_num = value;

                    RaisePropertyChanged(nameof(build_num));
                }
            }

            #endregion

            #region رقم النوع
            private int? _typeid;

            public int? typeid
            {
                get
                {
                    return _typeid;
                }

                set
                {
                    if (_typeid == value)
                        return;

                    _typeid = value;

                    RaisePropertyChanged(nameof(typeid));
                }
            }

            #endregion


            #region رقم الحالة

            private byte? _stateid;

            public byte? stateid
            {
                get
                {
                    return _stateid;
                }

                set
                {
                    if (_stateid == value)
                        return;

                    _stateid = value;

                    RaisePropertyChanged(nameof(stateid));
                }
            }

            #endregion

            #region رقم الفني

            private int? _man_id;

            public int? man_id
            {
                get
                {
                    return _man_id;
                }

                set
                {
                    if (_man_id == value)
                        return;

                    _man_id = value;

                    RaisePropertyChanged(nameof(man_id));
                }
            }
            #endregion

            #region التاريخ من

            private DateTime? _datefrom;

            public DateTime? datefrom
            {
                get
                {
                    return _datefrom;
                }

                set
                {
                    if (_datefrom == value)
                        return;

                    _datefrom = value;

                    RaisePropertyChanged(nameof(datefrom));
                }
            }
            #endregion


            #region التاريخ الى

            private DateTime? _dateto;

            public DateTime? dateto
            {
                get
                {
                    return _dateto;
                }

                set
                {
                    if (_dateto == value)
                        return;

                    _dateto = value;

                    RaisePropertyChanged(nameof(dateto));
                }
            }

       
            #endregion

            #endregion

            #region متغييرات تنشيط خانات البحث
            #region لنوع الصياتة
            private bool _istype;

            public bool istype
            {
                get
                {
                    return _istype;
                }

                set
                {
                    if (_istype == value)
                        return;

                    _istype = value;

                    RaisePropertyChanged(nameof(istype));
                }
            }

            #endregion


            #region للخدمات

            private bool _isservice;

            public bool isservice
            {
                get
                {
                    return _isservice;
                }

                set
                {
                    if (_isservice == value)
                        return;

                    _isservice = value;

                    RaisePropertyChanged(nameof(isservice));
                }
            }

            #endregion

            #region للمبني

            private bool _isbuilding;

            public bool isbuilding
            {
                get
                {
                    return _isbuilding;
                }

                set
                {
                    if (_isbuilding == value)
                        return;

                    _isbuilding = value;

                    RaisePropertyChanged(nameof(isbuilding));
                }
            }

            #endregion


            #region للفني

            private bool _ismen;

            public bool ismen
            {
                get
                {
                    return _ismen;
                }

                set
                {
                    if (_ismen  == value)
                        return;

                    _ismen = value;

                    RaisePropertyChanged(nameof(ismen));
                }
            }

            #endregion

            #region للتاريخ

            private bool _isdate;

            public bool isdate
            {
                get
                {
                    return _isdate;
                }

                set
                {
                    if (_isdate == value)
                        return;

                    _isdate = value;

                    RaisePropertyChanged(nameof(isdate));
                }
            }

            #endregion

            #region حالة الصيانة

            private bool _isstate;

            public bool isstate 
            {
                get
                {
                    return _isstate;
                }

                set
                {
                    if (_isstate  == value)
                        return;

                    _isstate = value;

                    RaisePropertyChanged(nameof(isstate));
                }
            }

            #endregion

            #region حالة الفرع
            private bool _isbranch;

            public bool isbranch
            {
                get
                {
                    return _isbranch;
                }

                set
                {
                    if (_isbranch == value)
                        return;

                    _isbranch = value;

                    RaisePropertyChanged(nameof(isbranch));
                }
            }

            #endregion

            #endregion

            public EntityInstantFeedbackSource? ItemsSource
            {
                get => _ItemsSource;
            }  // =========================================================
               // GetQueryable
               // =========================================================

            // =========================================================
            // Listing
            // =========================================================

            public IList listing
            {
                get
                {
                    if (_listing == null && !IsInDesignMode)
                    {
                        DbContextHelper.InitOptions(
                     App.ConnectionString
                 );
                        using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                        _listing = context.T_Emergency_MaintenanceCosts_Services.Select(item => new {
                            Emergency_MaintenanceCosts_No = item.Emergency_MaintenanceCosts_No,
                            Date_com = item.T_Emergency_MaintenanceCost.Date_com,
                            T_Branch = item.T_Emergency_MaintenanceCost.Branch,
                            Branch_ID = item.T_Emergency_MaintenanceCost.Branch_ID,
                            Build_ID = item.T_Emergency_MaintenanceCost.Build_ID,
                            Cost = item.T_Emergency_MaintenanceCost.Cost,
                            Employe_ID = item.T_Emergency_MaintenanceCost.Employe_ID,
                            Location = item.Location,
                            Location_ID = item.Location_ID,
                            MaintenanceStatusName = item.T_Emergency_MaintenanceCost.MaintenanceStatusName,
                            MaintenanceStatusid = item.T_Emergency_MaintenanceCost.MaintenanceStatusid,
                            MaintenanceTypeName = item.T_Emergency_MaintenanceCost.MaintenanceTypeName,
                            Technicians = item.T_Emergency_MaintenanceCost.Technicians,
                            T_Service = item.T_Emergency_MaintenanceCost.T_Service,
                            T_MaintenanceCosts_Parts = item.T_Emergency_MaintenanceCost.T_MaintenanceCosts_Parts,
                            T_Location = item.T_Emergency_MaintenanceCost.T_Location,
                            T_FinishedMaintenances = item.T_Emergency_MaintenanceCost.T_FinishedMaintenances,

                            T_Employe = item.T_Emergency_MaintenanceCost.T_Employe,
                             T_Build = item.T_Emergency_MaintenanceCost.T_Build,
                            T_Emergency_MaintenanceCost = item.T_Emergency_MaintenanceCost






                        }).ToArray();
                    }
                    return _listing;
                }
            }


            #region تحديث نتائج البحث
            [Command]
            public void DataSourceRefresh(DataSourceRefreshArgs args)
            {
                _listing = null;

                if (_ItemsSource == null)
                    return;



                _ItemsSource.Refresh();
                // يعاد حساب الشريط من شروط آخر بحث مع تحديث النتائج نفسها.
                if (currentReportSearch is { } query) _ = UpdateReportTotalsAsync(query);

            }
            #endregion

        }
    

}
