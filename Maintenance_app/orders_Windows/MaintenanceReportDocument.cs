using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Maintenance_app.orders_Windows;

// يحتفظ بصفحات التقرير وعناوين صورها في حزمة ذاكرة طوال عمر المعاينة فقط.
public sealed class MaintenanceReportDocument : IDisposable
{
    #region حزمة الصور ومدة بقائها
    private readonly MemoryStream memory = new();
    private readonly Package package;
    // العنوان فريد لكل فتح، فلا تختلط صفحات تقريرين ولا يحتاج العارض ملفات عمل مؤقتة.
    private readonly Uri packageUri = new("memorystream://maintenance-preview/" + Guid.NewGuid().ToString("N") + ".zip");
    private bool disposed;
    public FixedDocument Document { get; } = new();

    internal MaintenanceReportDocument()
    {
        package = Package.Open(memory, FileMode.Create, FileAccess.ReadWrite);
        PackageStore.AddPackage(packageUri, package);
    }

    // إزالة التسجيل تأتي بعد فصل المستند عن العارض؛ التكرار آمن عند إغلاق النافذة واللوحة معًا.
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        PackageStore.RemovePackage(packageUri);
        package.Close(); memory.Dispose();
    }
    #endregion

    #region إنشاء صفحة لها عنوان صالح للتحديد والطباعة
    internal void AddPage(Stream source, double dpi)
    {
        var partUri = PackUriHelper.CreatePartUri(new Uri($"/pages/page-{Document.Pages.Count + 1}.png", UriKind.Relative));
        var part = package.CreatePart(partUri, "image/png", CompressionOption.NotCompressed);
        source.Position = 0;
        using (var destination = part.GetStream(FileMode.Create, FileAccess.Write)) source.CopyTo(destination);
        package.Flush();

        // FixedDocument يعيد فتح صورة الصفحة عند مرور المؤشر والتحديد والطباعة.
        // UriSource الصالح يمنع تفسير الاسم الافتراضي للصورة «image» كمسار ملف على القرص.
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = PackUriHelper.Create(packageUri, partUri);
        bitmap.EndInit(); bitmap.Freeze();
        double width = bitmap.PixelWidth * 96d / dpi, height = bitmap.PixelHeight * 96d / dpi;
        var page = new FixedPage { Width = width, Height = height, Background = Brushes.White };
        page.Children.Add(new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.Fill });
        Document.Pages.Add(new PageContent { Child = page });
        if (Document.Pages.Count == 1) Document.DocumentPaginator.PageSize = new Size(width, height);
    }
    #endregion
}
