using System;
using System.Collections.Generic;
using System.Text;

namespace Maintenance_app.dbContext
{
    public sealed class UserActivityRow
    {
        public int User_ID { get; init; }
        public string User_Name { get; init; } = "";
        public bool forbuild { get; init; }
        public bool fordon { get; init; }
        public bool foremp { get; init; }
        public bool forlocation { get; init; }
        public bool formaint { get; init; }
        public bool formaintrepo { get; init; }
        public bool forservices { get; init; }
        public bool mang_check { get; init; }
        public bool foruser { get; init; }
        public bool formangreq { get; init; }
        public bool branch_report { get; init; }
        public long OperationCount { get; init; }
    }

    public static class UserActivityService
    {
        #region عرض صلاحيات المستخدمين في البطاقات
        // يقرأ الإسقاط صلاحية الإنهاء ضمن العدّاد من السجل نفسه الذي تعتمد عليه الرئيسية.
        public static IQueryable<UserActivityRow> Query(Maintenance_DabaseContext db) => db.T_SystemUsers
            .Where(user => user.st == true)
            .Select(user => new UserActivityRow
            {
                User_ID = user.User_ID,
                User_Name = user.User_Name,
                forbuild = user.forbuild,
                fordon = user.fordon,
                foremp = user.foremp,
                forlocation = user.forlocation,
                formaint = user.formaint,
                formaintrepo = user.formaintrepo,
                forservices = user.forservices,
                mang_check = user.mang_check,
                foruser = user.foruser,
                formangreq = user.formangreq,
                branch_report = user.branch_report,
                OperationCount = db.T_Observers.LongCount(log => log.User_id == user.User_ID)
            });
        #endregion
    }

}
