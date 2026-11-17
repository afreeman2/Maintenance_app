using Microsoft.Data.SqlClient;

namespace Maintenance_app.DatabContext
{
    internal static class SqlConnectionSettings
    {
        public static string Create(string connectionTemplate, string userName, string password)
        {
            var connection = new SqlConnectionStringBuilder(connectionTemplate)
            {
                UserID = userName,
                Password = password
            };

            if (string.IsNullOrWhiteSpace(connection.DataSource))
                throw new InvalidOperationException("لم يُحدّد خادم قاعدة البيانات في ملف الاتصال.");

            if (!connection.ShouldSerialize("Encrypt"))
                connection.Encrypt = SqlConnectionEncryptOption.Mandatory;

            // The local SQL instance uses a self-signed certificate. Keep TLS enabled
            // and scope this compatibility default to the current computer only.
            // Preserve explicit certificate settings and Strict encryption.
            if (IsLocalServer(connection.DataSource)
                && !connection.ShouldSerialize("TrustServerCertificate")
                && connection.Encrypt != SqlConnectionEncryptOption.Strict)
            {
                connection.TrustServerCertificate = true;
            }

            return connection.ConnectionString;
        }

        private static bool IsLocalServer(string dataSource)
        {
            string host = dataSource.Trim();
            if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                host = host[4..];

            host = host.Split('\\', ',')[0].Trim();
            return host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
                || host == "."
                || host == "127.0.0.1"
                || host == "::1"
                || host == "[::1]";
        }
    }
}
