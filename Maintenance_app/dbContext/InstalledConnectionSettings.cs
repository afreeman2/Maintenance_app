using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Maintenance_app.DatabContext;

internal static class InstalledConnectionSettings
{
    #region حفظ اتصال النسخة المثبتة

    public const string FileName = "InstalledConnection.dat";

    // يحمي Windows بيانات الاتصال بحيث تُقرأ من حساب Windows الذي ثبّت المنظومة.
    public static void Write(string directory, string connectionString)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(connectionString);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, FileName),
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string Read(string path)
    {
        byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    #endregion
}
