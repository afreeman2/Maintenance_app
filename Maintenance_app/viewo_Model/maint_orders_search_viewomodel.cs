using DevExpress.Data.Linq;
using DevExpress.Mvvm;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm.Xpf;
using DevExpress.Xpf.Core;
using Maintenance_app.converts;
using Maintenance_app.DatabContext;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;

namespace Maintenance_app.viewo_Model
{
    public class doc_viewo : ViewModelBase, IDisposable
    {
        // =========================================================
        // Fields
        // =========================================================

        private EntityInstantFeedbackSource _ItemsSource;
        private bool _disposed;


        private IList _listing;


        // =========================================================
        // Constructor
        // =========================================================

        public doc_viewo()
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            var source = _ItemsSource;
            _ItemsSource = null;
            source?.Dispose();
        }

        public void ClearSearch()
        {
            // إلغاء تفعيل جميع الفلاتر
            isempid = false;
            isservice = false;
           
            isdate = false;
           

            // مسح قيم البحث
            empid = null;
            serviceid = null;
            
            datefrom = null;
            dateto = null;

            // إزالة مصدر النتائج الحالي
            var oldSource = _ItemsSource;

            _ItemsSource = null;

            RaisePropertyChanged(nameof(ItemsSource));

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

            var replacement = CreateItemsSource();
            var oldSource = _ItemsSource;
            _ItemsSource = replacement;
            RaisePropertyChanged(nameof(ItemsSource));
            oldSource?.Dispose();
        }
        #region متغيرات البحث


  
        #region رقم الفني
        private long? _empid;

        public long? empid
        {
            get
            {
                return _empid;
            }

            set
            {
                if (_empid == value)
                    return;

                _empid = value;

                RaisePropertyChanged(nameof(empid));
            }
        }

        #endregion


        #region رقم الخدمات

        private int? _serviceid;

        public int? serviceid
        {
            get
            {
                return _serviceid;
            }

            set
            {
                if (_serviceid  == value)
                    return;

                _serviceid = value;

                RaisePropertyChanged(nameof(serviceid));
            }
        }

        #endregion

        #region order_no
        private long? _orderNo;

        public long? orderNo
        {
            get
            {
                return _orderNo;
            }

            set
            {
                if (_orderNo == value)
                    return;

                _orderNo = value;

                RaisePropertyChanged(nameof(orderNo));
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
    
        #region للفئات

        private bool _isempid;

        public bool isempid
        {
            get
            {
                return _isempid;
            }

            set
            {
                if (_isempid == value)
                    return;

                _isempid = value;

                RaisePropertyChanged(nameof(isempid));
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

        #endregion

        private EntityInstantFeedbackSource CreateItemsSource()
        {
            // Each background source owns a stable snapshot of the search.
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>()
                .UseSqlServer(App.ConnectionString).Options;
           
            bool  filterepid = isempid, filterservis = isservice;
            bool  filterDate = isdate;
            long? selectedemp = empid,  selectedservice = serviceid;
            
            DateTime? startDate = datefrom?.Date;
            DateTime? endExclusive = dateto?.Date.AddDays(1);
            var source = new EntityInstantFeedbackSource
            {
                KeyExpression = nameof(T_Emergency_MaintenanceCost.Emergency_MaintenanceCosts_No)
            };

            source.GetQueryable += (sender, e) =>
            {
                var context = new Maintenance_DabaseContext(options);
                e.Tag = context;
                e.AreSourceRowsThreadSafe = false;

                IQueryable<T_Emergency_MaintenanceCost> query = context.T_Emergency_MaintenanceCosts
                    .AsNoTracking();
                 

                if (filterepid && selectedemp.HasValue)
                {
                    query = query.Where(x =>
                        x.Employe_ID == selectedemp.Value);
                }

              

                if (filterservis && selectedservice.HasValue)
                {
                    query = query.Where(x =>
                        x.Serv_num == selectedservice.Value);
                }

                if (filterDate && startDate.HasValue && endExclusive.HasValue)
                {
                    DateOnly start = DateOnly.FromDateTime(startDate.Value);
                    DateOnly end = DateOnly.FromDateTime(endExclusive.Value);

                    query = query.Where(x =>
                        x.Date_com >= start &&
                        x.Date_com < end);
                }



                e.QueryableSource = query.
                    Include(k => k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k => k.T_Section)
                        .Include(k => k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k => k.T_Manage).Include(k => k.T_MaintenanceCosts_Parts).Include(k => k.T_FinishedMaintenances).Include(k => k.T_Employe)
                        .Include(k => k.T_Service)
                        .Include(k => k.T_Emp_Maints).ThenInclude(k => k.T_Employe)
                    .OrderByDescending(x => x.Date_com);
            };
            source.DismissQueryable += (sender, e) => (e.Tag as Maintenance_DabaseContext)?.Dispose();

            return source;
        }

        public EntityInstantFeedbackSource ItemsSource
        {
            get => _ItemsSource;
        }  // =========================================================
        // GetQueryable
        // =========================================================

        private void ItemsSource_GetQueryable(
       object sender,
       GetQueryableEventArgs e)
        {
            long selectedType = (long)_orderNo;

            DbContextHelper.InitOptions(
                App.ConnectionString
            );

            var context = new Maintenance_DabaseContext(DbContextHelper.Options);

            // إذا لم يتم اختيار نوع، أرجع IQueryable حقيقي من EF
            // ولكن بدون أي سجلات
            if (selectedType <= 0)
            {
                e.QueryableSource =
                    context.T_Emergency_MaintenanceCosts
                        .AsNoTracking()
                        .Where(x => x.Emergency_MaintenanceCosts_No == -1);

                return;
            }

            // النوع المختار
            e.QueryableSource =
                context.T_Emergency_MaintenanceCosts
                    .AsNoTracking()
                    .Where(x => x.Emergency_MaintenanceCosts_No == selectedType).Include(k => k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k => k.T_Section)
                        .Include(k => k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k => k.T_Manage).Include(k => k.T_MaintenanceCosts_Parts).Include(k => k.T_FinishedMaintenances).Include(k => k.T_Employe)
                        .Include(k => k.T_Service)
                        .Include(k => k.T_Emp_Maints).ThenInclude(k => k.T_Employe)
                            .AsNoTracking();
        }

        // =========================================================
        // Listing
        // =========================================================

        public IList listing
        {
            get
            {
                if (_listing == null && !DevExpress.Mvvm.ViewModelBase.IsInDesignMode)
                {
                    DbContextHelper.InitOptions(
                 App.ConnectionString
             );
                    using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                    _listing = context.T_Emergency_MaintenanceCosts.Select(item => new {
                        Emergency_MaintenanceCosts_No = item.Emergency_MaintenanceCosts_No,
                        T_Emergency_MaintenanceCosts_Services = item.T_Emergency_MaintenanceCosts_Services,
                        T_Employe = item.T_Employe,
                        T_Emp_Maints = item.T_Emp_Maints,
                        T_Service = item.T_Service,
                        T_FinishedMaintenances = item.T_FinishedMaintenances,
                        T_Location = item.T_Location,
                        T_MaintenanceCosts_Parts = item.T_MaintenanceCosts_Parts,
                        T_Build = item.T_Build,
                        Build_ID = item.Build_ID,
                        Date_com = item.Date_com,
                        Emergency_MaintenanceCosts_ST = item.Emergency_MaintenanceCosts_ST,
                        Employe_ID = item.Employe_ID,
                        Location_ID = item.Location_ID,
                        Serv_num = item.Serv_num,
                        Type = item.Type,
                        User_ID = item.User_ID

                    }).ToArray();
                }
                return _listing;
            }
        }


        // =========================================================
        // DataSourceRefresh
        // =========================================================

        [Command]
        public void DataSourceRefresh(DataSourceRefreshArgs args)
        {
            _listing = null;

            if (_ItemsSource == null)
                return;



            _ItemsSource.Refresh();

        }

        // =========================================================
        // ValidateRow
        // =========================================================

        [Command]
        public void ValidateRow(
            EditFormRowValidationArgs args)
        {
            if (args == null)
                return;


            var editInfo =
                args.EditOperationContext as EditIssueInfo;

            if (editInfo == null)
                return;


            var context =
                editInfo.DbContext;

            if (context == null)
                return;


            context.SaveChanges();
        }


        // =========================================================
        // ValidateRowDeletion
        // =========================================================

        [Command]
        public void ValidateRowDeletion(
            EditFormValidateRowDeletionArgs args)
        {
            if (args == null)
                return;


            // =====================================================
            // الحصول على المفتاح
            // =====================================================

            var keyObject =
                args.Keys.SingleOrDefault();

            if (keyObject == null)
                return;


            int key;

            try
            {
                key = Convert.ToInt32(keyObject);
            }
            catch
            {
                return;
            }


            // =====================================================
            // إنشاء Context
            // =====================================================

            DbContextHelper.InitOptions(
                App.ConnectionString
            );

            using (var context =
                new Maintenance_DabaseContext(DbContextHelper.Options))
            {
                // =================================================
                // التحقق من ارتباط الفئة ببيانات أخرى
                // =================================================

                bool exists =
                    context.T_Emergency_MaintenanceCosts.Any(x =>
                        x.Emergency_MaintenanceCosts_No == key);


                if (exists)
                {

                    Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الخدمات والصيانة", "لا يمكن حذف أمر الصيانة، لأنه مرتبطة ببيانات أخرى.", MessageBoxButton.OK, MessageBoxImage.Error);

                    return;
                }


                // =================================================
                // رسالة تأكيد
                // =================================================


                var result =
                    Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الخدمات والصيانة", "هل أنت متأكد من حذف أمر الصيانة؟", MessageBoxButton.YesNo, MessageBoxImage.Question);


                if (result != MessageBoxResult.Yes)
                {

                    return;
                }


                // =================================================
                // تنفيذ الحذف
                // =================================================

                var item =
                    new T_Emergency_MaintenanceCost
                    {
                        Emergency_MaintenanceCosts_No = key
                    };


                context.T_Emergency_MaintenanceCosts.Attach(item);

                context.Entry(item).State =
                    EntityState.Deleted;
               

                context.SaveChanges();


                // =================================================
                // رسالة نجاح
                // =================================================

                Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الخدمات والصيانة", "تم حذف أمر الصيانة بنجاح.", MessageBoxButton.OK, MessageBoxImage.Information);
            }


            // =====================================================
            // تحديث Grid
            // =====================================================

            if (_ItemsSource != null)
            {
                _ItemsSource.Refresh();
            }
        }
    }

}
