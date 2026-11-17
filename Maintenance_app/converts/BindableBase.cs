using DevExpress.Mvvm;
using Maintenance_app.dbContext;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Maintenance_app.converts
{
    public class EditIssueInfo : BindableBase
    {
        public EditIssueInfo(Maintenance_DabaseContext dbContext, IList users)
        {
            DbContext = dbContext;
            Users = users;
        }
        public Maintenance_DabaseContext DbContext { get; }
        public IList Users { get; }
    }
}
