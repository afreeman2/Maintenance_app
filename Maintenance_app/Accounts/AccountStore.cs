using System.Data;
using System.Text;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Microsoft.EntityFrameworkCore;

namespace Maintenance_app.Accounts;

internal sealed class AccountStore(Func<Maintenance_DabaseContext>? contextFactory)
{
    #region الاتصال والتحقق من صلاحية إدارة الحسابات
    internal Maintenance_DabaseContext CreateContext() => contextFactory?.Invoke() ??
        new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>()
            .UseSqlServer(App.ConnectionString).Options);

    // تُراجع الصلاحية من القاعدة قبل القراءة أو التعديل، حتى إذا تغيّرت أثناء الجلسة.
    internal static async Task<T_SystemUser> RequireAdministratorAsync(Maintenance_DabaseContext db, int id) =>
        await db.T_SystemUsers.SingleOrDefaultAsync(user => user.User_ID == id && user.foruser)
        ?? throw new InvalidOperationException("حسابك لا يملك صلاحية إدارة المستخدمين.");
    #endregion

    #region ترميز كلمة المرور وتغييرها
    // يبقى ترميز UTF-8/Base64 متوافقًا مع شاشة الدخول والعملاء الحاليين.
    internal static string Encode(string password) => Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

    internal async Task<bool> ChangePasswordAsync(int id, string current, string replacement)
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var account = await db.T_SystemUsers.SingleOrDefaultAsync(user => user.User_ID == id)
            ?? throw new InvalidOperationException("الحساب الحالي غير موجود.");
        if (!string.Equals(App.decrypt(account.PassWord), current, StringComparison.Ordinal)) return false;
        account.PassWord = Encode(replacement);
        account.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
    #endregion
}
