using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Maintenance_app.converts
{
    public class ByteArrayToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not byte[] imageBytes || imageBytes.Length == 0) return null;
            using (var strm = new MemoryStream(imageBytes))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = strm;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var image = (BitmapSource)value;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }
    }

}
