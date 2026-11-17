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

namespace Maintenance_app.viewo_Model
{
    public class mangment_viewmodel : ViewModelBase
    {
        EntityInstantFeedbackSource _ItemsSource;
        Maintenance_DabaseContext _Context;
        IList _listing;
        public EntityInstantFeedbackSource ItemsSource
        {
            get
            {
                if (_ItemsSource == null)
                {
                    _ItemsSource = new EntityInstantFeedbackSource
                    {
                        KeyExpression = nameof(T_Manage.Manag_ID)
                    };
                    DbContextHelper.InitOptions(
                     App.ConnectionString
                 );

                    _ItemsSource.GetQueryable += (sender, e) => {
                        var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                        e.QueryableSource = context.T_Manages.OrderBy(k => k.Manag).AsNoTracking();
                    };
                }
                return _ItemsSource;
            }
        }

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
                    _listing = context.T_Manages.Select(item => new { Manag_ID = item.Manag_ID, Manag = item.Manag, Location_ID = item.Location_ID, Location = item.Location, Branch_ID = item.Branch_ID }).ToArray();

                }
                return _listing;
            }
        }

        [Command]
        public void DataSourceRefresh(DataSourceRefreshArgs args)
        {
            //MessageBox.Show("ValidateRowDeletion تم استدعاؤها");
            _listing = null;
            ItemsSource.Refresh();
        }

        [Command]
        public void ValidateRow(EditFormRowValidationArgs args)
        {
            var context = ((EditIssueInfo)args.EditOperationContext).DbContext;
            context.SaveChanges();
        }


        [Command]
        public void ValidateRowDeletion(EditFormValidateRowDeletionArgs args)
        {
            var key = (short)args.Keys.Single();
            DbContextHelper.InitOptions(
                App.ConnectionString
            );
            using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
            // التحقق من ارتباط الادارة او القسم بالمستندات
            bool exists = context.T_Emergency_MaintenanceCosts_Services.Any(x =>
                x.Manag_ID == key);
            if (exists)
            {
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

                ThemedMessageBox.Show(
                    null,
                    title: "منظومة الخدمات والصيانة",
                    text: "لا يمكن حذف الادارة او القسم، لأنها مرتبطة ببيانات أخرى.",
                    messageBoxButtons: MessageBoxButton.OK,
                    defaultButton: MessageBoxResult.OK,
                    icon: MessageBoxImage.Error,
                    true);

                ApplicationThemeHelper.ApplicationThemeName = "None";

                return;
            }
            // تأكيد الحذف
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

            var result = ThemedMessageBox.Show(
                null,
                title: "منظومة الخدمات والصيانة",
                text: "هل أنت متأكد من حذف الادارة او القسم؟",
                messageBoxButtons: MessageBoxButton.YesNo,
                defaultButton: MessageBoxResult.No,
                icon: MessageBoxImage.Question,
                true);

            if (result != MessageBoxResult.Yes)
            {
                ApplicationThemeHelper.ApplicationThemeName = "None";
                return;
            }
            // تنفيذ الحذف
            var item = new T_Manage
            {
                Manag_ID = key
                ,
                Location = App.location.Location,
                Branch_ID = App.location.Branch_ID,
                Location_ID = App.location.Location_ID
            };

            context.Entry(item).State = EntityState.Deleted;

            context.SaveChanges();

            ThemedMessageBox.Show(
                null,
                title: "منظومة الخدمات والصيانة",
                text: "تم حذف الادارة او القسم بنجاح.",
                messageBoxButtons: MessageBoxButton.OK,
                defaultButton: MessageBoxResult.OK,
                icon: MessageBoxImage.Information,
                true);
            ApplicationThemeHelper.ApplicationThemeName = "None";

        }

    }

}
