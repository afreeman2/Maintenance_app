using DevExpress.Data.Linq;



using DevExpress.Mvvm;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm.Xpf;
using DevExpress.Xpf.Core;
using Maintenance_app.DatabContext;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using System.Collections;

using Microsoft.EntityFrameworkCore;
using Maintenance_app.converts;
using System.Windows;
using System.Collections.ObjectModel;


namespace Maintenance_app.viewo_Model
{
    public class section_viewmodel : ViewModelBase
    {
        // =========================================================
        // Fields
        // =========================================================

        private EntityInstantFeedbackSource _ItemsSource;


        private IList _listing;


        // =========================================================
        // Constructor
        // =========================================================

        public section_viewmodel()
        {
        }


        // =========================================================
        // Manag_ID
        // =========================================================

        private short? _Manag_ID;

        public short? Manag_ID
        {
            get
            {
                return _Manag_ID;
            }

            set
            {
                if (_Manag_ID == value)
                    return;

                _Manag_ID = value;

                RaisePropertyChanged(nameof(Manag_ID));
            }
        }

        // =========================================================
        // أنواع الفئات
        // =========================================================

        public ObservableCollection<T_Manage> T_Manage
        {
            get
            {
                DbContextHelper.InitOptions(
                    App.ConnectionString
                );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                {
                    return new ObservableCollection<T_Manage>(
                        context.T_Manages
                            .AsNoTracking()
                            .OrderBy(x => x.Manag)
                            .ToList()
                    );
                }
            }
        }


        // =========================================================
        // EntityInstantFeedbackSource
        // =========================================================


        public EntityInstantFeedbackSource ItemsSource
        {
            get
            {
                if (_ItemsSource == null)
                {
                    _ItemsSource =
                        new EntityInstantFeedbackSource();

                    _ItemsSource.KeyExpression =
                        nameof(T_Section.Secti_ID);

                    DbContextHelper.InitOptions(
                                     App.ConnectionString
                                 );

                    _ItemsSource.GetQueryable += (sender, e) => {
                        var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                        e.QueryableSource = context.T_Sections.Where(k => k.Manag_ID == Manag_ID).OrderBy(k => k.Secti).AsNoTracking();
                    };
                }

                return _ItemsSource;
            }
        }

        // =========================================================
        // GetQueryable
        // =========================================================

        private void ItemsSource_GetQueryable(object sender, GetQueryableEventArgs e)
        {
            int selectedType = (int)_Manag_ID;

            DbContextHelper.InitOptions(App.ConnectionString);

            var context = new Maintenance_DabaseContext(DbContextHelper.Options);

            // إذا لم يتم اختيار نوع، أرجع IQueryable حقيقي من EF
            // ولكن بدون أي سجلات
            if (selectedType <= 0)
            {
                e.QueryableSource =
                    context.T_Sections
                        .AsNoTracking()
                        .Where(x => x.Manag_ID == -1);

                return;
            }

            // النوع المختار
            e.QueryableSource =
                context.T_Sections
                    .AsNoTracking()
                    .Where(x => x.Manag_ID == selectedType)
                    .OrderBy(x => x.Secti);
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
                    var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                    _listing = context.T_Sections.Select(item => new { Secti_ID = item.Secti_ID, Manag_ID = item.Manag_ID, Secti = item.Secti, Location_ID = item.Location_ID, Location = item.Location, Branch_ID = item.Branch_ID }).ToArray();
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


            short key;

            try
            {
                key = Convert.ToInt16(keyObject);
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
                // التحقق من ارتباط القسم او الوحدة ببيانات أخرى
                // =================================================

                bool exists =
                    context.T_Emergency_MaintenanceCosts_Services.Any(x =>
                        x.Secti_ID == key);


                if (exists)
                {
                    ApplicationThemeHelper.ApplicationThemeName =
                        "Win11Light";

                    ThemedMessageBox.Show(
                        null,
                        title: "منظومة المحفوظات",
                        text:
                            "لا يمكن حذف القسم او الوحدة، لأنها مرتبطة ببيانات أخرى.",
                        messageBoxButtons:
                            MessageBoxButton.OK,
                        defaultButton:
                            MessageBoxResult.OK,
                        icon:
                            MessageBoxImage.Error,
                        true);

                    ApplicationThemeHelper.ApplicationThemeName =
                        "None";

                    return;
                }


                // =================================================
                // رسالة تأكيد
                // =================================================

                ApplicationThemeHelper.ApplicationThemeName =
                    "Win11Light";

                var result =
                    ThemedMessageBox.Show(
                        null,
                        title: "منظومة المحفوظات",
                        text:
                            "هل أنت متأكد من حذف القسم او الوحدة؟",
                        messageBoxButtons:
                            MessageBoxButton.YesNo,
                        defaultButton:
                            MessageBoxResult.No,
                        icon:
                            MessageBoxImage.Question,
                        true);


                if (result != MessageBoxResult.Yes)
                {
                    ApplicationThemeHelper.ApplicationThemeName =
                        "None";

                    return;
                }


                // =================================================
                // تنفيذ الحذف
                // =================================================

                var item =
                    new T_Section
                    {
                        Secti_ID = key
                        ,Branch_ID=App.location.Branch_ID,
                        Location=App.location.Location,
                        Location_ID=App.location.Location_ID
                    };


                context.T_Sections.Attach(item);

                context.Entry(item).State =
                    EntityState.Deleted;
                //if (App._listclass != null && App._listclass.Count > 0)
                //{
                //    ObservableCollection<T_Section> listclass = App._listclass;
                //    App._listclass.Remove(App._listclass.Where(k => k.Secti_ID == item.Secti_ID).SingleOrDefault());
                //}

                context.SaveChanges();


                // =================================================
                // رسالة نجاح
                // =================================================

                ThemedMessageBox.Show(
                    null,
                    title: "منظومة المحفوظات",
                    text:
                        "تم حذف القسم او الوحدة بنجاح.",
                    messageBoxButtons:
                        MessageBoxButton.OK,
                    defaultButton:
                        MessageBoxResult.OK,
                    icon:
                        MessageBoxImage.Information,
                    true);


                ApplicationThemeHelper.ApplicationThemeName =
                    "None";
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
