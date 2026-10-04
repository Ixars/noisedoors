using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
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

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        private readonly DispatcherTimer _timer;
        private readonly List<CursorSample> _cursorPositions = new();
        private readonly List<KeySample> _pressedKeys = new();

        private DrawingPoint _lastCursorPosition;
        private int _keyPressedDebounce;
        private NotifyIcon? _trayIcon;
        private CancellationTokenSource? _spawnDelayCts;
        private Thickness _previousNoisePos;
        private IntPtr _hwnd;
        private int _fireAlarmLeft;

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
        private readonly SoundHandle _hijackIdle = SoundHandle.Create(Global.GetResourceSteam("Sounds/Hijackidle.wav"));
        private readonly SoundHandle _hijackEmerge = SoundHandle.Create(Global.GetResourceSteam("Sounds/hijackemerge.wav"));
        private readonly SoundHandle _hijackPause = SoundHandle.Create(Global.GetResourceSteam("Sounds/Hijackpause.wav"));
        private readonly SoundHandle _hijackSong = SoundHandle.Create(Global.GetResourceSteam("Sounds/Hijacksong.wav"));
        private readonly SoundHandle _hijackJumpscare = SoundHandle.Create(Global.GetResourceSteam("Sounds/hijackJumpscare.wav"));
        private readonly Keyboard _keyboard = new();

        private Point _dragStartScreen;
        private double _dragStartOffset;
        private bool _dragging;

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
            if (PresentationSource.FromVisual(this)?.CompositionTarget is { } ct)
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

            tvPopup.HorizontalOffset = (ActualWidth - tvOffSprite.Width) / 2;
            tvPopup.VerticalOffset = (ActualHeight - tvOffSprite.Height);

            _keyboard.KeyPressed += OnKeyPressed;
            Closing += OnClosing;
            Closed += OnClosed;

            _keyboard.Hook();

            SetupTickTimer();
            SetupTrayIcon();

            if (!Config.LoadConfig()) new ConfigWindow().Show();
            _ = SpawnLoopAsync();
        }

        private void tvPopup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            UIElement el = (UIElement)sender;

            _dragStartScreen = el.PointToScreen(e.GetPosition(el));
            _dragStartOffset = tvPopup.HorizontalOffset;
            _dragging = true;

            el.CaptureMouse();
            e.Handled = true;
        }

        private void tvPopup_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            e.Handled = true;
        }

        private void tvPopup_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging) return;

            UIElement el = (UIElement)sender;
            Point current = el.PointToScreen(e.GetPosition(el));

            tvPopup.HorizontalOffset = _dragStartOffset + (current.X - _dragStartScreen.X);
        }

        private void OnKeyPressed(FormsKeys key)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _pressedKeys.Add(new KeySample(now, key));
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
                    _spawnDelayCts = null;
            }
        }

        public async Task NoiseEmerge()
        {
            if (Global.tvOn) return;

            Global.changingState = true;

            _cursorPositions.Clear();
            _pressedKeys.Clear();

            tvOffSprite.Opacity = 0;
            tvOnSprite.Opacity = 0;

            bool wasHijackBefore = Global.isHijacked;
            int waitTime;

            if (Global.isHijacked)
            {
                waitTime = 13 * 1000;
                emergeSprite.Opacity = 0;
                hijackEmergeSprite.Opacity = 1;
                AnimationBehavior.GetAnimator(hijackEmergeSprite).Play();

                _hijackEmerge.Play();
            }
            else
            {
                waitTime = 9 * 1000;
                _noiseEmerge.Play();
                _noiseEmergeMusic.Play();

                emergeSprite.Opacity = 1;
                hijackEmergeSprite.Opacity = 0;
                AnimationBehavior.GetAnimator(emergeSprite).Play();
            }

            await Task.Delay(waitTime);

            if (Global.isHijacked && !wasHijackBefore)
            {
                _ = NoiseEmerge();
                return;
            }

            Point tvSpritePos = PointToScreenDip(tvOffSprite, new Point(0, 0));
            noiseCursor.Margin = new Thickness(tvSpritePos.X + 120, tvSpritePos.Y + 250, 0, 0);

            _fireAlarmLeft = Config.FireAlarmAmount;
            Global.RandomPosControl(fireAlarmSprite);

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Point targetDip = CursorPositionDip();
            Thickness targetPos = new(targetDip.X, targetDip.Y, 0, 0);

            for (int i = 1; i < 20; i++)
            {
                long t = currentUnixTime - (10 * (20 - i));

                Thickness pos = new(
                    Global.Lerp(noiseCursor.Margin.Left, targetPos.Left, i / 20f),
                    Global.Lerp(noiseCursor.Margin.Top, targetPos.Top, i / 20f),
                    0, 0);

                _cursorPositions.Add(new CursorSample(t, pos));
            }

            emergeSprite.Opacity = 0;
            hijackEmergeSprite.Opacity = 0;
            Global.tvOn = true;
            Global.changingState = false;
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

            int extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
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

            _ = HandleDeathCheck(currentCursorDip);
            UpdateTvAndFireAlarm(currentCursorDip);
            HandleInputAndCursor(currentCursorDip, cursorMoved);

            _previousNoisePos = noiseCursor.Margin;
            _lastCursorPosition = currentCursorPhysical;
        }

        private async Task HandleDeathCheck(Point currentCursorDip)
        {
            if (!Global.tvOn) return;

            long distance = (long)(Math.Abs(noiseCursor.Margin.Left - currentCursorDip.X) +
                                   Math.Abs(noiseCursor.Margin.Top - currentCursorDip.Y));

            if (distance >= DeathDistance) return;

            Global.tvOn = false;

            if (Global.isHijacked)
            {
                AnimationBehavior.GetAnimator(hijackJumpscareGif).Play();
                _hijackJumpscare.Play();
                await Task.Delay(200);
            }
            else
            {
                AnimationBehavior.GetAnimator(jumpscareGif).Play();
                _noiseDeath1.Play();
                _noiseDeathMusic.Play();
            }

            Global.isHijacked = false;

            await Task.Delay(5200);

            if (Config.ExecCMDOnDeath)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + Config.CMDOnDeath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    ErrorDialog = true
                });
            }

            if (Config.CrashOnDeath)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c shutdown /s /t 0",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    ErrorDialog = true
                });
            }
        }

        private void UpdateTvAndFireAlarm(Point currentCursorDip)
        {
            if (Global.tvOn)
            {
                double dx = fireAlarmSprite.Margin.Left - currentCursorDip.X;
                double dy = fireAlarmSprite.Margin.Top - currentCursorDip.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                fireAlarmSprite.Opacity = Math.Clamp(1.0 - (distance / FireAlarmFadeDistance), 0.0, 1.0);

                Image targetTvSprite = Global.isHijacked ? tvOnHijackSprite : tvOnSprite;
                if (targetTvSprite.Opacity != 1)
                {
                    targetTvSprite.Opacity = 1;
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
                    tvOnHijackSprite.Opacity = 0;
                    tvOffSprite.Opacity = 1;
                    _tvStatic.Pause();
                }
            }
        }

        private void HandleInputAndCursor(Point currentCursorDip, bool cursorMoved)
        {
            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool debounced = true;

            float actualNoiseSpeed = Global.isHijacked ? (float)Config.NoiseSpeed / 2f : (float)Config.NoiseSpeed;
            double speed = Math.Max(0.01, actualNoiseSpeed);

            if (_cursorPositions.Count > 2)
            {
                CursorSample youngest = _cursorPositions[^1];

                double storeDebounce = 10.0;
                if (_cursorPositions.Count > 100.0 / speed)
                    storeDebounce = 200.0 * speed;

                if (currentUnixTime < youngest.Time + storeDebounce)
                    debounced = false;
            }

            if (cursorMoved && Global.tvOn && debounced)
            {
                _cursorPositions.Add(new CursorSample(currentUnixTime, new Thickness(currentCursorDip.X, currentCursorDip.Y, 0, 0)));
            }

            if (Global.isHijacked && Global.tvOn)
            {
                _hijackSong.PlayLooping();
            }
            else
            {
                _hijackSong.Reset();
                _hijackSong.Pause();
            }

            bool active = ((!Global.isHijacked && (cursorMoved || _keyPressedDebounce > 0)) || (Global.isHijacked && !Global.hijackPause)) && Global.tvOn;

            if (!active)
            {
                _noiseIdle.Pause();
                _noiseThreat.Pause();
                _hijackIdle.Pause();

                if (AnimationBehavior.GetSourceUri(noiseCursor) != null)
                {
                    AnimationBehavior.SetSourceUri(noiseCursor, null);

                    if (Global.isHijacked)
                    {
                        _hijackPause.Play();
                        noiseCursor.Source = Global.LoadBitmapImage("pack://application:,,,/Assets/Sprites/pausehijack.png");
                    }
                    else
                    {
                        _noisePause.Play();
                        noiseCursor.Source = Global.LoadBitmapImage("pack://application:,,,/Assets/Sprites/pausecursor.png");
                    }
                }

                for (int i = 0; i < _cursorPositions.Count; i++)
                    _cursorPositions[i].Time += IdleHistoryShiftMs;

                for (int i = 0; i < _pressedKeys.Count; i++)
                    _pressedKeys[i].Time += IdleHistoryShiftMs;

                return;
            }

            if (Global.isHijacked && Global.rng.Next(0, 500) <= 1)
            {
                new Thread(async () =>
                {
                    Global.hijackPause = true;
                    await Task.Delay(Global.rng.Next(100, 1000));
                    Global.hijackPause = false;
                }).Start();
            }

            if (_keyPressedDebounce > 0)
                _keyPressedDebounce--;

            Thickness nextNoisePos;
            long oldestTime;

            if (_cursorPositions.Count > 0)
            {
                CursorSample oldest = _cursorPositions[0];
                oldestTime = oldest.Time;
                nextNoisePos = oldest.Position;

                double usedCursorHistoryDelayMs = CursorHistoryDelayMs;
                if (Global.isHijacked)
                    usedCursorHistoryDelayMs /= 2.0;

                if (_cursorPositions.Count > 400.0 / speed)
                    usedCursorHistoryDelayMs /= 4.0 / speed;

                if (currentUnixTime - oldest.Time > usedCursorHistoryDelayMs / speed)
                {
                    noiseCursor.Margin = nextNoisePos;
                    _cursorPositions.RemoveAt(0);
                }
            }
            else
            {
                oldestTime = currentUnixTime;
                nextNoisePos = new Thickness(currentCursorDip.X, currentCursorDip.Y, 0, 0);
            }

            if (_pressedKeys.Count > 0)
            {
                KeySample oldestKey = _pressedKeys[0];

                if (currentUnixTime - oldestKey.Time > CursorHistoryDelayMs / speed)
                {
                    Keyboard.KeyPress(oldestKey.Key);
                    _pressedKeys.RemoveAt(0);
                }
            }

            long noiseMagnitude = (long)(Math.Abs(_previousNoisePos.Left - nextNoisePos.Left) + Math.Abs(_previousNoisePos.Top - nextNoisePos.Top));
            if (noiseMagnitude < NoiseIdleThreshold)
            {
                if (Global.isHijacked)
                {
                    SetCursorAnimation(noiseCursor, "/Assets/Sprites/hijackcursor.gif");
                    _noiseIdle.Pause();
                    _hijackIdle.PlayLooping();
                    _noiseThreat.Pause();
                }
                else
                {
                    _noiseIdle.PlayLooping();
                    _hijackIdle.Pause();
                    _noiseThreat.Pause();
                    SetCursorAnimation(noiseCursor, "/Assets/Sprites/cursor.gif");
                }
            }
            else if (currentUnixTime - oldestTime > ThreatDelayMs)
            {
                _noiseIdle.Pause();
                _hijackIdle.Pause();
                _noiseThreat.PlayLooping();

                SetCursorAnimation(noiseCursor, Global.isHijacked ? "/Assets/Sprites/ffhijack.gif" : "/Assets/Sprites/ffcursor.gif");
            }
        }

        private static void SetCursorAnimation(System.Windows.Controls.Image image, string relativeUri)
        {
            Uri? current = AnimationBehavior.GetSourceUri(image);

            if (current == null || current.OriginalString != relativeUri)
AnimationBehavior.SetSourceUri(image, new Uri(relativeUri, UriKind.Relative));
        }

        private void fireAlarmSprite_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _fireAlarmPull.Play();
            fireAlarmSprite.Opacity = 0;
            _fireAlarmLeft--;
            Global.RandomPosControl(fireAlarmSprite);

            if (_fireAlarmLeft <= 0)
            {
                Global.tvOn = false;
                Global.isHijacked = false;

                _fireAlarm.Play();
                AnimationBehavior.GetAnimator(fireAlarmGif).Play();
            }
        }

        private void FileDropInTv(object sender, System.Windows.DragEventArgs e)
        {
            string filePath = ((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop))[0];

            if (Path.GetExtension(filePath) != ".exe" || Global.isHijacked) return;

            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);

            byte[] hash = sha256.ComputeHash(stream);
            string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            if (Global.ransomHashes.Contains(hashString))
            {
                Global.tvOn = false;
                Global.isHijacked = true;

                if (!Global.changingState)
                    _ = NoiseEmerge();
            }
        }
    }
}