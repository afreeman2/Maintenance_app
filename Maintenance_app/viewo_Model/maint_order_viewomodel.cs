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
    internal class maint_order_viewomodel: ViewModelBase
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
                        KeyExpression = nameof(T_Emergency_MaintenanceCost.Emergency_MaintenanceCosts_No)
                    };
                    DbContextHelper.InitOptions(
                  App.ConnectionString
              );

                    _ItemsSource.GetQueryable += (sender, e) => {
                        var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                        e.QueryableSource = context.T_Emergency_MaintenanceCosts.Include(k=> k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k=> k.T_Section)
                        .Include(k => k.T_Emergency_MaintenanceCosts_Services).ThenInclude(k=> k.T_Manage).Include(k=> k.T_MaintenanceCosts_Parts).Include(k=> k.T_FinishedMaintenances).Include(k=> k.T_Employe)
                        .Include(k=> k.T_Service)
                        .Include(k=> k.T_Emp_Maints).ThenInclude(k=> k.T_Employe).AsNoTracking();
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
                    _listing = context.T_Emergency_MaintenanceCosts.Select(item => new {
                        Emergency_MaintenanceCosts_No = item.Emergency_MaintenanceCosts_No,
                        T_Emergency_MaintenanceCosts_Services = item.T_Emergency_MaintenanceCosts_Services,
                        T_Employe = item.T_Employe,
                        T_Emp_Maints = item.T_Emp_Maints,
                        T_Service = item.T_Service,
                        T_FinishedMaintenances = item.T_FinishedMaintenances,
                        T_Location = item.T_Location,
                    T_MaintenanceCosts_Parts =  item.T_MaintenanceCosts_Parts,
                    T_Build = item.T_Build,
                        Build_ID = item.Build_ID,
                    Date_com=item.Date_com,
                        Emergency_MaintenanceCosts_ST = item.Emergency_MaintenanceCosts_ST,
                        Employe_ID = item.Employe_ID,
                    Location_ID=item.Location_ID,
                        Serv_num = item.Serv_num,
                        Type = item.Type,
                    User_ID=item.User_ID}).ToArray();

                    //App._listemails = new ObservableCollection<T_email_reciver>((List<T_email_reciver>)_listing);

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
        //[Command]
        //public void ValidateRow(RowValidationArgs args)
        //{
        //    MessageBox.Show("RowValidationArgs تم استدعاؤها");
        //    var item = (T_email_reciver)args.Item;
        //    if (args.IsNewItem)
        //        _Context.T_email_recivers.Add(item);
        //    _Context.SaveChanges();
        //}

        [Command]
        public void ValidateRowDeletion(EditFormValidateRowDeletionArgs args)
        {
            var key = (int)args.Keys.Single();
            DbContextHelper.InitOptions(
                App.ConnectionString
            );
            using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
            // التحقق من ارتباط الجهة بالمستندات

            // تأكيد الحذف

            var result = Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الخدمات والصيانة", "هل أنت متأكد من حذف الجهة؟", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            // تنفيذ الحذف
            var item = new T_Emergency_MaintenanceCost
            {
                Emergency_MaintenanceCosts_No = key
            };

            context.Entry(item).State = EntityState.Deleted;

            context.SaveChanges();
            //var cached = App._listemails?.FirstOrDefault(email => email.id_email_reciver == item.id_email_reciver);
            //if (cached != null)
            //{
            //    App._listemails.Remove(cached);
            //}
            Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الخدمات والصيانة", "تم حذف امر الصيانة.", MessageBoxButton.OK, MessageBoxImage.Information);

        }

    }
}
