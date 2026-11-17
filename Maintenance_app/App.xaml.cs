using DevExpress.Xpf.Core;
using DevExpress.Xpf.Printing;
using Maintenance_app.converts;
using Maintenance_app.dataSet;
using Maintenance_app.main_Windows;
using Maintenance_app.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Transactions;
using System.Windows;
using Application = System.Windows.Application;

namespace Maintenance_app
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        #region بيانات الجلسة وإعدادات الاتصال المشتركة

        public static int mid;
        public static bool is_but = false;
         public static T_Location location { get; set; }
        public static ObservableCollection<T_Section> _listclass { get; set; }
        public static T_SystemUser login_user { get; set; }
        public static string Branch_Name { get; set; }
        public static T_SystemUser appuser { get; set; }
        // تُقرأ بيانات الاتصال من إعدادات التثبيت أو التسجيل عند بدء التشغيل.
        public static string saPassword, DB_US,  usname, ConnectionString;

        #endregion

        public static string decrypt(string encryptpwd)
        {
            string decryptpwd = string.Empty;
            UTF8Encoding encodepwd = new UTF8Encoding();
            Decoder Decode = encodepwd.GetDecoder();
            byte[] todecode_byte = Convert.FromBase64String(encryptpwd);
            int charCount = Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount - 1 + 1];
            Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
            decryptpwd = new String(decoded_char);
            return decryptpwd;
        }
        public static string encrypt(string password)
        {
            string strmsg = string.Empty;
            byte[] encode = new byte[password.Length - 1 + 1];
            encode = Encoding.UTF8.GetBytes(password);
            strmsg = Convert.ToBase64String(encode);
            return strmsg;
        }
        public bool checkUpRegApp1()
        {
            const string appName = "Maintenance_services";
            const string expectedCode = "90764299750094";
            const string branchName = "";
            string registryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SSF\" + appName;
        
            try
            {
                bool is64Bit = Environment.Is64BitOperatingSystem;
                RegistryView view = is64Bit ? RegistryView.Registry64 : RegistryView.Registry32;
                bool registrationRequired;
                string userName = string.Empty;
                string password = string.Empty;
        
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    string? code = ReadRegistrationValue(baseKey, registryPath, "CodeRun", false);
                    registrationRequired = !string.Equals(code, expectedCode, StringComparison.Ordinal);
        
                    if (!registrationRequired)
                    {
                        string storedUser = ReadRegistrationValue(baseKey, registryPath, "NameUserSQL", true)!;
                        string storedPassword = ReadRegistrationValue(baseKey, registryPath, "PassUserSQL", true)!;
        
                        userName = string.IsNullOrEmpty(storedUser) ? string.Empty : decrypt(storedUser);
                        password = string.IsNullOrEmpty(storedPassword) ? string.Empty : decrypt(storedPassword);
                    }
                }
        
                // تُغلق جميع مفاتيح Registry قبل تشغيل التسجيل والخروج.
                if (registrationRequired)
                {
                    StartProductRegistration(is64Bit);
                    return false;
                }
        
                // لا تُحدّث الإعدادات إلا بعد نجاح قراءة القيمتين وفكهما.
                Branch_Name = branchName;
                DB_US = userName;
                saPassword = password;
                return true;
            }
            catch (Exception ex)
            {
                Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الصيانة والخدمات", "تعذّر التحقق من التسجيل أو قراءة إعدادات الاتصال.\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        private static string? ReadRegistrationValue(
            RegistryKey baseKey, string registryPath, string valueName, bool required)
        {
            using (RegistryKey? key = baseKey.OpenSubKey(registryPath + "\\" + valueName, false))
            {
                object? rawValue = key == null
                    ? null
                    : key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string? value = rawValue as string;
        
                // غياب رمز التشغيل أو عدم كونه نصًا يستدعي التسجيل.
                // إعدادات الاتصال مطلوبة؛ النص الفارغ مسموح كما في الكود الأصلي.
                if (value == null && required)
                {
                    throw new InvalidOperationException(
                        "إعداد التسجيل «" + valueName + "» غير موجود أو ليس قيمة نصية.");
                }
        
                return value;
            }
        }
        
        private static void StartProductRegistration(bool is64Bit)
        {
            string fileName = is64Bit ? "RegisterProduct64.exe" : "RegisterProduct32.exe";
            string executablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("ملف تسجيل المنظومة غير موجود: " + fileName);
            }
        
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
        
            using (Process? process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("تعذّر تشغيل برنامج تسجيل المنظومة.");
                }
            }
        
        }

        #region تشغيل المنظومة وقراءة إعدادات الاتصال

        private void Application_Startup(object sender, StartupEventArgs e)
        {

            try
            {
                // تسجيل الـ Assembly بالكامل كمنطقة موثوقة (يُفضل لتجنب أي مشاكل مستقبلية مع بقية الـ DataSets)
                DevExpress.Utils.DeserializationSettings.RegisterTrustedAssembly(typeof(DataSet1).Assembly);
                // تسجيل الـ Assembly بالكامل كمنطقة موثوقة (يُفضل لتجنب أي مشاكل مستقبلية مع بقية الـ DataSets)
                DevExpress.Utils.DeserializationSettings.RegisterTrustedAssembly(typeof(sub_data).Assembly);

                // أو تسجيل الكلاس المحدد بالذات فقط:
                // DevExpress.Utils.DeserializationSettings.RegisterTrustedClass(typeof(Maintenance_app.dataSet.DataSet1));
                // أو تسجيل الكلاس المحدد بالذات فقط:
                // DevExpress.Utils.DeserializationSettings.RegisterTrustedClass(typeof(Maintenance_app.dataSet.DataSet1));
                ApplicationThemeHelper.ApplicationThemeName = "None";

                PrintingLocalizer.Active = new ArabicPrintingLocalizer();
                CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ar-LY");

                // يستخدم التثبيت الجديد اتصالًا محميًا خاصًا بحساب Windows، وتستمر طريقة التسجيل القديمة لنسخ التطوير.
                string installedSettings = Path.Combine(AppContext.BaseDirectory,
                    Maintenance_app.DatabContext.InstalledConnectionSettings.FileName);
                if (File.Exists(installedSettings))
                {
                    ConnectionString = Maintenance_app.DatabContext.InstalledConnectionSettings.Read(installedSettings);
                    new Login_Windows().ShowDialog();
                    return;
                }

                if (!checkUpRegApp1())
                {
                    Shutdown();
                    return;
                }
                if (File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + @"\ConectionString") == false)
                {

                    Maintenance_app.Dashboard.UiMessages.Show(null, "منظومة الصيانة والخدمات", "ملف الاتصال غير موجود بجانب البرنامج:\n" + Path.Combine(AppContext.BaseDirectory, "ConectionString"), MessageBoxButton.OK, MessageBoxImage.Error);

                
                    Shutdown(1);
                    return;
                }
                else
                {
                    string connectionTemplate = File.ReadAllText(
                        Path.Combine(AppContext.BaseDirectory, "ConectionString"));
                    ConnectionString = Maintenance_app.DatabContext.SqlConnectionSettings.Create(
                        connectionTemplate, DB_US, saPassword);
                      Login_Windows login = new Login_Windows();
                    login.ShowDialog();
                }
            }
            catch (Exception ex)
            {

                Maintenance_app.Dashboard.UiMessages.Show(null, "خطأ غير متوقع سيتم فرض إغلاق التطبيق", $"{ex.Message}", MessageBoxButton.OK, MessageBoxImage.Error);

                App.Current.Shutdown();

            }

        }

        #endregion
      }


}
