using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Maintenance_app.dbContext_Aggregated;

internal static class DbContextHelper
{
    #region اتصال قاعدة بيانات الفروع
    // اسم القاعدة من تعريف EF Core Power Tools. تُستخدم بيانات الخادم والمصادقة من اتصال المنظومة.
    private const string AggregatedDatabase = "Maintenance_Dabase_Aggregated ";

    internal static Maintenance_Aggregated_DabaseContext CreateContext()
    {
        var connection = new SqlConnectionStringBuilder(App.ConnectionString)
        {
            InitialCatalog = AggregatedDatabase,
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        var options = new DbContextOptionsBuilder<Maintenance_Aggregated_DabaseContext>()
            .UseSqlServer(connection.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new Maintenance_Aggregated_DabaseContext(options);
    }
    #endregion
}
