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
        public static bool isHijacked = false;
        public static bool hijackPause = false;
        public static bool changingState = false;

        // ---------------------- PUBLIC METHODS ----------------------
        public static Random rng = new Random();
        public static string[] ransomHashes = {
            "b38d822c7b83a5fa5c20dccf22188529f9ad75caa1e15885b994f5787f733bb8",
            "7abedb7bd160a5b1ef7467dd58dd70b6aa50b2983f54c7dab0cd100adb645dcc",
            "0f2051005cf9120f471f4d8f3c92e366eb330200c539c5019cfda5be6cd8435e",
            "390cd31ac20cdeff0127d074aff0728157fbc5ef1f93e8ba98042bf22c1c6fc6"
        };

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
