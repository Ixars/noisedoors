using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using XamlAnimatedGif;
using DrawingPoint = System.Drawing.Point;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsKeys = System.Windows.Forms.Keys;
using WpfApplication = System.Windows.Application;

namespace Noise
{
    public partial class MainWindow : Window
    {
        private const int TickIntervalMs = 10;
        private const int CursorHistoryDelayMs = 200;
        private const int ThreatDelayMs = 400;
        private const int NoiseIdleThreshold = 30;
        private const int DeathDistance = 32;
        private const double FireAlarmFadeDistance = 100.0;
        private const int IdleHistoryShiftMs = 10;

        private readonly DispatcherTimer _timer;
        private readonly List<CursorSample> _cursorPositions = new();
        private readonly List<KeySample> _pressedKeys = new();

        private DrawingPoint _lastCursorPosition;
        private int _keyPressedDebounce;
        private NotifyIcon? _trayIcon;
        private CancellationTokenSource? _spawnDelayCts;
        private Thickness _previousNoisePos;
        private IntPtr _hwnd;
        
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        private readonly SoundHandle _tvStatic = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noisestatic.wav"));
        private readonly SoundHandle _noiseIdle = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noiseidle.wav"));
        private readonly SoundHandle _noiseThreat = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noise_threat.wav"));
        private readonly SoundHandle _noiseEmerge = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noiseemerge.wav"));
        private readonly SoundHandle _noiseEmergeMusic = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noiseemerge_music.wav"));
        private readonly SoundHandle _fireAlarm = SoundHandle.Create(Global.GetResourceSteam("Sounds/firealarm.wav"));
        private readonly SoundHandle _fireAlarmPull = SoundHandle.Create(Global.GetResourceSteam("Sounds/Fire_alarm_pull.wav"));
        private readonly SoundHandle _noiseDeath1 = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noise_death_1.wav"));
        private readonly SoundHandle _noiseDeathMusic = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noise_death_music.wav"));
        private readonly SoundHandle _noisePause = SoundHandle.Create(Global.GetResourceSteam("Sounds/Noisepause2.wav"));
        private readonly Keyboard _keyboard = new();

        private sealed class CursorSample
        {
            public long Time { get; set; }
            public Thickness Position { get; set; }

            public CursorSample(long time, Thickness position)
            {
                Time = time;
                Position = position;
            }
        }

        private sealed class KeySample
        {
            public long Time { get; set; }
            public FormsKeys Key { get; }

            public KeySample(long time, FormsKeys key)
            {
                Time = time;
                Key = key;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
            _lastCursorPosition = FormsCursor.Position;
            _previousNoisePos = new Thickness();

            Global.mainWindow = this;
        }

        private void UpdateDpiScale()
        {
            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } ct)
            {
                _dpiScaleX = ct.TransformToDevice.M11;
                _dpiScaleY = ct.TransformToDevice.M22;
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _dpiScaleX = newDpi.DpiScaleX;
            _dpiScaleY = newDpi.DpiScaleY;
        }

        private Point CursorPositionDip()
        {
            DrawingPoint p = FormsCursor.Position;
            return new Point(p.X / _dpiScaleX, p.Y / _dpiScaleY);
        }

        private Point PointToScreenDip(Visual visual, Point dip)
        {
            Point physical = visual.PointToScreen(dip);
            return new Point(physical.X / _dpiScaleX, physical.Y / _dpiScaleY);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            MakeClickThrough();
            UpdateDpiScale();

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowState = WindowState.Maximized;

            _keyboard.KeyPressed += OnKeyPressed;
            Closing += OnClosing;
            Closed += OnClosed;

            _keyboard.Hook();

            SetupTickTimer();
            SetupTrayIcon();

            if (!Config.LoadConfig()) { new ConfigWindow().Show(); }
            _ = SpawnLoopAsync();
        }

        private void OnKeyPressed(FormsKeys key)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _pressedKeys.Add(new KeySample(currentUnixTime, key));
                _keyPressedDebounce++;
            }));
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _trayIcon?.Visible = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _trayIcon?.Dispose();
            _spawnDelayCts?.Cancel();
        }

        private void SetupTrayIcon()
        {
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            trayMenu.Items.Add("Configuration").Click += (s, e) => new ConfigWindow().Show();
            trayMenu.Items.Add("Close").Click += (s, e) => WpfApplication.Current.Shutdown();

            _trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? System.Drawing.SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Text = "Noise",
                Visible = true
            };
        }

        // -------------------- SPAWN LOOP --------------------

        private async Task SpawnLoopAsync()
        {
            while (true)
            {
                if (!Config.SpawnAutomatically)
                {
                    await InterruptibleDelay(250);
                    continue;
                }

                int minDelay = Math.Min(Config.MinSpawnDelay, Config.MaxSpawnDelay) * 1000;
                int maxDelay = Math.Max(Config.MinSpawnDelay, Config.MaxSpawnDelay) * 1000;

                await InterruptibleDelay(Global.rng.Next(minDelay, maxDelay));

                try { await NoiseEmerge(); }
                catch (OperationCanceledException) { }
                catch { break; }
            }
        }

        public void RetriggerSpawnLoop() => _spawnDelayCts?.Cancel();

        private async Task InterruptibleDelay(int milliseconds)
        {
            _spawnDelayCts?.Cancel();

            using CancellationTokenSource cts = new CancellationTokenSource();
            _spawnDelayCts = cts;

            try
            {
                await Task.Delay(milliseconds, cts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_spawnDelayCts, cts))
                {
                    _spawnDelayCts = null;
                }
            }
        }

        public async Task NoiseEmerge()
        {
            if (Global.tvOn) { return; }

            _cursorPositions.Clear();
            _pressedKeys.Clear();

            tvOffSprite.Opacity = 0;
            tvOnSprite.Opacity = 0;
            emergeSprite.Opacity = 1;

            AnimationBehavior.GetAnimator(emergeSprite).Play();

            Point tvSpritePos = PointToScreenDip(tvOffSprite, new Point(0, 0));

            _noiseEmerge.Play();
            _noiseEmergeMusic.Play();

            noiseCursor.Margin = new Thickness(tvSpritePos.X + 120, tvSpritePos.Y + 250, 0, 0);

            Global.RandomPosControl(fireAlarmSprite);

            await Task.Delay(9 * 1000);

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Point targetDip = CursorPositionDip();
            Thickness targetPos = new Thickness(targetDip.X, targetDip.Y, 0, 0);

            for (int i = 1; i < 20; i++)
            {
                long t = currentUnixTime - (10 * (20 - i));

                Thickness pos = new Thickness(
                    Global.Lerp(noiseCursor.Margin.Left, targetPos.Left, i / 20f),
                    Global.Lerp(noiseCursor.Margin.Top, targetPos.Top, i / 20f),
                    0,
                    0);

                _cursorPositions.Add(new CursorSample(t, pos));
            }

            emergeSprite.Opacity = 0;
            Global.tvOn = true;
        }

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new(-1);

            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;

            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private void MakeClickThrough()
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            int extendedStyle = GetWindowLong(_hwnd, -20);
            SetWindowLong(_hwnd, -20, extendedStyle |
                0x00000020 | // WS_EX_TRANSPARENT
                0x00080000); // WS_EX_LAYERED
        }

        private void SetupTickTimer()
        {
            _timer.Tick += TimerTick;
            _timer.Start();
        }

        private void TimerTick(object? sender, EventArgs e)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            DrawingPoint currentCursorPhysical = FormsCursor.Position;
            bool cursorMoved = currentCursorPhysical != _lastCursorPosition;

            Point currentCursorDip = new(currentCursorPhysical.X / _dpiScaleX, currentCursorPhysical.Y / _dpiScaleY);

            HandleDeathCheck(currentCursorDip);
            UpdateTvAndFireAlarm(currentCursorDip);
            HandleInputAndCursor(currentCursorDip, cursorMoved);

            _previousNoisePos = noiseCursor.Margin;
            _lastCursorPosition = currentCursorPhysical;
        }

        private void HandleDeathCheck(Point currentCursorDip)
        {
            if (!Global.tvOn) { return; }

            long distance = (long)(Math.Abs(noiseCursor.Margin.Left - currentCursorDip.X) + Math.Abs(noiseCursor.Margin.Top - currentCursorDip.Y));

            if (distance >= DeathDistance) { return; }

            Global.tvOn = false;

            AnimationBehavior.GetAnimator(jumpscareGif).Play();
            AnimationBehavior.SetRepeatBehavior(jumpscareGif, new System.Windows.Media.Animation.RepeatBehavior(1));

            _noiseDeath1.Play();
            _noiseDeathMusic.Play();

            if (Config.ExecCMDOnDeath)
            {
                string cmd = Config.CMDOnDeath.Split(' ')[0];
                string args = Config.CMDOnDeath[cmd.Length..];
                Process.Start(cmd, args);
            }

            if (Config.CrashOnDeath)
            {
                Process.Start("shutdown", "/s /t 0");
            }
        }

        private void UpdateTvAndFireAlarm(Point currentCursorDip)
        {
            if (Global.tvOn)
            {
                double dx = fireAlarmSprite.Margin.Left - currentCursorDip.X;
                double dy = fireAlarmSprite.Margin.Top - currentCursorDip.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double opacity = Math.Clamp(1.0 - (distance / FireAlarmFadeDistance), 0.0, 1.0);

                fireAlarmSprite.Opacity = opacity;

                if (tvOnSprite.Opacity != 1)
                {
                    tvOnSprite.Opacity = 1;
                    tvOffSprite.Opacity = 0;
                    _tvStatic.PlayLooping();
                }

                noiseCursor.Opacity = 1;
            }
            else
            {
                noiseCursor.Opacity = 0;
                fireAlarmSprite.Opacity = 0;

                if (tvOffSprite.Opacity != 1)
                {
                    tvOnSprite.Opacity = 0;
                    tvOffSprite.Opacity = 1;
                    _tvStatic.Pause();
                }
            }
        }

        private void HandleInputAndCursor(Point currentCursorDip, bool cursorMoved)
        {
            if ((cursorMoved || _keyPressedDebounce > 0) && Global.tvOn)
            {
                if (_keyPressedDebounce > 0) { _keyPressedDebounce--; }
                long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                Thickness nextNoisePos;
                long oldestTime;

                if (_cursorPositions.Count > 0)
                {
                    CursorSample oldest = _cursorPositions[0];
                    oldestTime = oldest.Time;
                    nextNoisePos = oldest.Position;

                    _cursorPositions.Add(new CursorSample(
                        currentUnixTime,
                        new Thickness(currentCursorDip.X, currentCursorDip.Y, 0, 0)));

                    if (currentUnixTime - oldest.Time > CursorHistoryDelayMs)
                    {
                        noiseCursor.Margin = nextNoisePos;
                        _cursorPositions.RemoveAt(0);
                    }
                }
                else
                {
                    oldestTime = currentUnixTime;
                    nextNoisePos = new Thickness(currentCursorDip.X, currentCursorDip.Y, 0, 0);
                    _cursorPositions.Add(new CursorSample(currentUnixTime, nextNoisePos));
                }

                if (_pressedKeys.Count > 0)
                {
                    KeySample oldestKey = _pressedKeys[0];

                    if (currentUnixTime - oldestKey.Time > CursorHistoryDelayMs)
                    {
                        Keyboard.KeyPress(oldestKey.Key);
                        _pressedKeys.RemoveAt(0);
                    }
                }

                long noiseMagnitude = (long)(Math.Abs(_previousNoisePos.Left - nextNoisePos.Left) + Math.Abs(_previousNoisePos.Top - nextNoisePos.Top));

                if (noiseMagnitude < NoiseIdleThreshold)
                {
                    _noiseIdle.PlayLooping();
                    _noiseThreat.Pause();
                    SetCursorAnimation(noiseCursor, "/Assets/Sprites/cursor.gif");
                }
                else if (currentUnixTime - oldestTime > ThreatDelayMs)
                {
                    _noiseIdle.Pause();
                    _noiseThreat.PlayLooping();
                    SetCursorAnimation(noiseCursor, "/Assets/Sprites/ffcursor.gif");
                }
            }
            else
            {
                _noiseIdle.Pause();
                _noiseThreat.Pause();

                if (AnimationBehavior.GetSourceUri(noiseCursor) != null)
                {
                    _noisePause.Play();
                    AnimationBehavior.SetSourceUri(noiseCursor, null);
                    noiseCursor.Source = Global.LoadBitmapImage("pack://application:,,,/Assets/Sprites/pausecursor.png");
                }

                for (int i = 0; i < _cursorPositions.Count; i++) { _cursorPositions[i].Time += IdleHistoryShiftMs; }
                for (int i = 0; i < _pressedKeys.Count; i++) { _pressedKeys[i].Time += IdleHistoryShiftMs; }
            }
        }

        private static void SetCursorAnimation(System.Windows.Controls.Image image, string relativeUri)
        {
            Uri? current = AnimationBehavior.GetSourceUri(image);

            if (current == null || current.OriginalString != relativeUri)
            { AnimationBehavior.SetSourceUri(image, new Uri(relativeUri, UriKind.Relative)); }
        }

        private void fireAlarmSprite_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Global.tvOn = false;

            _fireAlarmPull.Play();
            _fireAlarm.Play();

            AnimationBehavior.GetAnimator(fireAlarmGif).Play();
            AnimationBehavior.SetRepeatBehavior(fireAlarmGif, new System.Windows.Media.Animation.RepeatBehavior(1));
        }
    }
}