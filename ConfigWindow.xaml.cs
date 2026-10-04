using System.Windows;

namespace Noise
{
    public partial class ConfigWindow : Window
    {
        private bool _loaded = false;
        public ConfigWindow() { InitializeComponent(); }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            cb_spawnAutomatically.IsChecked = Config.SpawnAutomatically;
            tb_minSpawnDelay.Text = Config.MinSpawnDelay.ToString();
            tb_maxSpawnDelay.Text = Config.MaxSpawnDelay.ToString();
            tb_noiseSpeed.Text = Config.NoiseSpeed.ToString();
            tb_fireAlarmAmount.Text = Config.FireAlarmAmount.ToString();

            cb_crashOnDeath.IsChecked = Config.CrashOnDeath;
            cb_cmdOnDeath.IsChecked = Config.ExecCMDOnDeath;
            tb_cmd.Text = Config.CMDOnDeath;

            tb_cmd.IsEnabled = cb_cmdOnDeath.IsChecked ?? false;
            tb_minSpawnDelay.IsEnabled = cb_spawnAutomatically.IsChecked ?? false;
            tb_maxSpawnDelay.IsEnabled = cb_spawnAutomatically.IsChecked ?? false;
            _loaded = true;
        }

        private void FieldUpdated()
        {
            if (!_loaded) return;

            tb_cmd.IsEnabled = cb_cmdOnDeath.IsChecked ?? false;

            tb_minSpawnDelay.IsEnabled = cb_spawnAutomatically.IsChecked ?? false;
            tb_maxSpawnDelay.IsEnabled = cb_spawnAutomatically.IsChecked ?? false;

            Config.SpawnAutomatically = cb_spawnAutomatically.IsChecked ?? false;
            Config.MinSpawnDelay = ParseClamped(tb_minSpawnDelay.Text, Config.MinSpawnDelay, 0, 86400);
            Config.MaxSpawnDelay = ParseClamped(tb_maxSpawnDelay.Text, Config.MaxSpawnDelay, 0, 86400);
            Config.NoiseSpeed = ParseClamped(tb_noiseSpeed.Text, Config.NoiseSpeed, 1, 100);
            Config.FireAlarmAmount = ParseClamped(tb_fireAlarmAmount.Text, Config.FireAlarmAmount, 1, 100);

            Config.CrashOnDeath = cb_crashOnDeath.IsChecked ?? false;
            Config.ExecCMDOnDeath = cb_cmdOnDeath.IsChecked ?? false;
            Config.CMDOnDeath = tb_cmd.Text;

            Config.SaveConfig();
        }

        private void FieldUpdated(object sender, RoutedEventArgs e) => FieldUpdated();
        private void FieldUpdated(object sender, System.Windows.Controls.TextChangedEventArgs e) => FieldUpdated();

        private static int ParseClamped(string text, int fallback, int min, int max)
        {
            int value = Int32.TryParse(text, out int parsed) ? parsed : fallback;
            return Math.Clamp(value, min, max);
        }

        private void btn_spawn_Click(object sender, RoutedEventArgs e)
        {
            _=Global.mainWindow.NoiseEmerge();
            // Cuts the current spawn delay
            Global.mainWindow?.RetriggerSpawnLoop();
        }

    }
}
