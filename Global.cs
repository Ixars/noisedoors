using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using WpfApplication = System.Windows.Application;

namespace Noise
{
    internal class Global
    {
        public static bool tvOn = false;
        public static MainWindow? mainWindow;

        // ---------------------- PUBLIC METHODS ----------------------
        public static Random rng = new Random();

        public static BitmapImage LoadBitmapImage(string uri)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(uri);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        static public Stream GetResourceSteam(string uri)
        {
            return WpfApplication.GetResourceStream(new Uri($"pack://application:,,,/Assets/{uri}")).Stream;
        }

        /// <summary>
        /// Randomly positions a control within the screen bounds.
        /// </summary>
        public static void RandomPosControl(FrameworkElement element)
        {
            MainWindow? window = mainWindow;
            if (window == null) { return; }

            double screenW = window.ActualWidth;
            double screenH = window.ActualHeight;

            double elemW = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
            double elemH = element.ActualHeight > 0 ? element.ActualHeight : element.Height;

            int maxX = (int)Math.Max(0, screenW - elemW);
            int maxY = (int)Math.Max(0, screenH - elemH);

            int x = rng.Next(0, Math.Max(1, maxX));
            int y = rng.Next(0, Math.Max(1, maxY));

            element.Margin = new Thickness(x, y, 0, 0);
        }

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
