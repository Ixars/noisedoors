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
        public static System.Drawing.Rectangle screenBounds => Screen.PrimaryScreen.WorkingArea;
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
            int x = Global.rng.Next(0, (int)(Global.screenBounds.Width - element.ActualWidth));
            int y = Global.rng.Next(0, (int)(Global.screenBounds.Height - element.ActualHeight));

            element.Margin = new Thickness(x, y, 0, 0);
        }

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
