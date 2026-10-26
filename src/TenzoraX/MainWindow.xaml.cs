using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Path = System.IO.Path;

namespace TenzoraX
{
    public class AppSettings
    {
        public bool AutostartEnabled { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public string LastActiveProfile { get; set; } = "Default";
        public Dictionary<string, double> ButtonPositions { get; set; } = new();
        public Dictionary<string, double> RelativeButtonPositions { get; set; } = new();
        public bool EditMode { get; set; } = false;
        public string EditModeType { get; set; } = "image";
        public double L2_Left { get; set; } = 120;
        public double L2_Top { get; set; } = 0;
        public double R2_Left { get; set; } = 325;
        public double R2_Top { get; set; } = 0;
        public double L1_Left { get; set; } = 115;
        public double L1_Top { get; set; } = 25;
        public double R1_Left { get; set; } = 320;
        public double R1_Top { get; set; } = 25;
        public double SELECT_Left { get; set; } = 195;
        public double SELECT_Top { get; set; } = 120;
        public double START_Left { get; set; } = 265;
        public double START_Top { get; set; } = 120;
        public double L3_Left { get; set; } = 140;
        public double L3_Top { get; set; } = 150;
        public double R3_Left { get; set; } = 290;
        public double R3_Top { get; set; } = 150;
        public double A_Left { get; set; } = 394;
        public double A_Top { get; set; } = 138;
        public double B_Left { get; set; } = 424;
        public double B_Top { get; set; } = 157;
        public double X_Left { get; set; } = 366;
        public double X_Top { get; set; } = 157;
        public double Y_Left { get; set; } = 394;
        public double Y_Top { get; set; } = 99;
        public double DPAD_UP_Left { get; set; } = 104;
        public double DPAD_UP_Top { get; set; } = 0;
        public double DPAD_DOWN_Left { get; set; } = 104;
        public double DPAD_DOWN_Top { get; set; } = 38;
        public double DPAD_LEFT_Left { get; set; } = 0;
        public double DPAD_LEFT_Top { get; set; } = 19;
        public double DPAD_RIGHT_Left { get; set; } = 38;
        public double DPAD_RIGHT_Top { get; set; } = 19;
        public string ControllerImagePath { get; set; } = "";
        public double ControllerScale { get; set; } = 1.0;
        public double ControllerLeft { get; set; } = 50;
        public double ControllerTop { get; set; } = 20;
        public bool CaptureEnabled { get; set; } = true;
        public string Language { get; set; } = "";
        /// <summary>Auto-Wiederauswahl des Controllers: "first" (erster verbundener) oder "last" (zuletzt verwendeter, StableId).</summary>
        public string ControllerReselectMode { get; set; } = "first";

        /// <summary>Oberflächen-Stil: "flat" (flach), "3d" (Licht, Kanten, Tiefe) oder "shine" (3D mit Glanzkante).</summary>
        public string UiTheme { get; set; } = Theme3D.ThreeD;

        /// <summary>Ob der Controller-Bereich eingeklappt ist, damit die Tastatur den Platz bekommt.</summary>
        public bool GamepadCollapsed { get; set; } = false;

        /// <summary>Ob der animierte GhostFibers-Hintergrund läuft (GPU-Pixel-Shader).</summary>
        public bool AnimatedBackgroundEnabled { get; set; } = true;
        /// <summary>
        /// Erzwingt eine Stufe des Hintergrunds: 0 = automatisch (nach gemessenem Anzeigetakt),
        /// 1 = minimal (2 Faserschichten, ohne Grain), 2 = sparsam (3 Schichten),
        /// 3 = voll (4 Schichten, wie im Original).
        /// </summary>
        public int BackgroundQualityOverride { get; set; } = 0;
        public double WindowWidth { get; set; } = 1180;
        public double WindowHeight { get; set; } = 720;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public bool WindowMaximized { get; set; } = false;
        public string OutputMode { get; set; } = "VK";
        /// <summary>Ob der aktive Controller zusätzlich als virtueller Xbox-360-Controller (ViGEm) gespiegelt wird. Wird nie persistiert –的标准 immer false beim Start.</summary>
        public bool UseVigemXbox { get; set; } = false;
        public bool SoundEnabled { get; set; } = true;
        public double SoundVolume { get; set; } = 0.5;
        public bool RunAsAdministrator { get; set; } = false;
        public bool HasCustomPosition { get; set; } = false;
        public bool HotkeyNotificationsEnabled { get; set; } = true;
        public double NotificationDuration { get; set; } = 5.0;
        public double NotificationWidth { get; set; } = 360;
        public double NotificationPositionX { get; set; } = -1;
        public double NotificationPositionY { get; set; } = -1;
        // Ecke für die Benachrichtigung: TopRight | TopLeft | BottomRight | BottomLeft
        public string NotificationCorner { get; set; } = "TopRight";
    }

    public partial class MainWindow : Window
    {
        private const int WM_INPUT = 0x00FF;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                ControllerManager.Instance.HandleWmInput(lParam);
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private NotifyIcon? _notifyIcon;
        /// <summary>Gecachtes Tray-Symbol (Controller-Icon). Wiederverwendet, damit
        /// UI-Updates kein neues GDI-Handle erzeugen; wird beim Beenden freigegeben.</summary>
        private System.Drawing.Icon? _controllerIcon;
        private bool _isExplicitClose = false;

        // Ein Klick auf „Gamecontroller öffnen“ darf nie zwei joy.cpl-Aufrufe gleichzeitig starten.
        private static int _gameControllersOpening;

        private AppSettings _settings = new();
        private LanguageManager Lang => LanguageManager.Instance;
        private string ConfigFilePath => Path.Combine(GetDocumentsPath(), ConfigFileName);
        private const string ConfigFileName = "config.json";

        private readonly InputCaptureEngine _capture = new();
        private readonly Dictionary<string, System.Windows.Controls.Button> _gamepadButtonsUi = new();
        private readonly HashSet<string> _physicallyHeldButtons = new();
        private readonly List<DetectedController> _comboControllers = new();
        private volatile bool _stateUiUpdatePending;

        // UI-Update-Diffing: Der Poll-Thread feuert StateChanged mit ~250 Hz – auch
        // wenn sich nichts ändert. Ohne Diffing hätte jeder UI-Update ~20 neue Brushes
        // und Canvas-Positionen erzeugt und den UI-Thread gesättigt (→ sichtbarer Lag).
        private readonly HashSet<string> _lastRenderedButtons = new();
        private double _lastRenderedLx, _lastRenderedLy, _lastRenderedRx, _lastRenderedRy;
        private readonly System.Diagnostics.Stopwatch _uiGate = System.Diagnostics.Stopwatch.StartNew();
        private const long MinUiUpdateIntervalMs = 8;   // max. ~120 UI-Updates/s

        // Drag-Drop support for button positioning
        private bool _isDragging = false;
        private System.Windows.Controls.Button? _draggedButton = null;
        private System.Windows.Point _dragStartPoint = new();
        private double _originalLeft = 0, _originalTop = 0;
        private FrameworkElement? _draggedElement = null;

        // Button selection (Edit Mode - Buttons)
        private System.Windows.Controls.Button? _selectedButton = null;
        private string? _selectedButtonKey = null;

        // Drag-Drop and Scaling support for controller background image
        private bool _isDraggingImg = false;
        private System.Windows.Point _dragStartPointImg = new();
        private double _originalLeftImg = 0, _originalTopImg = 0;

        private bool _settingsLoaded;

        // Animierter Hintergrund (GhostFibers): Zeit wird nur weitergezählt, solange das
        // Fenster sichtbar ist – im Tray oder minimiert kostet er nichts.
        private Effects.GhostFibersEffect? _backgroundEffect;
        private bool _backgroundLoopHooked = false;
        private DateTime _backgroundNextFrameAt = DateTime.MinValue;
        private DateTime _backgroundLastFrameAt = DateTime.MinValue;
        private double _backgroundElapsedSeconds = 0;
        private readonly Effects.BackgroundPerformancePolicy _backgroundPolicy = new();
        private Effects.BackgroundQuality _backgroundQualityLogged = Effects.BackgroundQuality.Full;
        private DateTime _backgroundActivityLoggedAt = DateTime.MinValue;
        private int _backgroundDrawnSinceLog = 0;
        private bool _backgroundResolutionReady = false;
        private System.Windows.Point _backgroundEffectResolution = new(1, 1);

        public MainWindow()
        {
            // Design VOR dem Fenster-XAML anwenden: sonst zeichnet der erste Frame
            // die flachen Standardflächen aus App.xaml und die App „springt“ sichtbar
            // in den gewählten Stil. Ohne gespeicherten Wert ist es 3D.
            ApplyStartupTheme();

            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    // Ignore CRT shutdown noise from single-file publish (same filter as App.xaml.cs)
                    if (ex is DllNotFoundException &&
                        ex.StackTrace != null &&
                        (ex.StackTrace.Contains("__std_type_info_destroy_list") ||
                         ex.StackTrace.Contains("__scrt_uninitialize_type_info") ||
                         ex.StackTrace.Contains("_app_exit_callback")))
                        return;

                    LogCrash("Unhandled", ex);
                }
            };
            Dispatcher.UnhandledException += (s, e) =>
            {
                LogCrash("Dispatcher", e.Exception);
                e.Handled = true;
            };
        }

        /// <summary>
        /// Liest nur den Design-Stil aus der config.json (vor InitializeComponent).
        /// Fehlt die Datei oder der Eintrag, gilt 3D.
        /// </summary>
        private void ApplyStartupTheme()
        {
            string? json = null;
            try
            {
                if (File.Exists(ConfigFilePath))
                    json = File.ReadAllText(ConfigFilePath);
            }
            catch
            {
                // Nicht lesbar → Standard (3D), LoadSettings versucht es später erneut.
            }
            Theme3D.Apply(Theme3D.ThemeFromConfigJson(json));
        }

        private string GetDocumentsPath()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TenzoraX");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemeHelper.ApplyImmersiveDarkMode(this);
                IntPtr hwnd = new WindowInteropHelper(this).Handle;

                // RawInput für HID-Gamepads (WM_INPUT): funktioniert auch, wenn andere
                // Prozesse (z.B. Steam) das Gerät offen halten – gleicher Weg wie joy.cpl.
                ControllerManager.Instance.RegisterRawInput(hwnd);
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

                Icon = CreateControllerIconSource();
                InitializeGamepadButtonMap();
                LoadSettings();

                App.LogApp("Konfiguration geladen");

                // Autostart auf Task-Scheduler (höchste Rechte) migrieren, damit die
                // App beim Anmelden OHNE UAC-Prompt erhöht startet – kein Aufblitzen.
                if (_settings.AutostartEnabled)
                    SettingsWindow.MigrateAutostartToTaskScheduler();

                // Restore window size/position
                if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
                {
                    Width = _settings.WindowWidth;
                    Height = _settings.WindowHeight;
                }
                bool positionValid = false;
                if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
                {
                    var rect = new System.Drawing.Rectangle(
                        (int)_settings.WindowLeft, (int)_settings.WindowTop,
                        (int)_settings.WindowWidth, (int)_settings.WindowHeight);
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        if (screen.WorkingArea.IntersectsWith(rect))
                        {
                            Left = _settings.WindowLeft;
                            Top = _settings.WindowTop;
                            positionValid = true;
                            break;
                        }
                    }
                }
                if (!positionValid)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                // Restore maximized state (must be set after size/position)
                if (_settings.WindowMaximized)
                    WindowState = WindowState.Maximized;

                // Auto-elevate: only if setting is on AND we're not already admin.
                // Beim Autostart (--minimized) NICHT erhöhen: Der Task-Scheduler startet
                // die App bereits erhöht; ein UAC-Prompt beim Boot wäre genau das
                // sichtbare Aufblitzen, das der Nutzer nicht will. Nur bei manuellem
                // Start (ohne --minimized) einmalig erhöhen.
                var cmdLineArgs = Environment.GetCommandLineArgs();
                bool isBootStart = cmdLineArgs.Contains("--minimized");
                if (_settings.RunAsAdministrator && !InputSimulator.IsRunningAsAdmin() && !isBootStart)
                {
                    try
                    {
                        var cmdArgs = Environment.GetCommandLineArgs();
                        var restArgs = cmdArgs.Skip(1).ToList();
                        if (!restArgs.Contains("--admin-restart")) restArgs.Add("--admin-restart");
                        var argStr = string.Join(" ", restArgs
                            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        var psi = new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath,
                            UseShellExecute = true,
                            Verb = "runas",
                            Arguments = argStr
                        };
                        Process.Start(psi);
                        App.ReleaseInstanceMutex();
                        Environment.Exit(0);
                        return;
                    }
                    catch
                    {
                        _settings.RunAsAdministrator = false;
                        SaveSettings();
                    }
                }

                TxtVersionHeader.Text = $"v{AppVersion.Current}";
                Title = $"TenzoraX v{AppVersion.Current}";

                // Cleanup: remove any leftover .old.exe from old update versions
                string oldExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TenzoraX.old.exe");
                if (File.Exists(oldExe)) try { File.Delete(oldExe); } catch { }
                string oldExe2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TenzoraX.exe.bak");
                if (File.Exists(oldExe2)) try { File.Delete(oldExe2); } catch { }

                InitLanguage();
            DataContext = LanguageManager.Instance;
            LoadButtonPositions();
            UpdateButtonsAppearance();
            LoadControllerBackground();
            InitializeTrayIcon();
            InitializeDragHandlers();

            // Blockade der Tastatur-Ausgabe (UIPI: aktives Fenster mit Admin-Rechten)
            // sichtbar machen statt still zu scheitern. BeginInvoke: nicht vom
            // Poll-Thread aus blockieren. Meldung ist bereits lokalisiert.
            InputSimulator.InjectionBlocked += message =>
            {
                try
                {
                    Dispatcher.BeginInvoke(() => NotificationManager.Show("", message));
                }
                catch { }
            };

            ControllerManager.Instance.GamepadsChanged += OnGamepadsChanged;
            ControllerManager.Instance.StateChanged += OnControllerStateChanged;
            ControllerManager.Instance.Initialize();
            // ViGEm-Xbox-Spiegelung gemäß gespeicherter Einstellung wiederherstellen.
            if (_settings.UseVigemXbox)
                ControllerManager.Instance.SetVigemXbox(_settings.UseVigemXbox);
            RefreshGamepadsList();
            InitVigemHidhideUi();
            ControllerManager.Instance.ReselectMode = _settings.ControllerReselectMode;
            StartHidHideRestoreTimer();
            StartLiveInputTimer();
            App.LogApp("Controller-System initialisiert");

            // Hinweis erst nach dem ersten Rendering zeigen (nicht beim Start aufblitzen lassen).
            Dispatcher.BeginInvoke(() => CheckAdminStartupHint());

            RefreshProfilesList();
            SelectProfile(_settings.LastActiveProfile);

            ChkEditMode.IsChecked = _settings.EditMode;
            if (_settings.EditMode)
            {
                if (_settings.EditModeType == "buttons")
                    RadioEditButtons.IsChecked = true;
                else
                    RadioEditImage.IsChecked = true;
            }

            // Output mode – die Einträge legt bereits ApplyLanguage (über InitLanguage oben) an.
            // Nur falls das ausblieb, hier nachfüllen; sonst stünde jeder Modus doppelt in der Liste.
            if (ComboOutputMode.Items.Count == 0)
            {
                ComboOutputMode.Items.Add(Lang.OutputMode_VK);
                ComboOutputMode.Items.Add(Lang.OutputMode_ScanCode);
            }
            ComboOutputMode.SelectedIndex = _settings.OutputMode == "ScanCode" ? 1 : 0;
            ApplyOutputMode();

            // Initialize manager state from settings (UI controls are in SettingsWindow)
            SoundManager.Enabled = _settings.SoundEnabled;
            SoundManager.Volume = _settings.SoundVolume;
            NotificationManager.Enabled = _settings.HotkeyNotificationsEnabled;
            NotificationManager.Duration = _settings.NotificationDuration;
            NotificationManager.NotificationWidth = _settings.NotificationWidth;
            NotificationManager.SavedPosX = _settings.NotificationPositionX;
            NotificationManager.SavedPosY = _settings.NotificationPositionY;
            if (_settings.NotificationCorner is NotificationManager.CornerTopLeft
                or NotificationManager.CornerTopRight
                or NotificationManager.CornerBottomLeft
                or NotificationManager.CornerBottomRight)
                NotificationManager.Corner = _settings.NotificationCorner;

            // Final position reapply & centering after layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterController();
                ReapplyButtonPositions();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Re-center when window is resized (only if user hasn't manually positioned)
            CanvasGamepad.SizeChanged += CanvasGamepad_SizeChanged;

            // Auto-save window size/position during resize/move (crash-safe persistence)
            SizeChanged += (s, e) => { if (_settingsLoaded) SaveWindowPosition(); };
            StateChanged += (s, e) => { if (_settingsLoaded) { _settings.WindowMaximized = WindowState == WindowState.Maximized; SaveSettings(); } };

            // Animierter Hintergrund: Größe mitführen und im Tray/minimiert anhalten.
            ApplyBackgroundSetting();
            SizeChanged += (s, e) => UpdateBackgroundResolution();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && WindowState != WindowState.Minimized) StartBackgroundAnimation();
                else StopBackgroundAnimation();
            };
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized) StopBackgroundAnimation();
                else if (IsVisible) StartBackgroundAnimation();
            };

            // Check for updates after everything is ready
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await CheckForUpdateAsync();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var args = Environment.GetCommandLineArgs();
            if (_settings.StartMinimized || args.Contains("--minimized"))
            {
                HideToTray(false);
            }

            _settingsLoaded = true;
            SaveSettings();

            // Einklapp-Zustand des Controller-Felds aus der letzten Sitzung anwenden.
            SetGamepadCollapsed(_settings.GamepadCollapsed, persist: false);

            // Notify user if previous session crashed
            if (App.HasCrashLog)
            {
                try { ShowCrashDialog(); App.DeleteCrashLog(); } catch { }
            }
            }
            catch (Exception ex)
            {
                try { LogCrash("Startup", ex); } catch { }
                System.Windows.MessageBox.Show(
                    $"TenzoraX konnte nicht gestartet werden.\n\n{ex.GetType().Name}: {ex.Message}\n\nDetails: crash.log",
                    "Startfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadControllerBackground()
        {
            if (!string.IsNullOrEmpty(_settings.ControllerImagePath) && File.Exists(_settings.ControllerImagePath))
            {
                try { ImgController.Source = new BitmapImage(new Uri(_settings.ControllerImagePath)); } catch { }
            }
            if (ImgController.Source == null)
            {
                try { ImgController.Source = new BitmapImage(new Uri("pack://application:,,,/assets/icons/xbox-360.png")); } catch { }
            }

            double scale = _settings.ControllerScale;
            ImgController.Width = 400 * scale;
            ImgController.Height = 230 * scale;

            Canvas.SetLeft(ImgController, _settings.ControllerLeft);
            Canvas.SetTop(ImgController, _settings.ControllerTop);

            ReapplyButtonPositions();
        }

        private void InitializeDragHandlers()
        {
            foreach (var pair in _gamepadButtonsUi)
            {
                var btn = pair.Value;
                btn.PreviewMouseLeftButtonDown += Button_MouseLeftButtonDown;
                btn.PreviewMouseMove += Button_MouseMove;
                btn.PreviewMouseLeftButtonUp += Button_MouseLeftButtonUp;
            }

            // L3/R3 parent canvases: clicking the ring area also selects/drags, and move/up during capture
            Canvas_L3.PreviewMouseLeftButtonDown += StickCanvas_MouseLeftButtonDown;
            Canvas_L3.PreviewMouseMove += Button_MouseMove;
            Canvas_L3.PreviewMouseLeftButtonUp += Button_MouseLeftButtonUp;
            Canvas_R3.PreviewMouseLeftButtonDown += StickCanvas_MouseLeftButtonDown;
            Canvas_R3.PreviewMouseMove += Button_MouseMove;
            Canvas_R3.PreviewMouseLeftButtonUp += Button_MouseLeftButtonUp;

            ImgController.PreviewMouseLeftButtonDown += ImgController_MouseLeftButtonDown;
            ImgController.PreviewMouseMove += ImgController_MouseMove;
            ImgController.PreviewMouseLeftButtonUp += ImgController_MouseLeftButtonUp;
            ImgController.PreviewMouseWheel += ImgController_MouseWheel;
        }

        private void StickCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.EditMode || _settings.EditModeType != "buttons")
                return;

            System.Windows.Controls.Button btn = (sender == Canvas_L3) ? Btn_L3 : Btn_R3;
            SelectButton(btn);

            _isDragging = true;
            _draggedButton = btn;
            _draggedElement = (FrameworkElement)sender;

            _dragStartPoint = e.GetPosition(CanvasGamepad);
            _originalLeft = Canvas.GetLeft(_draggedElement);
            _originalTop = Canvas.GetTop(_draggedElement);

            _draggedElement.CaptureMouse();
            e.Handled = true;
        }

        private void Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.EditMode || _settings.EditModeType != "buttons" || sender is not System.Windows.Controls.Button btn)
                return;

            SelectButton(btn);

            _isDragging = true;
            _draggedButton = btn;

            if (btn == Btn_L3)
                _draggedElement = Canvas_L3;
            else if (btn == Btn_R3)
                _draggedElement = Canvas_R3;
            else
                _draggedElement = btn;

            _dragStartPoint = e.GetPosition(CanvasGamepad);
            _originalLeft = Canvas.GetLeft(_draggedElement);
            _originalTop = Canvas.GetTop(_draggedElement);

            _draggedElement.CaptureMouse();
            e.Handled = true;
        }

        private void Button_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && _draggedElement != null && _draggedButton != null)
            {
                System.Windows.Point currentPosition = e.GetPosition(CanvasGamepad);
                double newLeft = _originalLeft + (currentPosition.X - _dragStartPoint.X);
                double newTop = _originalTop + (currentPosition.Y - _dragStartPoint.Y);

                double elW = _draggedElement.ActualWidth;
                double elH = _draggedElement.ActualHeight;
                double maxL = CanvasGamepad.ActualWidth - elW;
                double maxT = CanvasGamepad.ActualHeight - elH;
                if (maxL >= 0) newLeft = Math.Clamp(newLeft, 0, maxL);
                if (maxT >= 0) newTop = Math.Clamp(newTop, 0, maxT);

                Canvas.SetLeft(_draggedElement, newLeft);
                Canvas.SetTop(_draggedElement, newTop);
                e.Handled = true;
            }
        }

        private void Button_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && _draggedElement != null && _draggedButton != null)
            {
                _isDragging = false;
                _draggedElement.ReleaseMouseCapture();

                string key = _draggedButton.Name.Replace("Btn_", "");
                double absLeft = Canvas.GetLeft(_draggedElement);
                double absTop = Canvas.GetTop(_draggedElement);

                // Convert to relative position (percentage of image bounds)
                double imgLeft = Canvas.GetLeft(ImgController);
                double imgTop = Canvas.GetTop(ImgController);
                double imgW = ImgController.ActualWidth;
                double imgH = ImgController.ActualHeight;
                double relX = (absLeft - imgLeft) / imgW;
                double relY = (absTop - imgTop) / imgH;

                // Save relative position
                _settings.RelativeButtonPositions[$"{key}_X"] = relX;
                _settings.RelativeButtonPositions[$"{key}_Y"] = relY;
                SaveSettings();

                _draggedButton = null;
                _draggedElement = null;
                e.Handled = true;
            }
        }

        private void ReapplyButtonPositions()
        {
            double imgLeft = Canvas.GetLeft(ImgController);
            double imgTop = Canvas.GetTop(ImgController);
            if (double.IsNaN(imgLeft)) imgLeft = 50;
            if (double.IsNaN(imgTop)) imgTop = 20;
            double imgW = ImgController.ActualWidth;
            double imgH = ImgController.ActualHeight;
            if (imgW <= 0 || imgH <= 0)
            {
                imgW = ImgController.Width;
                imgH = ImgController.Height;
            }
            if (double.IsNaN(imgW) || double.IsNaN(imgH) || imgW <= 0 || imgH <= 0)
            {
                imgW = 400 * _settings.ControllerScale;
                imgH = 230 * _settings.ControllerScale;
            }
            if (imgW <= 0 || imgH <= 0) return;

            foreach (var pair in _gamepadButtonsUi)
            {
                string key = pair.Key;
                var btn = pair.Value;
                FrameworkElement el = btn;
                if (btn == Btn_L3) el = Canvas_L3;
                else if (btn == Btn_R3) el = Canvas_R3;

                if (_settings.RelativeButtonPositions.TryGetValue($"{key}_X", out double rx) &&
                    _settings.RelativeButtonPositions.TryGetValue($"{key}_Y", out double ry))
                {
                    Canvas.SetLeft(el, imgLeft + rx * imgW);
                    Canvas.SetTop(el, imgTop + ry * imgH);
                }
            }
        }

        private void CenterController()
        {
            double cw = CanvasGamepad.ActualWidth;
            double ch = CanvasGamepad.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            double imgW = ImgController.ActualWidth;
            double imgH = ImgController.ActualHeight;
            if (imgW <= 0 || imgH <= 0)
            {
                imgW = ImgController.Width;
                imgH = ImgController.Height;
            }
            if (imgW <= 0 || imgH <= 0) return;

            _settings.ControllerLeft = Math.Max(0, (cw - imgW) / 2);
            _settings.ControllerTop = Math.Max(0, (ch - imgH) / 2);
            Canvas.SetLeft(ImgController, _settings.ControllerLeft);
            Canvas.SetTop(ImgController, _settings.ControllerTop);
        }

        private void CanvasGamepad_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CenterController();
            ReapplyButtonPositions();
        }

        private void ImgController_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_settings.EditMode && _settings.EditModeType == "image")
            {
                _isDraggingImg = true;
                _dragStartPointImg = e.GetPosition(CanvasGamepad);
                
                _originalLeftImg = Canvas.GetLeft(ImgController);
                if (double.IsNaN(_originalLeftImg)) _originalLeftImg = 50;
                
                _originalTopImg = Canvas.GetTop(ImgController);
                if (double.IsNaN(_originalTopImg)) _originalTopImg = 20;
                
                ImgController.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ImgController_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingImg && _settings.EditMode)
            {
                System.Windows.Point currentPosition = e.GetPosition(CanvasGamepad);
                
                double deltaX = currentPosition.X - _dragStartPointImg.X;
                double deltaY = currentPosition.Y - _dragStartPointImg.Y;
                
                double newLeft = _originalLeftImg + deltaX;
                double newTop  = _originalTopImg  + deltaY;

                // Keep at least 40px of the image visible inside the canvas
                double minVisible = 40;
                double maxLeft = CanvasGamepad.ActualWidth  - minVisible;
                double maxTop  = CanvasGamepad.ActualHeight - minVisible;
                double minLeft = minVisible - ImgController.Width;
                double minTop  = minVisible - ImgController.Height;

                newLeft = Math.Clamp(newLeft, minLeft, maxLeft);
                newTop  = Math.Clamp(newTop,  minTop,  maxTop);

                Canvas.SetLeft(ImgController, newLeft);
                Canvas.SetTop(ImgController, newTop);
                
                e.Handled = true;
            }
        }

        private void ImgController_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingImg)
            {
                _isDraggingImg = false;
                ImgController.ReleaseMouseCapture();
                
                _settings.ControllerLeft = Canvas.GetLeft(ImgController);
                _settings.ControllerTop = Canvas.GetTop(ImgController);
                _settings.HasCustomPosition = true;
                SaveSettings();
                ReapplyButtonPositions();
                e.Handled = true;
            }
        }

        private void ImgController_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_settings.EditMode && _settings.EditModeType == "image")
            {
                double delta = e.Delta > 0 ? 0.05 : -0.05;
                double newScale = _settings.ControllerScale + delta;
                
                if (newScale < 0.05) newScale = 0.05;
                // no upper limit for free scaling
                
                _settings.ControllerScale = newScale;
                
                ImgController.Width  = 400 * _settings.ControllerScale;
                ImgController.Height = 230 * _settings.ControllerScale;

                // After resize, clamp position so at least 40px stays visible
                double minVisible = 40;
                double curLeft = Canvas.GetLeft(ImgController);
                double curTop  = Canvas.GetTop(ImgController);
                if (double.IsNaN(curLeft)) curLeft = _settings.ControllerLeft;
                if (double.IsNaN(curTop))  curTop  = _settings.ControllerTop;

                double maxLeft = CanvasGamepad.ActualWidth  - minVisible;
                double maxTop  = CanvasGamepad.ActualHeight - minVisible;
                double minLeft = minVisible - ImgController.Width;
                double minTop  = minVisible - ImgController.Height;

                double clampedLeft = Math.Clamp(curLeft, minLeft, maxLeft);
                double clampedTop  = Math.Clamp(curTop,  minTop,  maxTop);

                Canvas.SetLeft(ImgController, clampedLeft);
                Canvas.SetTop(ImgController, clampedTop);

                _settings.ControllerLeft = clampedLeft;
                _settings.ControllerTop  = clampedTop;

                ReapplyButtonPositions();

                SaveSettings();
                e.Handled = true;
            }
        }

        private void LoadButtonPositions()
        {
            // Migration: convert old absolute positions to relative
            if (_settings.RelativeButtonPositions.Count == 0 && _settings.ButtonPositions.Count > 0)
            {
                double imgLeft = Canvas.GetLeft(ImgController);
                double imgTop = Canvas.GetTop(ImgController);
                if (double.IsNaN(imgLeft)) imgLeft = 50;
                if (double.IsNaN(imgTop)) imgTop = 20;
                double imgW = ImgController.ActualWidth;
                double imgH = ImgController.ActualHeight;
                if (imgW <= 0 || imgH <= 0) { imgW = 400; imgH = 230; }

                foreach (var pair in _gamepadButtonsUi)
                {
                    string key = pair.Key;
                    if (_settings.ButtonPositions.TryGetValue($"{key}_Left", out double al) &&
                        _settings.ButtonPositions.TryGetValue($"{key}_Top", out double at))
                    {
                        _settings.RelativeButtonPositions[$"{key}_X"] = (al - imgLeft) / imgW;
                        _settings.RelativeButtonPositions[$"{key}_Y"] = (at - imgTop) / imgH;
                    }
                }
                _settings.ButtonPositions.Clear();
                SaveSettings();
            }

            // Apply relative positions
            double imgLeftCur = Canvas.GetLeft(ImgController);
            double imgTopCur = Canvas.GetTop(ImgController);
            if (double.IsNaN(imgLeftCur)) imgLeftCur = 50;
            if (double.IsNaN(imgTopCur)) imgTopCur = 20;
            double imgWCur = ImgController.ActualWidth;
            double imgHCur = ImgController.ActualHeight;
            if (imgWCur <= 0 || imgHCur <= 0) { imgWCur = 400; imgHCur = 230; }

            foreach (var pair in _gamepadButtonsUi)
            {
                string key = pair.Key;
                var btn = pair.Value;
                FrameworkElement el = btn;
                if (btn == Btn_L3) el = Canvas_L3;
                else if (btn == Btn_R3) el = Canvas_R3;

                if (_settings.RelativeButtonPositions.TryGetValue($"{key}_X", out double rx) &&
                    _settings.RelativeButtonPositions.TryGetValue($"{key}_Y", out double ry))
                {
                    Canvas.SetLeft(el, imgLeftCur + rx * imgWCur);
                    Canvas.SetTop(el, imgTopCur + ry * imgHCur);
                }
            }
        }

        private void UpdateButtonsAppearance()
        {
            bool edit = _settings.EditMode;
            bool btnMode = edit && _settings.EditModeType == "buttons";
            bool imgMode = edit && _settings.EditModeType == "image";
            var white = System.Windows.Media.Brushes.White;
            var muted = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 45, 45, 61));

            foreach (var pair in _gamepadButtonsUi)
            {
                var btn = pair.Value;
                string key = pair.Key;

                if (btnMode)
                {
                    btn.Opacity = 1.0;
                    btn.IsHitTestVisible = true;
                    btn.Foreground = white;
                    btn.FontWeight = FontWeights.ExtraBold;
                    if (_selectedButton == btn)
                    {
                        btn.BorderThickness = new Thickness(3);
                        btn.BorderBrush = System.Windows.Media.Brushes.Cyan;
                        btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 120, 200));
                    }
                    else
                    {
                        btn.BorderThickness = new Thickness(2);
                        btn.BorderBrush = System.Windows.Media.Brushes.Lime;
                        btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 16, 44, 30));
                    }
                }
                else
                {
                    btn.BorderThickness = new Thickness(1);
                    btn.BorderBrush = muted;
                    btn.FontWeight = FontWeights.Bold;

                    if (imgMode)
                    {
                        btn.Opacity = 0.6;
                        btn.IsHitTestVisible = false;
                        btn.Foreground = white;
                        byte a = 70;
                        if (key == "A") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 20, 44, 30));
                        else if (key == "B") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 47, 20, 24));
                        else if (key == "X") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 18, 32, 48));
                        else if (key == "Y") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 42, 36, 21));
                        else btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 26, 26, 36));
                    }
                    else
                    {
                        btn.Opacity = 0.85;
                        btn.IsHitTestVisible = true;
                        btn.Foreground = white;
                        byte a = 100;
                        if (key == "A") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 20, 44, 30));
                        else if (key == "B") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 47, 20, 24));
                        else if (key == "X") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 18, 32, 48));
                        else if (key == "Y") btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 42, 36, 21));
                        else btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, 26, 26, 36));
                    }
                }
            }
        }

        private void InitializeGamepadButtonMap()
        {
            _gamepadButtonsUi["A"] = Btn_A;
            _gamepadButtonsUi["B"] = Btn_B;
            _gamepadButtonsUi["X"] = Btn_X;
            _gamepadButtonsUi["Y"] = Btn_Y;
            _gamepadButtonsUi["L1"] = Btn_L1;
            _gamepadButtonsUi["R1"] = Btn_R1;
            _gamepadButtonsUi["L2"] = Btn_L2;
            _gamepadButtonsUi["R2"] = Btn_R2;
            // L3/R3 buttons are inside borders - we need to add them too
            _gamepadButtonsUi["L3"] = Btn_L3;
            _gamepadButtonsUi["R3"] = Btn_R3;
            _gamepadButtonsUi["SELECT"] = Btn_SELECT;
            _gamepadButtonsUi["START"] = Btn_START;
            _gamepadButtonsUi["DPAD_UP"] = Btn_DPAD_UP;
            _gamepadButtonsUi["DPAD_DOWN"] = Btn_DPAD_DOWN;
            _gamepadButtonsUi["DPAD_LEFT"] = Btn_DPAD_LEFT;
            _gamepadButtonsUi["DPAD_RIGHT"] = Btn_DPAD_RIGHT;
        }

        #region Gamepad Polling

        private void OnGamepadsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(RefreshGamepadsList);
        }

        private void RefreshGamepadsList()
        {
            ComboGamepads.Items.Clear();
            _comboControllers.Clear();
            _comboControllers.AddRange(ControllerManager.Instance.ConnectedControllers);
            if (_comboControllers.Count == 0)
            {
                ComboGamepads.Items.Add(Lang.Combo_NoController);
                ComboGamepads.SelectedIndex = 0;
                SetStatusText(Lang.Status_Disconnected, System.Windows.Media.Brushes.Gray);
            }
            else
            {
                foreach (var c in _comboControllers)
                {
                    // Deaktivierte Windows-Schnittstellen nennt der Eintrag beim Namen
                    // („– deaktiviert (Windows)“) statt sie als normalen Controller zu zeigen.
                    ComboGamepads.Items.Add(ControllerManager.FormatControllerListEntry(c));
                }
                int idx = ControllerManager.Instance.ActiveGamepadIndex;
                if (idx >= 0 && idx < _comboControllers.Count)
                    ComboGamepads.SelectedIndex = idx;
                UpdateStatusFromSelection();
            }

            UpdateInputMismatchBanner();
        }

        /// <summary>
        /// Zeigt den Hinweis, wenn die App ein Pad listet, Windows aber weder XInput noch
        /// DirectInput ein Gerät führt – dann ist typischerweise die Schnittstelle in Windows
        /// deaktiviert, und das Pad fehlt im Panel „Game Controller“ und bei Steam.
        /// </summary>
        private void UpdateInputMismatchBanner()
        {
            string warning = ControllerManager.Instance.InputVisibilityWarning;
            bool show = !string.IsNullOrWhiteSpace(warning) && _comboControllers.Count > 0;
            TxtInputMismatch.Text = show ? warning : "";
            BannerInputMismatch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            // Der Knopf erscheint nur, wenn die Schnittstelle bekannt ist – sonst gibt es
            // nichts, was die App gezielt einschalten könnte.
            bool canRepair = show && !string.IsNullOrEmpty(ControllerManager.Instance.DisabledInterfaceId);
            BtnEnableDisabledInterface.Visibility = canRepair ? Visibility.Visible : Visibility.Collapsed;
            BtnEnableDisabledInterface.IsEnabled = canRepair;
        }

        /// <summary>
        /// Schaltet die deaktivierte Schnittstelle direkt wieder ein (einmalige UAC-Abfrage,
        /// falls die App nicht erhöht läuft) und meldet das Ergebnis im Banner.
        /// </summary>
        private async void BtnEnableDisabledInterface_Click(object sender, RoutedEventArgs e)
        {
            string instanceId = ControllerManager.Instance.DisabledInterfaceId;
            if (string.IsNullOrEmpty(instanceId)) return;

            BtnEnableDisabledInterface.IsEnabled = false;
            BtnEnableDisabledInterface.Content = "Aktiviere …";
            TxtInputMismatch.Text = "Schnittstelle wird aktiviert – Administratorabfrage bestätigen.";

            bool ok;
            string message;
            try
            {
                (ok, message) = await System.Threading.Tasks.Task.Run(
                    () => DeviceInterfaceRecovery.Enable(instanceId));
            }
            catch (Exception ex)
            {
                ok = false;
                message = "Aktivieren fehlgeschlagen: " + ex.Message;
            }

            App.LogApp("Banner: Schnittstelle aktivieren – " + (ok ? "ok" : "fehlgeschlagen") + ": " + message);

            TxtInputMismatch.Text = message;
            BtnEnableDisabledInterface.Content = "Schnittstelle aktivieren";
            BtnEnableDisabledInterface.IsEnabled = true;

            // Bei Erfolg verschwindet das Banner mit dem nächsten Rescan (Schnittstelle
            // läuft, Windows sieht das Gerät wieder); bei Misserfolg bleibt der Text stehen.
            if (ok) RefreshGamepadsList();
        }

        private void ComboGamepads_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Pfeiltasten (↑/↓) öffnen die Controller-Liste, statt nur zu navigieren
            if (!ComboGamepads.IsDropDownOpen && (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up))
                ComboGamepads.IsDropDownOpen = true;
        }

        private void ComboGamepads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = ComboGamepads.SelectedIndex;
            if (idx >= 0 && idx < _comboControllers.Count)
            {
                ControllerManager.Instance.ActiveGamepadIndex = idx;
                ControllerManager.Instance.SetActiveController(_comboControllers[idx].StableId);
                UpdateStatusFromSelection();
            }
        }

        private void UpdateStatusFromSelection()
        {
            int idx = ComboGamepads.SelectedIndex;
            if (_comboControllers.Count == 0 || idx < 0 || idx >= _comboControllers.Count)
            {
                SetStatusText(Lang.Status_Disconnected, System.Windows.Media.Brushes.Gray);
                return;
            }

            var c = _comboControllers[idx];
            if (c.AccessError)
            {
                SetStatusText(Lang.Status_AccessError, System.Windows.Media.Brushes.Orange);
                App.LogApp("Controller ohne Zugriff: " + c.Name + " – " + c.AccessErrorReason);
            }
            else
            {
                SetStatusText(Lang.Status_Connected, System.Windows.Media.Brushes.LightGreen);
            }
        }

        private bool HasAnyAccessError()
        {
            foreach (var c in _comboControllers)
                if (c.AccessError) return true;
            return false;
        }

        /// <summary>
        /// Die Statuszeile („Status: Verbunden“) wurde aus dem Hauptfenster entfernt.
        /// Die Methode bleibt als No-op bestehen, damit alle Aufrufer unverändert bleiben.
        /// </summary>
        private void SetStatusText(string text, System.Windows.Media.Brush brush)
        {
            // absichtlich leer – Anzeige entfernt
        }

        private void TogglePause()
        {
            ProfileManager.SetPaused(!ProfileManager.IsPaused);
            if (!ProfileManager.IsPaused)
            {
                UpdateStatusFromSelection();
            }
            UpdateTrayMenu();
        }

        private void BtnControllerHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ControllerHistoryWindow { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.LogApp("Verlauf-Fenster Fehler: " + ex.Message);
            }
        }

        private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new DiagnosticsWindow { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                App.LogApp("Diagnose-Fenster Fehler: " + ex.Message);
            }
        }

        /// <summary>Höhe des Controller-Bereichs vor dem Einklappen (DIP).</summary>
        private double _gamepadRowHeight = 370;
        private bool _gamepadCollapsed;

        private void BtnToggleGamepad_Click(object sender, RoutedEventArgs e)
            => SetGamepadCollapsed(!_gamepadCollapsed, persist: true);

        /// <summary>
        /// Klappt das Controller-Feld auf seine Kopfzeile zusammen, damit die Tastatur den
        /// ganzen linken Bereich bekommt. Ausgeklappt wird die vorherige Höhe wiederhergestellt.
        /// </summary>
        private void SetGamepadCollapsed(bool collapsed, bool persist)
        {
            _gamepadCollapsed = collapsed;

            if (collapsed)
            {
                if (GamepadRow.ActualHeight > 0) _gamepadRowHeight = GamepadRow.ActualHeight;
                GamepadRow.Height = GridLength.Auto;
            }
            else
            {
                GamepadRow.Height = new GridLength(_gamepadRowHeight > 0 ? _gamepadRowHeight : 370);
            }

            SplitterRow.Height = collapsed ? new GridLength(0) : new GridLength(8);
            SplitterGamepad.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            GamepadLiveRow.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            GamepadSurface.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

            BtnToggleGamepad.Content = collapsed ? "▸" : "▾";
            BtnToggleGamepad.ToolTip = collapsed ? Lang.Gamepad_Expand : Lang.Gamepad_Collapse;

            if (persist)
            {
                _settings.GamepadCollapsed = collapsed;
                SaveSettings();
            }
        }

        private void OnControllerStateChanged(object? sender, ControllerStateEventArgs e)
        {
            // Eingabe-Verarbeitung direkt auf dem Poll-Thread: StateChanged wird
            // ausschließlich vom Poll-Thread ausgelöst (ReadXInput/ReadHid/
            // ReadWinMM). Die Mapping-Ausgabe (SendInput) läuft damit NICHT über
            // den WPF-Dispatcher und wird durch UI-Stau (Stick-Visualisierung,
            // Button-Highlights) nicht mehr verzögert.
            LatencyMonitor.MarkInputEvent();
            ProfileManager.Instance.ProcessControllerInput(e.PressedButtons);

            // UI-Updates bündeln: nur EIN ausstehender BeginInvoke gleichzeitig.
            // Kommt der UI-Thread nicht nach (z.B. Stick-Visualisierung bei ~250 Hz),
            // gewinnt der neueste Zustand – die Dispatcher-Queue wächst nicht endlos
            // und die Eingabe-Ausgabe ist davon ohnehin entkoppelt.
            if (_stateUiUpdatePending) return;
            // Zusätzlich auf max. ~120 Hz begrenzen: rendern kann nur der Monitor,
            // und bei Dauerbewegung der Sticks würde der UI-Thread sonst mit 250 Hz
            // Layout/Render-Arbeit beschäftigt.
            if (_uiGate.ElapsedMilliseconds < MinUiUpdateIntervalMs) return;
            _stateUiUpdatePending = true;
            _uiGate.Restart();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    sw.Stop();
                    LatencyMonitor.RecordDispatcherBacklog(sw.Elapsed);
                    UpdateUiFromControllerState(e);
                }
                finally
                {
                    _stateUiUpdatePending = false;
                }
            });
        }

        private void UpdateUiFromControllerState(ControllerStateEventArgs e)
        {
                if (e.ControllerName == "None")
                {
                    if (HasAnyAccessError())
                    {
                        SetStatusText(Lang.Status_AccessError, System.Windows.Media.Brushes.Orange);
                    }
                    else
                    {
                        SetStatusText(Lang.Status_Disconnected, System.Windows.Media.Brushes.Gray);
                    }
                }
                else
                {
                    SetStatusText(Lang.Status_Connected, System.Windows.Media.Brushes.LightGreen);
                }

                foreach (var pair in _gamepadButtonsUi)
                {
                    string btnKey = pair.Key;
                    System.Windows.Controls.Button btn = pair.Value;

                    if (e.PressedButtons.Contains(btnKey))
                    {
                        // Highlight nur beim Übergang setzen – nicht bei jedem Tick
                        if (!_lastRenderedButtons.Contains(btnKey))
                        {
                            _lastRenderedButtons.Add(btnKey);
                            btn.Background = System.Windows.Media.Brushes.Cyan;
                            btn.BorderBrush = System.Windows.Media.Brushes.White;
                        }
                        if (!_physicallyHeldButtons.Contains(btnKey))
                        {
                            _physicallyHeldButtons.Add(btnKey);
                            _capture.AddComboButton(btnKey);
                            TxtSelectedCombo.Text = _capture.ComboDisplay;
                            if (string.IsNullOrEmpty(_capture.ComboDisplay))
                                TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
                        }
                    }
                    else
                    {
                        if (_lastRenderedButtons.Contains(btnKey))
                        {
                            _lastRenderedButtons.Remove(btnKey);
                            if (btnKey == "A") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 44, 30));
                            else if (btnKey == "B") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 20, 24));
                            else if (btnKey == "X") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 32, 48));
                            else if (btnKey == "Y") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 36, 21));
                            else btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 36));

                            btn.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 61));
                        }
                        _physicallyHeldButtons.Remove(btnKey);
                    }
                }

                // Also auto-detect analog stick directions
                var stickDirections = new[] { "LS_LEFT", "LS_RIGHT", "LS_UP", "LS_DOWN", "RS_LEFT", "RS_RIGHT", "RS_UP", "RS_DOWN" };
                foreach (var dir in stickDirections)
                {
                    if (e.PressedButtons.Contains(dir))
                    {
                        if (!_physicallyHeldButtons.Contains(dir))
                        {
                            _physicallyHeldButtons.Add(dir);
                            _capture.AddComboButton(dir);
                            TxtSelectedCombo.Text = _capture.ComboDisplay;
                            if (string.IsNullOrEmpty(_capture.ComboDisplay))
                                TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
                        }
                    }
                    else
                    {
                        _physicallyHeldButtons.Remove(dir);
                    }
                }

            if (!_settings.EditMode)
            {
                // Map -1..1 to 0..24 range (centered at 12) – nur bei Änderung setzen,
                // sonst invalidieren die Canvas.SetLeft/SetTop bei jedem Tick das Layout.
                // Quantisiert, damit winziges Analog-Jitter in Ruhelage nicht rendert.
                double lx = Math.Round(e.LeftStickX, 3);
                double ly = Math.Round(e.LeftStickY, 3);
                double rx = Math.Round(e.RightStickX, 3);
                double ry = Math.Round(e.RightStickY, 3);
                if (lx != _lastRenderedLx || ly != _lastRenderedLy)
                {
                    _lastRenderedLx = lx;
                    _lastRenderedLy = ly;
                    Canvas.SetLeft(Btn_L3, 12 + (lx * 12));
                    Canvas.SetTop(Btn_L3, 12 + (ly * -12));
                }
                if (rx != _lastRenderedRx || ry != _lastRenderedRy)
                {
                    _lastRenderedRx = rx;
                    _lastRenderedRy = ry;
                    Canvas.SetLeft(Btn_R3, 12 + (rx * 12));
                    Canvas.SetTop(Btn_R3, 12 + (ry * -12));
                }
            }
        }

        #region Animated Background (GhostFibers)

        /// <summary>
        /// Übernimmt die Hintergrund-Einstellung: Effekt sichtbar/aus und Animationsschleife
        /// an/aus. Ist der Shader nicht verfügbar, bleibt der flache Hintergrund – das
        /// Fenster funktioniert in jedem Fall.
        /// </summary>
        private void ApplyBackgroundSetting()
        {
            _backgroundEffect = GhostFibersBackground.Effect as Effects.GhostFibersEffect;

            bool wanted = _settings.AnimatedBackgroundEnabled && _backgroundEffect != null;

            // Absicherung: Hardware-Beschleunigung, Stromversorgung und die native
            // Bildwiederholrate des Bildschirms stehen sofort fest; die gemessenen Anzeigetakte
            // entscheiden später über Drosselung oder Abschaltung.
            _backgroundPolicy.ForcedQuality = _settings.BackgroundQualityOverride;
            _backgroundPolicy.Initialize(IsOnBattery(), WpfRenderTier);
            _backgroundActivityLoggedAt = DateTime.MinValue;
            _backgroundDrawnSinceLog = 0;
            bool enabled = wanted && _backgroundPolicy.Enabled;

            GhostFibersBackground.Visibility = enabled
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (!enabled)
            {
                StopBackgroundAnimation();
                if (wanted) LogBackgroundQuality(force: true);
                return;
            }

            _backgroundResolutionReady = true;
            ApplyBackgroundShaderCost();
            UpdateBackgroundResolution();
            LogBackgroundQuality(force: true);
            if (IsVisible && WindowState != WindowState.Minimized) StartBackgroundAnimation();
        }

        /// <summary>
        /// Übergibt die Flächengröße in **Gerätepixeln** an den Shader (nicht in DIP), sonst würde
        /// das Fasermuster mit der Windows-Zoomstufe größer oder kleiner werden.
        /// </summary>
        private void UpdateBackgroundResolution()
        {
            if (_backgroundEffect == null || !_backgroundResolutionReady) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            _backgroundEffectResolution = new System.Windows.Point(
                Math.Max(1, ActualWidth * dpi.DpiScaleX),
                Math.Max(1, ActualHeight * dpi.DpiScaleY));
            _backgroundEffect.Resolution = _backgroundEffectResolution;
        }

        /// <summary>
        /// Übergibt die Rechenarbeit der aktuellen Stufe an den Shader: Anzahl der Faserschichten
        /// und Grain. Das ist der Hebel, der in WPF nachweislich GPU-Zeit spart – jede Schicht
        /// kostet einen Wellen-, Polar-, <c>pow(…,16)</c>- und Glow-Durchlauf je Pixel, und Grain
        /// fünf zusätzliche Hashes.
        ///
        /// Eine echte Auflösungsreduktion wurde am 13.09.2026 gemessen und verworfen: eine
        /// Skalierung des Elements spart nichts (9,5 % gegen 9,4 % GPU), <c>BitmapCache(0.5)</c>
        /// ebenso (9,5 %), und der Weg über eine klein gerenderte Fläche mit VisualBrush sparte in
        /// der App nichts (4,6–4,8 % gegen 4,8 %). Feste Width/Height auf dieser Fläche machten es
        /// sogar teurer als volle Auflösung (12,5 % gegen 7,3 %).
        /// </summary>
        private void ApplyBackgroundShaderCost()
        {
            if (_backgroundEffect == null) return;
            _backgroundEffect.Layers = _backgroundPolicy.Layers;
            _backgroundEffect.Grain = _backgroundPolicy.Grain;
        }

        private void StartBackgroundAnimation()
        {
            if (_backgroundEffect == null || _backgroundLoopHooked) return;
            _backgroundNextFrameAt = DateTime.MinValue;
            _backgroundLastFrameAt = DateTime.MinValue;
            _backgroundActivityLoggedAt = DateTime.MinValue;
            _backgroundDrawnSinceLog = 0;
            CompositionTarget.Rendering += OnBackgroundRendering;
            _backgroundLoopHooked = true;
        }

        private void StopBackgroundAnimation()
        {
            if (!_backgroundLoopHooked) return;
            CompositionTarget.Rendering -= OnBackgroundRendering;
            _backgroundLoopHooked = false;
        }

        private void OnBackgroundRendering(object? sender, EventArgs e)
        {
            if (_backgroundEffect == null) return;

            DateTime now = DateTime.UtcNow;

            // Jeden Anzeigetakt melden – auch die, in denen der Effekt gedrosselt nichts zeichnet.
            // Nur so ist erkennbar, ob die Oberfläche insgesamt noch mitkommt.
            var quality = _backgroundPolicy.Report(now, IsOnBattery(), WpfRenderTier);
            if (quality != _backgroundQualityLogged)
            {
                LogBackgroundQuality(force: true);
                ApplyBackgroundShaderCost();       // Stufenwechsel: Rechenarbeit nachziehen
            }

            // Alle 30 s belegen, was wirklich passiert: der gemessene Anzeigetakt (kommt die
            // Oberfläche mit?) und die tatsächlich gezeichnete Bildrate (greift die Drosselung
            // wie beabsichtigt?). Nur so ist die Drosselung nachweisbar und nicht bloß eingestellt.
            if (_backgroundActivityLoggedAt == DateTime.MinValue)
            {
                _backgroundActivityLoggedAt = now;
                _backgroundDrawnSinceLog = 0;
            }
            else if ((now - _backgroundActivityLoggedAt).TotalSeconds >= 30)
            {
                double seconds = (now - _backgroundActivityLoggedAt).TotalSeconds;
                double drawnPerSecond = _backgroundDrawnSinceLog / seconds;
                double hz = Effects.DisplayRefresh.Hz;
                string reference = hz > 1 ? " (Monitor " + hz.ToString("0") + " Hz)" : "";
                string measuredText = _backgroundPolicy.LastMeasuredDisplayFps is double measuredFps
                    ? measuredFps.ToString("0.0") + " Takte/s"
                    : "noch keine Messung";
                App.LogApp("Hintergrund: Anzeige " + measuredText + reference
                    + " | gezeichnet " + drawnPerSecond.ToString("0.0") + "/s (Ziel "
                    + ((int)_backgroundPolicy.TargetFps) + " fps)"
                    + " | " + _backgroundPolicy.Describe(IsOnBattery(), WpfRenderTier)
                    + " [" + ((int)_backgroundEffectResolution.X) + "x" + ((int)_backgroundEffectResolution.Y) + " Pixel]");
                _backgroundActivityLoggedAt = now;
                _backgroundDrawnSinceLog = 0;
            }

            if (!_backgroundPolicy.Enabled)
            {
                // Die aktuelle Hardware kommt nicht mit: Effekt abschalten statt bremsen.
                GhostFibersBackground.Visibility = System.Windows.Visibility.Collapsed;
                StopBackgroundAnimation();
                return;
            }

            // Drosselung: gezeichnet wird nur im Zielabstand (30 bzw. 15 Bilder/s). WPF rendert
            // nur, wenn sich ein Uniform wirklich ändert – die Drosselung spart also echte GPU-Zeit.
            // Der Zeitplan wird aus dem Ziel fortgeschrieben, nicht auf den echten Takt gerundet:
            // sonst käme bei krummen Verhältnissen zu selten ein Bild heraus (gemessen: 21 statt
            // 30 Bilder/s bei 64 Takten/s Takt).
            var targetInterval = _backgroundPolicy.TargetInterval;
            if (_backgroundNextFrameAt == DateTime.MinValue) _backgroundNextFrameAt = now;
            if (now < _backgroundNextFrameAt) return;

            if ((now - _backgroundNextFrameAt).TotalSeconds > 1.0)
            {
                _backgroundNextFrameAt = now;            // nach langer Pause nichts aufholen
            }
            else
            {
                do { _backgroundNextFrameAt = _backgroundNextFrameAt.Add(targetInterval); }
                while (_backgroundNextFrameAt <= now);
            }

            // Wie im Original: große Zeitsprünge (z.B. nach Standby) begrenzen.
            double delta = _backgroundLastFrameAt == DateTime.MinValue
                ? 0
                : (now - _backgroundLastFrameAt).TotalSeconds;
            _backgroundLastFrameAt = now;
            _backgroundElapsedSeconds += Math.Min(delta, 0.1);
            _backgroundEffect.Time = _backgroundElapsedSeconds;
            _backgroundDrawnSinceLog++;
        }

        /// <summary>WPF-Renderstufe: 0 = ohne Hardware-Beschleunigung (dann kein Effekt).</summary>
        private static int WpfRenderTier => System.Windows.Media.RenderCapability.Tier >> 16;

        /// <summary>
        /// Läuft der Rechner auf Akku? Auf Desktop-Rechnern gibt es keinen Akku und die
        /// Abfrage meldet „Online" – dort wird also nie gedrosselt.
        /// </summary>
        private static bool IsOnBattery()
        {
            try
            {
                return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
                    == System.Windows.Forms.PowerLineStatus.Offline;
            }
            catch
            {
                return false;   // im Zweifel wie Netzbetrieb behandeln
            }
        }

        /// <summary>Schreibt jede Qualitätsstufe genau einmal ins Log – als Nachweis.</summary>
        private void LogBackgroundQuality(bool force = false)
        {
            if (!force && _backgroundPolicy.Quality == _backgroundQualityLogged) return;
            _backgroundQualityLogged = _backgroundPolicy.Quality;

            // Neue Stufe → für diese Stufe wird die erreichte Bildrate erneut belegt.
            _backgroundActivityLoggedAt = DateTime.MinValue;
            _backgroundDrawnSinceLog = 0;

            // Immer die *wirklich* gerechnete Stufe nennen – eine feste Vorgabe aus den
            // Einstellungen kann von der Entscheidung der Regel abweichen.
            string text = "Hintergrund: " + _backgroundPolicy.Describe(IsOnBattery(), WpfRenderTier);
            if (_backgroundPolicy.Enabled)
            {
                text += " | Regel sagt: " + _backgroundPolicy.Quality
                    + ", gerechnet wird: " + _backgroundPolicy.EffectiveQuality;
                if (_backgroundResolutionReady)
                {
                    text += " auf " + _backgroundEffectResolution.X.ToString("0") + "x"
                        + _backgroundEffectResolution.Y.ToString("0") + " Pixeln";
                }
            }
            App.LogApp(text);
        }

        #endregion

        /// <summary>Übernimmt den Ausgabemodus („VK“ oder „ScanCode“) auf den Input-Simulator.</summary>
        private void ApplyOutputMode()
        {
            InputSimulator.OutputMode = _settings.OutputMode;
        }

        private void ComboOutputMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string mode = ComboOutputMode.SelectedIndex == 1 ? "ScanCode" : "VK";
            if (_settings.OutputMode != mode)
            {
                _settings.OutputMode = mode;
                ApplyOutputMode();
                SaveSettings();
            }
        }

        #endregion


        #region Profiles Management

        private void RefreshProfilesList()
        {
            ComboProfiles.Items.Clear();
            var profiles = ProfileManager.Instance.GetProfileNames();
            foreach (var p in profiles)
                ComboProfiles.Items.Add(p);
        }

        private void SelectProfile(string profileName)
        {
            var profile = ProfileManager.Instance.LoadProfileByName(profileName);
            if (profile != null)
            {
                _settings.LastActiveProfile = profileName;
                SaveSettings();
                ComboProfiles.SelectedItem = profileName;
                ListMappings.ItemsSource = null;
                ListMappings.ItemsSource = profile.Mappings;
            }
        }

        private void ComboProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboProfiles.SelectedItem is string selectedProfile && ProfileManager.Instance.ActiveProfile.Name != selectedProfile)
            {
                SelectProfile(selectedProfile);
            UpdateTrayMenu();
        }
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(Lang.Dialog_NewProfilePrompt, Lang.Dialog_NewProfileTitle, Lang.Dialog_NewProfileDefault);
            input = input.Trim();
            if (string.IsNullOrEmpty(input)) return;
            foreach (char c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');

            ProfileManager.Instance.SaveProfile(new MappingProfile { Name = input, Mappings = new List<Mapping>() });
            RefreshProfilesList();
            SelectProfile(input);
            UpdateTrayMenu();
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboProfiles.SelectedItem is string profileName && profileName != "Default")
            {
                var result = System.Windows.MessageBox.Show(Lang.Format("Dialog_DeleteProfileMsg", profileName), Lang.Dialog_DeleteProfileTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    ProfileManager.Instance.DeleteProfile(profileName);
                    RefreshProfilesList();
                    SelectProfile("Default");
                    UpdateTrayMenu();
                }
            }
        }

        #endregion

        #region Mapping Creation Logic

        private void GamepadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_settings.EditMode && sender is System.Windows.Controls.Button btn)
            {
                string key = btn.Name.Replace("Btn_", "");
                _capture.AddComboButton(key);
                TxtSelectedCombo.Text = _capture.ComboDisplay;
                if (string.IsNullOrEmpty(_capture.ComboDisplay))
                    TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
            }
        }

        private void KeyboardKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                string key = btn.Tag as string ?? btn.Content.ToString() ?? "";
                _capture.AddActionKey(key);
                TxtSelectedAction.Text = _capture.ActionDisplay;
                if (string.IsNullOrEmpty(_capture.ActionDisplay))
                    TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
            }
        }

        private void BtnClearCombo_Click(object sender, RoutedEventArgs e)
        {
            _capture.ClearCombo();
            TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
        }

        private void ListMappings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Gespeicherte Kombination anklicken → in den Editor laden, damit sie
            // bearbeitet werden kann (Text-Aktion/Tasten ändern statt neu anlegen).
            if (ListMappings.SelectedItem is Mapping mapping)
            {
                _capture.LoadMapping(mapping);
                TxtSelectedCombo.Text = _capture.ComboDisplay;
                if (string.IsNullOrEmpty(_capture.ComboDisplay))
                    TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
                TxtSelectedAction.Text = _capture.ActionDisplay;
                if (string.IsNullOrEmpty(_capture.ActionDisplay))
                    TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
                TxtMappingName.Text = mapping.Name ?? "";
                // Laufende Aufnahme beenden + Button zurücksetzen
                BtnRecord.Content = Lang.Mapping_Record;
                BtnRecord.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                BtnRecord.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 44, 58));
            }
        }

        private void BtnClearAction_Click(object sender, RoutedEventArgs e)
        {
            _capture.ClearAction();
            TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
        }

        private void BtnSaveMapping_Click(object sender, RoutedEventArgs e)
        {
            if (!_capture.HasCombo || !_capture.HasAction)
            {
                System.Windows.MessageBox.Show(Lang.Dialog_MappingIncompleteMsg, Lang.Dialog_MappingIncompleteTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Name (Beschriftung) ist optional und wird nie getippt.
            string? name = string.IsNullOrWhiteSpace(TxtMappingName.Text) ? null : TxtMappingName.Text.Trim();

            var existing = ProfileManager.Instance.ActiveProfile.Mappings.FirstOrDefault(m => m.Combo.SequenceEqual(_capture.CurrentCombo));
            if (existing != null)
            {
                existing.Action = new List<string>(_capture.CurrentAction);
                existing.Text = _capture.CurrentActionText;
                existing.Name = name;
            }
            else
                ProfileManager.Instance.AddMapping(new Mapping
                {
                    Combo = new List<string>(_capture.CurrentCombo),
                    Action = new List<string>(_capture.CurrentAction),
                    Text = _capture.CurrentActionText,
                    Name = name
                });

            ProfileManager.Instance.SaveActiveProfile();
            _capture.Reset();
            TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
            TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
            TxtMappingName.Text = "";
            BtnRecord.Content = Lang.Mapping_Record;
            BtnRecord.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            BtnRecord.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 44, 58));
            ListMappings.ItemsSource = null;
            ListMappings.ItemsSource = ProfileManager.Instance.ActiveProfile.Mappings;
        }

        private void BtnDeleteMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Mapping mapping)
            {
                ProfileManager.Instance.RemoveMapping(mapping);
                ProfileManager.Instance.SaveActiveProfile();
                ListMappings.ItemsSource = null;
                ListMappings.ItemsSource = ProfileManager.Instance.ActiveProfile.Mappings;
            }
        }

        private void BtnTestOutput_Click(object sender, RoutedEventArgs e)
        {
            var action = new List<string>();
            if (_capture.HasAction)
                action.AddRange(_capture.CurrentAction);
            else if (ProfileManager.Instance.ActiveProfile.Mappings.Count > 0)
                action.AddRange(ProfileManager.Instance.ActiveProfile.Mappings[0].Action);

            if (action.Count > 0)
                InputSimulator.SimulateCombination(action);
        }

        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            _capture.ToggleCapture();

            if (_capture.IsCapturing)
            {
                _capture.ClearCombo();
                _capture.ClearAction();
                TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
                TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
                TxtMappingName.Text = "";
                BtnRecord.Content = Lang.Mapping_StopRecord;
                BtnRecord.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                BtnRecord.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 20, 24));
            }
            else
            {
                BtnRecord.Content = Lang.Mapping_Record;
                BtnRecord.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                BtnRecord.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 44, 58));
            }
        }

        #endregion

        #region Application Settings & Autostart

        private void LoadSettings()
        {
            // Migration: move old AppData config to Documents
            string oldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TenzoraX", ConfigFileName);
            if (File.Exists(oldPath) && !File.Exists(ConfigFilePath))
            {
                try { File.Move(oldPath, ConfigFilePath); } catch { }
            }

            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { _settings = new AppSettings(); }
            }
            else
            {
                _settings = new AppSettings();
                SaveSettings();
            }

            // Design-Stil anwenden, bevor das Fenster zum ersten Mal gezeichnet wird.
            Theme3D.Apply(_settings.UiTheme);

            if (_settings.RelativeButtonPositions.Count == 0)
            {
                SetDefaultRelativePositions();
                SaveSettings();
            }

            // Xbox-Spiegelung wird nie persistiert – immer deaktiviert beim Start.
            _settings.UseVigemXbox = false;

        }

        private void SetDefaultRelativePositions()
        {
            // Default positions matching the reference controller image (image at 50,20, size 400x230)
            var defaults = new Dictionary<string, double>
            {
                ["L2_X"] = 0.209, ["L2_Y"] = -0.151,
                ["R2_X"] = 0.799, ["R2_Y"] = -0.151,
                ["L1_X"] = 0.215, ["L1_Y"] = -0.058,
                ["R1_X"] = 0.797, ["R1_Y"] = -0.052,
                ["DPAD_UP_X"] = 0.422, ["DPAD_UP_Y"] = 0.381,
                ["DPAD_LEFT_X"] = 0.359, ["DPAD_LEFT_Y"] = 0.454,
                ["DPAD_RIGHT_X"] = 0.476, ["DPAD_RIGHT_Y"] = 0.468,
                ["DPAD_DOWN_X"] = 0.409, ["DPAD_DOWN_Y"] = 0.567,
                ["SELECT_X"] = 0.446, ["SELECT_Y"] = 0.212,
                ["START_X"] = 0.591, ["START_Y"] = 0.212,
                ["L3_X"] = 0.240, ["L3_Y"] = 0.156,
                ["R3_X"] = 0.678, ["R3_Y"] = 0.410,
                ["Y_X"] = 0.871, ["Y_Y"] = 0.100,
                ["X_X"] = 0.787, ["X_Y"] = 0.201,
                ["B_X"] = 0.947, ["B_Y"] = 0.228,
                ["A_X"] = 0.865, ["A_Y"] = 0.341,
            };
            foreach (var kvp in defaults)
                _settings.RelativeButtonPositions[kvp.Key] = kvp.Value;
        }

        public void ResetPositionsToDefaults()
        {
            _settings.RelativeButtonPositions.Clear();
            _settings.ButtonPositions.Clear();
            SetDefaultRelativePositions();
            SaveSettings();
            ReapplyButtonPositions();
        }

        private void SaveWindowPosition()
        {
            if (WindowState == WindowState.Maximized && RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
            {
                _settings.WindowWidth = RestoreBounds.Width;
                _settings.WindowHeight = RestoreBounds.Height;
                _settings.WindowLeft = RestoreBounds.Left;
                _settings.WindowTop = RestoreBounds.Top;
            }
            else if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
            }
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            SaveSettings();
        }

        private void SaveSettings()
        {
            if (!_settingsLoaded) return;
            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch { }
        }

        private void InitLanguage()
        {
            string lang = _settings.Language;
            if (string.IsNullOrEmpty(lang))
            {
                lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (!Lang.HasLanguage(lang))
                    lang = "en";
                _settings.Language = lang;
                SaveSettings();
            }

            ApplyLanguage(lang);
        }

        private void LogCrash(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(GetDocumentsPath(), "crash.log");
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n{ex.StackTrace}\n";
                File.AppendAllText(logPath, entry);
            }
            catch { }
        }

        private void ApplyLanguage(string lang)
        {
            Lang.Switch(lang);
            RefreshGamepadsList();
            UpdateTrayMenu();

            int sel = ComboOutputMode.SelectedIndex;
            ComboOutputMode.Items.Clear();
            ComboOutputMode.Items.Add(Lang.OutputMode_VK);
            ComboOutputMode.Items.Add(Lang.OutputMode_ScanCode);
            ComboOutputMode.SelectedIndex = sel >= 0 ? sel : 0;

            if (!_capture.IsCapturing)
                BtnRecord.Content = Lang.Mapping_Record;
            else
                BtnRecord.Content = Lang.Mapping_StopRecord;

            UpdateVigemStatusMain();
            UpdateHidhideStatusMain();
        }

        private bool _suppressVigemChanged;
        /// <summary>Alle Schnittstellen, die die Automatik aktuell versteckt hält.</summary>
        private readonly List<string> _hidhideHidden = new();
        /// <summary>True, wenn gerade Schnittstellen versteckt sind (Spiegelung).</summary>
        private bool _hidhideAutoActive => _hidhideHidden.Count > 0;
        private bool _adminPromptShown;
        private string _hidhideStatusOverride = "";
        private System.Windows.Threading.DispatcherTimer? _hidhideRestoreTimer;

        private void InitVigemHidhideUi()
        {
            _suppressVigemChanged = true;
            ChkVigemXboxMain.IsChecked = _settings.UseVigemXbox;
            _suppressVigemChanged = false;
            UpdateVigemStatusMain();
            UpdateHidhideStatusMain();
            UpdateDriversHeaderStatus();

            // Treiber-Status live halten: Registry-Check ist billig (ein paar
            // Millisekunden alle 3 s) – so wird das Licht automatisch grün, sobald
            // der Nutzer die Installer bestätigt hat, ohne die App neu zu starten.
            _driversStatusTimer?.Stop();
            _driversStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _driversStatusTimer.Tick += (s, e) =>
            {
                if (_driversInstallPending) return;  // während Installation nicht umschalten
                UpdateDriversHeaderStatus();
            };
            _driversStatusTimer.Start();
        }

        private DispatcherTimer? _driversStatusTimer;
        private volatile bool _driversInstallPending;

        private void ChkVigemXboxMain_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressVigemChanged) return;

            bool on = ChkVigemXboxMain.IsChecked == true;
            _settings.UseVigemXbox = on;
            SaveSettings();

            // Einmalig pro Sitzung vorschlagen, TenzoraX dauerhaft als Administrator
            // laufen zu lassen: Die HidHide-Automatik (Gamepad verstecken) und die
            // Eingabe in erhöhte Spiele funktionieren dann ohne jede UAC-Rückfrage.
            if (on && !InputSimulator.IsRunningAsAdmin() && !_settings.RunAsAdministrator && !_adminPromptShown)
            {
                _adminPromptShown = true;
                var adminResult = System.Windows.MessageBox.Show(
                    LanguageManager.Instance.Admin_TogglePrompt,
                    LanguageManager.Instance.Admin_ToggleTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (adminResult == MessageBoxResult.Yes)
                {
                    _settings.RunAsAdministrator = true;
                    SaveSettings();
                    RestartAsAdmin();
                    return; // Neustart beendet diesen Prozess (bzw. meldet Abbruch)
                }
            }

            bool ok = ControllerManager.Instance.SetVigemXbox(on);
            UpdateVigemStatusMain();

            // HidHide-Automatik: physischen Controller automatisch verstecken, damit
            // Spiele nur die virtuelle Xbox 360 sehen (kein Doppel-Input). Beim
            // Deaktivieren wird das Verstecken wieder aufgehoben. Läuft im Hintergrund,
            // damit die UI beim Ausführen der CLI nicht blockiert.
            // HidHide-Zustand den aktiven Optionen nachziehen: Mit Spiegelung werden alle
            // Schnittstellen des Pads versteckt (Spiele sehen nur die virtuelle Xbox 360),
            // ohne sie bleiben nur die HID-Schnittstellen des Duplikat-Schalters übrig.
            RefreshHidHide();

            if (on && !ok)
            {
                if (!VigemXboxBridge.IsDriverInstalled())
                {
                    // Lokaler Installer vorhanden? Dann direkt starten, sonst Download-Seite anbieten.
                    string? local = DriverInstaller.Find(DriverInstaller.ViGEmBusFile);
                    if (local != null)
                    {
                        var result = System.Windows.MessageBox.Show(
                            LanguageManager.Instance.Vigem_InstallLocalPrompt,
                            LanguageManager.Instance.Vigem_NoDriverTitle,
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                            DriverInstaller.LaunchInstaller(local);
                    }
                    else
                    {
                        var result = System.Windows.MessageBox.Show(
                            LanguageManager.Instance.Vigem_InstallPrompt,
                            LanguageManager.Instance.Vigem_NoDriverTitle,
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            try { Process.Start(new ProcessStartInfo(VigemXboxBridge.DriverDownloadUrl) { UseShellExecute = true }); }
                            catch { }
                        }
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.Instance.Vigem_NoDriverMsg,
                        LanguageManager.Instance.Vigem_NoDriverTitle,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnOpenHidhideClient_Click(object sender, RoutedEventArgs e)
        {
            string? client = FindHidHideClient();
            if (client == null)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.Instance.Hidhide_Missing,
                    LanguageManager.Instance.Hidhide_MissingTitle,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = client,
                UseShellExecute = true,
                // Die HidHide-Konfiguration (Geräte verstecken / erlaubte Apps) benötigt
                // Administrator-Rechte. Läuft TenzoraX bereits als Admin, entfällt die
                // erneute UAC-Abfrage.
                Verb = InputSimulator.IsRunningAsAdmin() ? "" : "runas"
            };
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // UAC abgebrochen oder Start fehlgeschlagen – nicht mehr still schlucken.
                System.Windows.MessageBox.Show(
                    LanguageManager.Instance.Format("Hidhide_LaunchError", ex.Message),
                    LanguageManager.Instance.Hidhide_MissingTitle,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Sucht den HidHide Configuration Client: zuerst im Standard-Installationspfad
        /// (64- und 32-Bit), dann im Dokumente-Datenordner von TenzoraX („Dokumente\TenzoraX“
        /// und „Dokumente\TenzoraX\drivers“) und zuletzt über die Registry-Deinstallations-
        /// einträge. Wichtig: Die Client-Datei kann direkt im Installationsordner ODER im
        /// Unterordner „x64“/„x86“ liegen – alle Varianten werden geprüft.
        /// </summary>
        private static string? FindHidHideClient()
        {
            string docRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var bases = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Nefarius Software Solutions", "HidHide"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Nefarius Software Solutions", "HidHide"),
                Path.Combine(docRoot, "TenzoraX"),
                Path.Combine(docRoot, "TenzoraX", "drivers")
            };
            foreach (var baseDir in bases)
            {
                var exe = FindHidHideClientInDir(baseDir);
                if (exe != null) return exe;
            }

            return FindHidHideClientViaRegistry();
        }

        /// <summary>Prüft einen Ordner auf HidHideClient.exe – direkt und in den x64/x86-Unterordnern.</summary>
        private static string? FindHidHideClientInDir(string baseDir)
        {
            foreach (var sub in new[] { "", "x64", "x86" })
            {
                var p = string.IsNullOrEmpty(sub)
                    ? Path.Combine(baseDir, "HidHideClient.exe")
                    : Path.Combine(baseDir, sub, "HidHideClient.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Sucht HidHideClient.exe über die Windows-Deinstallations-Registry (64- und 32-Bit-Sicht).</summary>
        private static string? FindHidHideClientViaRegistry()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null) continue;

                    foreach (var subName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var entry = uninstall.OpenSubKey(subName);
                            string? displayName = entry?.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(displayName) ||
                                displayName.IndexOf("HidHide", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            string? location = entry?.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(location))
                            {
                                var exe = FindHidHideClientInDir(location);
                                if (exe != null) return exe;
                            }

                            string? icon = entry?.GetValue("DisplayIcon") as string;
                            if (!string.IsNullOrEmpty(icon))
                            {
                                var iconPath = icon.Split(',')[0].Trim('"');
                                if (iconPath.EndsWith("HidHideClient.exe", StringComparison.OrdinalIgnoreCase)
                                    && File.Exists(iconPath))
                                    return iconPath;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>Aktualisiert das Treiber-Status-Licht in der Kopfzeile.
        /// Grün: ViGEmBus UND HidHide installiert. Rot: mindestens einer fehlt.
        /// Grau: Zustand noch unklar (z.B. direkt beim Start).</summary>
        private void UpdateDriversHeaderStatus()
        {
            if (DriversStatusDotHeader == null) return;

            bool vigem = VigemXboxBridge.IsDriverInstalled();
            bool hidhide = DriverInstaller.IsDriverInstalled("hidhide");
            bool allOk = vigem && hidhide;

            var color = allOk
                ? System.Windows.Media.Color.FromRgb(16, 185, 129)    // grün
                : System.Windows.Media.Color.FromRgb(239, 68, 68);    // rot
            DriversStatusDotHeader.Fill = new System.Windows.Media.SolidColorBrush(color);

            string tooltip = allOk
                ? Lang.Drivers_HeaderTooltipOk
                : LanguageManager.Instance.Format("Drivers_HeaderTooltipMissing",
                    vigem ? Lang.Drivers_Installed : Lang.Drivers_Missing,
                    hidhide ? Lang.Drivers_Installed : Lang.Drivers_Missing);
            BtnDriversHeader.ToolTip = tooltip;
        }

        /// <summary>Klick auf das Treiber-Status-Licht: fehlende Treiber herunterladen
        /// und installieren (ViGEmBus + HidHide, je nachdem was fehlt).</summary>
        private async void BtnDriversHeader_Click(object sender, RoutedEventArgs e)
        {
            bool vigem = VigemXboxBridge.IsDriverInstalled();
            bool hidhide = DriverInstaller.IsDriverInstalled("hidhide");

            if (vigem && hidhide)
            {
                System.Windows.MessageBox.Show(Lang.Drivers_HeaderAllInstalled,
                    Lang.Drivers_CheckTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _driversInstallPending = true;
                var missing = new List<string>();
                if (!vigem) missing.Add(DriverInstaller.ViGEmBusFile);
                if (!hidhide) missing.Add(DriverInstaller.HidHideFile);

                // Vor dem Herunterladen einmal prüfen, ob die Setup-Datei evtl. schon
                // im Treiber-Ordner liegt (z.B. manuell dort abgelegt) – dann reicht
                // ein Direktstart ohne Download.
                var toInstall = new List<string>();
                var toDownload = new List<string>();
                foreach (var file in missing)
                {
                    var existing = DriverInstaller.Find(file);
                    if (existing != null)
                        toInstall.Add(existing);
                    else
                        toDownload.Add(file);
                }

                if (toDownload.Count > 0)
                {
                    var progress = new Progress<int>(p =>
                    {
                        if (DriversStatusDotHeader != null)
                            DriversStatusDotHeader.Fill = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(245, 158, 11)); // orange: Download läuft
                    });

                    foreach (var file in toDownload)
                    {
                        var path = await DriverInstaller.EnsureDownloadedAsync(file, progress);
                        if (path == null)
                        {
                            System.Windows.MessageBox.Show(
                                LanguageManager.Instance.Format("Drivers_HeaderDownloadFailed", file),
                                Lang.Drivers_CheckTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        toInstall.Add(path);
                    }
                }

                // Installer starten (UAC) – einer nach dem anderen.
                foreach (var setupPath in toInstall)
                {
                    DriverInstaller.LaunchInstaller(setupPath);
                }

                _driversInstallPending = false;
                UpdateDriversHeaderStatus();
            }
            catch (Exception ex)
            {
                _driversInstallPending = false;
                App.LogApp("Treiber-Installation Fehler: " + ex.Message);
            }
        }

        private void BtnCheckDrivers_Click(object sender, RoutedEventArgs e)
        {
            bool vigem = VigemXboxBridge.IsDriverInstalled();
            bool hidhide = DriverInstaller.IsDriverInstalled("hidhide");
            bool admin = InputSimulator.IsRunningAsAdmin();

            // Statusanzeigen im Tab auffrischen.
            UpdateVigemStatusMain();
            UpdateHidhideStatusMain();
            UpdateDriversHeaderStatus();

            string msg = LanguageManager.Instance.Format("Drivers_CheckResult",
                vigem ? LanguageManager.Instance.Drivers_Installed : LanguageManager.Instance.Drivers_Missing,
                hidhide ? LanguageManager.Instance.Drivers_Installed : LanguageManager.Instance.Drivers_Missing,
                admin ? LanguageManager.Instance.Drivers_AdminAvailable : LanguageManager.Instance.Drivers_AdminMissing);
            System.Windows.MessageBox.Show(msg,
                LanguageManager.Instance.Drivers_CheckTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateVigemStatusMain()
        {
            string status = ControllerManager.Instance.VigemStatus;
            TxtVigemStatusMain.Text = status;

            bool active = ControllerManager.Instance.VigemIsXboxActive;
            var color = active
                ? System.Windows.Media.Color.FromRgb(16, 185, 129)   // grün: verbunden
                : status.StartsWith("ViGEmBus-Treiber")
                    ? System.Windows.Media.Color.FromRgb(239, 68, 68) // rot: Treiber fehlt
                    : System.Windows.Media.Color.FromRgb(100, 116, 139); // grau: inaktiv

            VigemStatusDotMain.Fill = new System.Windows.Media.SolidColorBrush(color);
        }

        private void UpdateHidhideStatusMain()
        {
            bool installed = DriverInstaller.IsDriverInstalled("hidhide");
            if (_hidhideStatusOverride != "")
            {
                TxtHidhideStatusMain.Text = _hidhideStatusOverride;
                HidhideStatusDotMain.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                return;
            }
            if (installed && _hidhideAutoActive)
            {
                // Mit Spiegelung sehen Spiele nur die virtuelle Xbox 360, beim
                // Duplikat-Schalter bleibt XInput sichtbar – das soll der Text unterscheiden.
                TxtHidhideStatusMain.Text = LanguageManager.Instance.Hidhide_AutoActive;
                HidhideStatusDotMain.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                return;
            }
            TxtHidhideStatusMain.Text = installed
                ? LanguageManager.Instance.Hidhide_Installed
                : LanguageManager.Instance.Hidhide_Missing;
            HidhideStatusDotMain.Fill = new System.Windows.Media.SolidColorBrush(
                installed
                    ? System.Windows.Media.Color.FromRgb(16, 185, 129)
                    : System.Windows.Media.Color.FromRgb(100, 116, 139));
        }

        /// <summary>
        /// Wenn die Xbox-360-Spiegelung aktiv ist und TenzoraX NICHT als Administrator
        /// läuft, weist die App einmalig beim Start darauf hin – ohne Admin-Rechte
        /// funktionieren weder die HidHide-Automatik noch die Eingabe in Spiele, die
        /// mit erhöhten Rechten laufen (UIPI-Blockade).
        /// </summary>
        private void CheckAdminStartupHint()
        {
            if (InputSimulator.IsRunningAsAdmin()) return;
            if (!_settings.UseVigemXbox) return;

            // Beim Autostart (--minimized, PC-Boot) KEINEN Hinweis-Dialog zeigen:
            // Der Task-Scheduler startet die App bereits mit höchsten Rechten, und
            // ein Dialog beim Booten wäre genau das Aufblitzen, das der Nutzer nicht
            // will. Nur bei manuellem Start (ohne --minimized) hinweisen.
            if (Environment.GetCommandLineArgs().Contains("--minimized")) return;

            var result = System.Windows.MessageBox.Show(
                LanguageManager.Instance.Admin_StartupHint,
                LanguageManager.Instance.Admin_StartupTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                RestartAsAdmin();
        }

        private void RestartAsAdmin()
        {
            try
            {
                var restArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
                if (!restArgs.Contains("--admin-restart")) restArgs.Add("--admin-restart");
                var argStr = string.Join(" ", restArgs
                    .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = argStr
                };
                // Process.Start mit runas blockiert, bis der Nutzer die UAC-Abfrage
                // beantwortet. Erst danach: Mutex freigeben (sonst „abandoned“) und
                // diese Instanz beenden. Die neue Instanz wartet mit --admin-restart
                // kurz auf die Freigabe und übernimmt dann sauber.
                Process.Start(psi);
                App.ReleaseInstanceMutex();
                Environment.Exit(0);
            }
            catch
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.Instance.Settings_RestartFailed,
                    LanguageManager.Instance.Dialog_AutostartErrorTitle,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Soll-Zustand der HidHide-Versteckung, abgeleitet aus den aktiven Optionen.
        /// Liest den Gerätebaum – deshalb immer über RefreshHidHide im Hintergrund aufrufen.
        /// </summary>
        private List<string> ComputeDesiredHidden(string padInstanceId)
        {
            // Die Spiegelung versteckt nur dann etwas, wenn der virtuelle Controller
            // wirklich läuft – sonst verschwände das Pad, ohne dass Spiele Ersatz sehen.
            bool mirror = _settings.UseVigemXbox && ControllerManager.Instance.VigemIsXboxActive;
            if (!mirror) return new List<string>();
            if (string.IsNullOrWhiteSpace(padInstanceId)) return new List<string>();

            var group = TenzoraX.Detection.PhysicalDeviceResolver.ResolveForThisMachine(padInstanceId);
            return group;
        }

        /// <summary>
        /// Bringt HidHide auf den Zustand, der aus den aktiven Optionen folgt. Angewendet
        /// wird nur die Differenz – so kostet ein Umschalten genau einen erhöhten
        /// CLI-Aufruf (ein UAC-Dialog), und Verstecke, die beide Optionen gemeinsam
        /// brauchen, bleiben erhalten.
        ///
        ///   Spiegelung an → ALLE Schnittstellen des Pads (Spiele sehen nur die virtuelle Xbox 360)
        ///   beides aus    → alles freigeben, Cloaking aus
        /// </summary>
        private async void RefreshHidHide(string instanceId = "")
        {
            var active = GetActiveController();
            string padInstanceId = !string.IsNullOrWhiteSpace(instanceId)
                ? instanceId.Trim()
                : (active != null && !string.IsNullOrEmpty(active.InstanceId) && !IsLikelyVirtualXbox(active)
                    ? active.InstanceId
                    : "");

            List<string> desired;
            try
            {
                desired = await System.Threading.Tasks.Task.Run(() => ComputeDesiredHidden(padInstanceId));
            }
            catch
            {
                desired = new List<string>();
            }

            if (!IsLoaded) return; // Fenster wurde inzwischen geschlossen

            var toHide = desired.Where(id => !_hidhideHidden.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            var toShow = _hidhideHidden.Where(id => !desired.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            bool cloakOn = desired.Count > 0;

            // Nichts zu tun und der Zustand passt schon (z.B. Optionen unverändert)?
            // Dann ohne CLI-Aufruf (und ohne UAC) aussteigen.
            if (toHide.Count == 0 && toShow.Count == 0 && cloakOn == _hidhideAutoActive)
            {
                UpdateHidhideStatusMain();
                return;
            }

            _hidhideStatusOverride = "";
            UpdateHidhideStatusMain();

            bool ok;
            try
            {
                ok = await System.Threading.Tasks.Task.Run(
                    () => HidHideAutomation.ApplyDeviceState(toHide, toShow, cloakOn));
            }
            catch
            {
                ok = false;
            }

            if (!IsLoaded) return;

            if (!ok)
            {
                // Zustand ist unbekannt (CLI fehlt, keine Admin-Rechte, UAC abgebrochen) →
                // nichts als versteckt führen und den Grund anzeigen.
                _hidhideHidden.Clear();
                _hidhideStatusOverride = InputSimulator.IsRunningAsAdmin()
                    ? LanguageManager.Instance.Hidhide_AutoFailed
                    : LanguageManager.Instance.Hidhide_AutoNeedsAdmin;
                UpdateHidhideStatusMain();
                App.LogApp("HidHide-Zustand konnte nicht gesetzt werden (CLI fehlt, keine Admin-Rechte oder UAC abgebrochen).");
                return;
            }

            _hidhideHidden.Clear();
            _hidhideHidden.AddRange(desired);
            App.LogApp("HidHide-Zustand: " + desired.Count + " versteckt (Spiegelung=" + _settings.UseVigemXbox + ")"
                       + (desired.Count > 0 ? ": " + string.Join(", ", desired) : ""));
            UpdateHidhideStatusMain();

            // Sicherheitsnetz: nur wenn jetzt etwas versteckt ist, prüfen wir kurz danach,
            // ob TenzoraX das Pad (über die Ausnahmeliste) noch lesen kann.
            if (cloakOn && !string.IsNullOrWhiteSpace(padInstanceId))
                CheckHidHideStillSeesController(padInstanceId);

        }

        /// <summary>
        /// Start-Wiederherstellung: War die Spiegelung beim letzten Mal aktiv, wird das
        /// physische Gamepad automatisch wieder versteckt, sobald ein Controller da ist
        /// (nach Neustart/Update ist sonst nur die Spiegelung, aber nicht das Verstecken aktiv).
        /// </summary>
        /// <summary>
        /// Live-Eingabe-Anzeige im Hauptfenster: aktualisiert ~5×/Sekunde die
        /// Tasten-/Achsen-Werte des aktiven Controllers (liest nur, blockiert nie).
        /// </summary>
        private void StartLiveInputTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            timer.Tick += (s, e) =>
            {
                var cm = ControllerManager.Instance;
                string buttons = cm.LastPressedSummary;
                string axes = cm.LastAxesSummary;

                bool active = !string.IsNullOrEmpty(buttons)
                    || (cm.LastSourceInputAtUtc != DateTime.MinValue
                        && (DateTime.UtcNow - cm.LastSourceInputAtUtc).TotalMilliseconds < 500);

                TxtLiveButtons.Text = LanguageManager.Instance.Live_ButtonsLabel + " "
                    + (string.IsNullOrEmpty(buttons) ? "—" : buttons);
                TxtLiveAxes.Text = LanguageManager.Instance.Live_AxesLabel + " "
                    + (string.IsNullOrEmpty(axes) ? "—" : axes);

                // Grün = Eingabe kommt gerade an, grau = Leerlauf.
                var color = active
                    ? System.Windows.Media.Color.FromRgb(16, 185, 129)
                    : System.Windows.Media.Color.FromRgb(74, 74, 74);
                LiveActivityDot.Fill = new System.Windows.Media.SolidColorBrush(color);
                if (active)
                {
                    TxtLiveButtons.Foreground = System.Windows.Media.Brushes.White;
                    TxtLiveAxes.Foreground = System.Windows.Media.Brushes.White;
                }
                else
                {
                    TxtLiveButtons.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
                    TxtLiveAxes.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
                }
            };
            timer.Start();
        }

        private void StartHidHideRestoreTimer()
        {
            // Läuft, sobald die Spiegelung-Option HidHide braucht – der ViGEmBus-Treiber
            // wird zusätzlich gebraucht.
            if (!_settings.UseVigemXbox) return;
            if (!DriverInstaller.IsDriverInstalled("hidhide")) return;

            _hidhideRestoreTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _hidhideRestoreTimer.Tick += (s, e) =>
            {
                if (_hidhideAutoActive)
                {
                    _hidhideRestoreTimer.Stop();
                    return;
                }

                var active = GetActiveController();
                if (active == null) return; // noch kein Controller erkannt → weiter warten
                _hidhideRestoreTimer.Stop();
                if (string.IsNullOrEmpty(active.InstanceId) || IsLikelyVirtualXbox(active)) return;

                EnsureHidHideAfterRestart(active.InstanceId);
            };
            _hidhideRestoreTimer.Start();
        }

        /// <summary>
        /// Start-Wiederherstellung: Ist die KOMPLETTE Schnittstellen-Gruppe des Pads
        /// bereits versteckt (hat einen Neustart überlebt), ist nichts zu tun – kein
        /// unnötiger UAC-Prompt. Ein nur aktives Cloaking oder ein halb verstecktes
        /// Gerät (nur eine von zwei Schnittstellen) reicht NICHT – dann wird sauber
        /// nachgezogen. Der Lese-Check läuft im Hintergrund und braucht keine
        /// Administrator-Rechte.
        /// </summary>
        private async void EnsureHidHideAfterRestart(string instanceId)
        {
            List<string> expected;
            bool alreadyComplete;
            try
            {
                expected = await System.Threading.Tasks.Task.Run(() => ComputeDesiredHidden(instanceId));

                // "Schon alles versteckt" gilt nur, wenn zusätzlich das Cloaking aktiv
                // ist: Nach einem harten Beenden bleiben die Einträge in HidHides Liste
                // stehen, während Cloaking aus ist – dann ist das Pad in Wahrheit
                // sichtbar und der Zustand muss neu gesetzt werden.
                alreadyComplete = expected.Count > 0 &&
                    await System.Threading.Tasks.Task.Run(
                        () => HidHideAutomation.AreAllDevicesHidden(expected) && HidHideAutomation.IsCloakActive());
            }
            catch
            {
                expected = new List<string>();
                alreadyComplete = false;
            }

            if (!IsLoaded) return;

            if (alreadyComplete)
            {
                // Hat den Neustart überlebt → Zustand übernehmen, kein UAC-Dialog nötig.
                _hidhideHidden.Clear();
                _hidhideHidden.AddRange(expected);
                UpdateHidhideStatusMain();
                App.LogApp("HidHide-Zustand hat den Neustart überlebt: " + expected.Count + " Schnittstelle(n).");
                return;
            }

            RefreshHidHide(instanceId);
        }

        /// <summary>
        /// Sicherheitsnetz nach dem automatischen Verstecken: Falls HidHide das Gerät
        /// auch für TenzoraX (Whitelist) ausblendet und der Controller nicht mehr lesbar
        /// ist, wird das Verstecken automatisch wieder aufgehoben – sonst wäre der
        /// Controller „weg“, obwohl er verbunden ist.
        /// </summary>
        private async void CheckHidHideStillSeesController(string instanceId)
        {
            await System.Threading.Tasks.Task.Delay(2500);
            if (!IsLoaded) return; // Fenster wurde inzwischen geschlossen
            try
            {
                // Schreibweise der Instanz-ID ist je nach Quelle unterschiedlich (siehe
                // DeviceHelpers.NormalizeInstanceKey) – hier zählt nur: ist das Gerät noch da?
                bool stillThere = ControllerManager.Instance.ConnectedControllers
                    .Any(c => string.Equals(c.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
                if (!stillThere && _hidhideAutoActive)
                {
                    var targets = _hidhideHidden.ToList();
                    _hidhideHidden.Clear();
                    UpdateHidhideStatusMain();
                    App.LogApp("HidHide-Sicherheitsnetz: Controller nach dem Verstecken nicht mehr lesbar – Verstecken wieder aufgehoben.");
                    // CLI im Hintergrund ausführen, damit die UI nicht blockiert.
                    _ = System.Threading.Tasks.Task.Run(() => HidHideAutomation.Unhide(targets));
                }
            }
            catch { }
        }

        /// <summary>Liefert den aktuell aktiven Controller (oder null, wenn keiner ausgewählt ist).</summary>
        private DetectedController? GetActiveController()
        {
            var list = ControllerManager.Instance.ConnectedControllers;
            int idx = ControllerManager.Instance.ActiveGamepadIndex;
            if (idx >= 0 && idx < list.Count) return list[idx];
            return null;
        }

        /// <summary>Erkennt den virtuellen ViGEm-Xbox-360-Controller (VID 045E / PID 028E), der niemals versteckt werden darf.</summary>
        private static bool IsLikelyVirtualXbox(DetectedController c)
        {
            return c.VidPid.IndexOf("045E", StringComparison.OrdinalIgnoreCase) >= 0
                && c.VidPid.IndexOf("028E", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BtnInstallHidhideMain_Click(object sender, RoutedEventArgs e)
        {
            UpdateHidhideStatusMain();   // ggf. Status nach Installation aktualisieren

            string? local = DriverInstaller.Find(DriverInstaller.HidHideFile);
            if (local != null)
            {
                bool ok = DriverInstaller.LaunchInstaller(local);
                if (!ok)
                    System.Windows.MessageBox.Show(
                        LanguageManager.Instance.Vigem_NoDriverMsg,
                        LanguageManager.Instance.Vigem_NoDriverTitle,
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/nefarius/HidHide")
                    {
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        /// <summary>
        /// Öffnet das Windows-Fenster "Game Controller" (joy.cpl) mit allen installierten
        /// Controllern. Wird über den Dateinamen gestartet (nicht über einen lokalisierten
        /// Menünamen) und funktioniert daher auf allen Windows-Versionen und in jeder Sprache.
        /// </summary>
        private void BtnOpenGameControllersMain_Click(object sender, RoutedEventArgs e)
        {
            if (System.Threading.Interlocked.Exchange(ref _gameControllersOpening, 1) == 1) return;

            // Das Panel wird im Hintergrund geöffnet: Es wartet ggf. auf eine ruhige
            // Geräteliste und prüft, ob das Fenster wirklich erscheint. Der UI-Thread
            // bleibt währenddessen bedienbar.
            DateTime lastChange = ControllerManager.Instance.LastControllerChangeUtc;
            System.Threading.Tasks.Task.Run(() =>
            {
                var result = GameControllersPanel.Open(lastChange);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _gameControllersOpening, 0);
                    if (!result.Success)
                    {
                        // Der konkrete Grund (z.B. DirectInput-Stau, Geräte werden gerade
                        // neu gestartet) hilft mehr als der allgemeine Text.
                        string message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? Lang.GamepadPanel_FailedMsg
                            : result.ErrorMessage;
                        System.Windows.MessageBox.Show(
                            this,
                            message,
                            Lang.GamepadPanel_FailedTitle,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }));
            });
        }

        private void TabsRightPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Beim Wechsel auf den Controller-Tab Status frisch anzeigen.
            if (TabsRightPanel.SelectedItem == TabController)
            {
                UpdateVigemStatusMain();
                UpdateHidhideStatusMain();
            }
        }

        private void ChkEditMode_Checked(object sender, RoutedEventArgs e)
        {
            _settings.EditMode = ChkEditMode.IsChecked == true;
            bool edit = _settings.EditMode;
            if (PanelEditModes != null)
                PanelEditModes.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            if (PanelControllerImageEdit != null)
                PanelControllerImageEdit.Visibility = edit && _settings.EditModeType == "image" ? Visibility.Visible : Visibility.Collapsed;
            if (!edit)
                DeselectButton();
            else if (_settings.EditModeType == "buttons")
                CanvasGamepad.Focus();
            UpdateButtonsAppearance();
            SaveSettings();
        }

        private void RadioEditMode_Checked(object sender, RoutedEventArgs e)
        {
            bool isImage = RadioEditImage.IsChecked == true;
            string newType = isImage ? "image" : "buttons";
            if (_settings.EditModeType == newType) return;

            _settings.EditModeType = newType;
            DeselectButton();
            if (PanelControllerImageEdit != null)
                PanelControllerImageEdit.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
            if (!isImage)
                CanvasGamepad.Focus();
            UpdateButtonsAppearance();
            SaveSettings();
        }

        private void BtnResetPositions_Click(object sender, RoutedEventArgs e)
        {
            ResetPositionsToDefaults();
        }

        private void SelectButton(System.Windows.Controls.Button btn)
        {
            DeselectButton();
            _selectedButton = btn;
            _selectedButtonKey = btn.Name.Replace("Btn_", "");
            btn.Foreground = System.Windows.Media.Brushes.White;
            btn.BorderBrush = System.Windows.Media.Brushes.Cyan;
            btn.BorderThickness = new Thickness(3);
            btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 120, 200));
        }

        private void DeselectButton()
        {
            if (_selectedButton != null)
            {
                _selectedButton.Foreground = System.Windows.Media.Brushes.White;
                _selectedButton.BorderThickness = new Thickness(2);
                _selectedButton.BorderBrush = System.Windows.Media.Brushes.Lime;
                _selectedButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 16, 44, 30));
                _selectedButton = null;
                _selectedButtonKey = null;
            }
        }

        private void CanvasGamepad_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_settings.EditMode && _settings.EditModeType == "buttons")
            {
                var pos = e.GetPosition(CanvasGamepad);
                bool hitButton = false;
                foreach (var pair in _gamepadButtonsUi)
                {
                    var btn = pair.Value;
                    FrameworkElement el = btn;
                    if (btn == Btn_L3) el = Canvas_L3;
                    else if (btn == Btn_R3) el = Canvas_R3;
                    double l = Canvas.GetLeft(el);
                    double t = Canvas.GetTop(el);
                    if (pos.X >= l && pos.X <= l + el.ActualWidth &&
                        pos.Y >= t && pos.Y <= t + el.ActualHeight)
                    {
                        hitButton = true;
                        break;
                    }
                }
                if (!hitButton)
                    DeselectButton();
            }
        }

        private void CanvasGamepad_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DeselectButton();
                e.Handled = true;
            }
        }

        private void BtnChangeControllerImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Lang.ImageFilter,
                Title = Lang.ImagePickerTitle
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _settings.ControllerImagePath = openFileDialog.FileName;
                SaveSettings();
                LoadControllerBackground();
            }
        }


        private void BtnResetControllerImage_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(Lang.Dialog_ResetImageMsg, Lang.Dialog_ResetImageTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _settings.ControllerImagePath = "";
                _settings.ControllerScale = 1.0;
                _settings.HasCustomPosition = false;
                LoadControllerBackground();
                SaveSettings();
            }
        }


        #endregion

        #region Update Check

        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            var info = await UpdateManager.CheckForUpdate();
            if (info == null) return;

            UpdateDialog? dlg = null;
            await Dispatcher.InvokeAsync(() =>
            {
                dlg = UpdateDialog.ShowUpdate(this, info);
            });

            if (dlg?.DownloadedExePath != null && System.IO.File.Exists(dlg.DownloadedExePath))
            {
                try
                {
                    UpdateManager.InstallUpdate(dlg.DownloadedExePath);
                }
                catch { }
            }
        }

        #endregion

        #region System Tray Icon & Window Lifecycle

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = Lang.Title_Window,
                Visible = true
            };
            _notifyIcon.Icon = CreateControllerIcon();
            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
            UpdateTrayMenu();
        }

        private void UpdateTrayMenu()
        {
            if (_notifyIcon == null) return;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            ApplyDarkTheme(contextMenu);

            var openItem = new System.Windows.Forms.ToolStripMenuItem(Lang.Tray_Open, null, (s, e) => RestoreFromTray())
            {
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
            };
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var profilesItem = new System.Windows.Forms.ToolStripMenuItem(Lang.Tray_SwitchProfile);
            var profileNames = ProfileManager.Instance.GetProfileNames();
            foreach (var p in profileNames)
            {
                var profileSubItem = new System.Windows.Forms.ToolStripMenuItem(p, null, (s, e) =>
                {
                    SelectProfile(p);
                    UpdateTrayMenu();
                });
                if (p == ProfileManager.Instance.ActiveProfile.Name)
                    profileSubItem.Checked = true;
                profilesItem.DropDownItems.Add(profileSubItem);
            }
            profilesItem.DropDownOpening += (s, e) =>
            {
                if (profilesItem.DropDown is System.Windows.Forms.ToolStripDropDownMenu sub)
                    ApplyDarkTheme(sub);
            };
            contextMenu.Items.Add(profilesItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var pauseItem = new System.Windows.Forms.ToolStripMenuItem(
                ProfileManager.IsPaused ? Lang.Tray_Resume : Lang.Tray_Pause,
                null,
                (s, e) => TogglePause())
            {
                Checked = ProfileManager.IsPaused
            };
            contextMenu.Items.Add(pauseItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem(Lang.Tray_Exit, null, (s, e) =>
            {
                _isExplicitClose = true;
                Close();
            });
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private static void ApplyDarkTheme(System.Windows.Forms.ToolStripDropDownMenu menu)
        {
            menu.BackColor = System.Drawing.Color.FromArgb(30, 30, 40);
            menu.ForeColor = System.Drawing.Color.White;
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkColorTable());
        }

        private class DarkColorTable : System.Windows.Forms.ProfessionalColorTable
        {
            public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(30, 30, 40);
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(60, 60, 80);
            public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(60, 60, 80);
            public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(60, 60, 80);
            public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(50, 50, 70);
            public override System.Drawing.Color MenuItemPressedGradientBegin => System.Drawing.Color.FromArgb(60, 60, 80);
            public override System.Drawing.Color MenuItemPressedGradientEnd => System.Drawing.Color.FromArgb(50, 50, 70);
            public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(30, 30, 40);
            public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(30, 30, 40);
            public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(30, 30, 40);
            public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(60, 60, 70);
            public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(30, 30, 40);
        }

        private void StopBackgroundOnClose()
        {
            // Im Schließen-Pfad keine Ausnahme riskieren: der Effekt ist nur Kosmetik.
            try { StopBackgroundAnimation(); } catch { }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitClose && _settings.MinimizeToTray)
            {
                SaveWindowPosition();
                e.Cancel = true;
                HideToTray(false);
                return;
            }

            // 0. HidHide-Automatik: Verstecken beim Beenden wieder aufheben, damit
            //    das physische Gamepad danach überall normal funktioniert.
            if (_hidhideAutoActive)
            {
                var targets = _hidhideHidden.ToList();
                if (InputSimulator.IsRunningAsAdmin())
                {
                    // Als Admin: synchron und schnell, kein UAC nötig.
                    try { HidHideAutomation.Unhide(targets); } catch { }
                }
                else
                {
                    // Ohne Admin: Aufheben anstoßen, ohne zu warten – der UAC-Dialog
                    // kann erscheinen, während TenzoraX bereits schließt. Der erhöhte
                    // cmd-Prozess arbeitet unabhängig weiter; bricht der Nutzer die
                    // Abfrage ab, bleibt das Verstecken bestehen (per HidHide-Client
                    // oder beim nächsten Umschalten aufhebbar).
                    try
                    {
                        _ = System.Threading.Tasks.Task.Run(() => HidHideAutomation.Unhide(targets));
                    }
                    catch { }
                }
                _hidhideHidden.Clear();
                _hidhideStatusOverride = "";
            }

            // 1. Stop controller polling
            try { ControllerManager.Instance.Shutdown(); } catch { }

            // 2. Stop driver status timer
            try { _driversStatusTimer?.Stop(); _driversStatusTimer = null; } catch { }

            // 3. Save window position, size and settings
            SaveWindowPosition();
            _settings.NotificationPositionX = NotificationManager.SavedPosX;
            _settings.NotificationPositionY = NotificationManager.SavedPosY;
            _settings.NotificationWidth = NotificationManager.NotificationWidth;
            SaveSettings();

            // 4. Dispose tray icon - hide first, then clean up
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Icon = null;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch { }

            // 5. Dispose cached controller icon (SystemIcons.Application gehört
            //     dem System – die darf nicht disposet werden).
            if (_controllerIcon != null && !ReferenceEquals(_controllerIcon, SystemIcons.Application))
            {
                try { _controllerIcon.Dispose(); } catch { }
                _controllerIcon = null;
            }

            // 6. Mark clean shutdown – nothing needed (crash detection uses crash.log)
            App.LogApp("TenzoraX wird beendet");
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings, () => SaveSettings(), ApplyLanguage);
            win.ShowDialog();
            // Wiederauswahl-Modus ggf. in den Einstellungen geändert → sofort übernehmen.
            ControllerManager.Instance.ReselectMode = _settings.ControllerReselectMode;
            // Animierter Hintergrund kann dort ein-/ausgeschaltet worden sein.
            ApplyBackgroundSetting();
        }

        private System.Drawing.Icon CreateControllerIcon()
        {
            // Gecachtes Icon wiederverwenden: Jede Erzeugung kostet GDI-Handles,
            // die bei häufigen Aufrufen (bis 120×/s bei getrenntem Controller)
            // sonst geleakt würden → GDI+-Erschöpfung, „A generic error occurred in GDI+".
            if (_controllerIcon != null) return _controllerIcon;

            try
            {
                var uri = new Uri("pack://application:,,,/assets/icons/app.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        _controllerIcon = new System.Drawing.Icon(stream);
                        return _controllerIcon;
                    }
                }
            }
            catch { }

            // Fallback: draw a simple placeholder icon. Komplett abgesichert – auch
            // hier kann GDI+ bei Handle-Erschöpfung werfen (GetHicon).
            try
            {
                using (var bmp = new System.Drawing.Bitmap(32, 32))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 40, 50)), 4, 10, 24, 14);
                    g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 210, 255)), 12, 14, 8, 8);
                    var hIcon = bmp.GetHicon();
                    try
                    {
                        // Clone copies the handle before the bitmap disposes it; original is released explicitly
                        var clone = System.Drawing.Icon.FromHandle(hIcon).Clone();
                        _controllerIcon = (System.Drawing.Icon)clone;
                        return _controllerIcon;
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch { }

            // Letzter Fallback: System-Icon statt Crash (nie disposen!).
            _controllerIcon = SystemIcons.Application;
            return _controllerIcon;
        }

        private ImageSource CreateControllerIconSource()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/assets/icons/app.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    using (var icon = new System.Drawing.Icon(stream))
                    {
                        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { }

            // Fallback: draw a simple placeholder (komplett abgesichert)
            try
            {
                using (var bmp = new System.Drawing.Bitmap(32, 32))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 40, 50)), 4, 10, 24, 14);
                    g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 210, 255)), 12, 14, 8, 8);
                    var hIcon = bmp.GetHicon();
                    try
                    {
                        return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch { }

            // Letzter Fallback: System-Icon als BitmapSource
            try
            {
                return Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            return null!;
        }

        private void HideToTray(bool showTip)
        {
            // ShowInTaskbar aus, damit das versteckte Fenster NICHT als minimiertes
            // Icon in der Taskleiste hängen bleibt (nur im Tray sichtbar).
            try { ShowInTaskbar = false; } catch { }
            Hide();
            if (showTip && _notifyIcon != null)
                _notifyIcon.ShowBalloonTip(2000, "TenzoraX", Lang.Tray_BalloonText, System.Windows.Forms.ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            // Normalen Zustand wiederherstellen: Taskleisten-Eintrag aktivieren und
            // das Fenster per Win32 SW_RESTORE normal (nicht minimiert) zeigen.
            // WICHTIG: Die Reihenfolge Show() -> WindowState=Normal. Setzt man den
            // Zustand VOR Show(), bleibt ein beim Start minimiert erzeugtes Fenster
            // hartnäckig minimiert (WPF wendet den Zustand erst nach dem Anzeigen an).
            try { ShowInTaskbar = true; } catch { }
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            // Zusätzlich natives SW_RESTORE als Sicherheitsnetz (9 = SW_RESTORE).
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    ShowWindow(hwnd, 9);
            }
            catch { }
            Activate();
        }

        private static void ShowCrashDialog()
        {
            string crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TenzoraX", "Logs", "crash.log");
            string logsDir = Path.GetDirectoryName(crashLogPath) ?? "";

            var dialog = new Window
            {
                Title = "TenzoraX – Vorherige Sitzung nicht ordnungsgemäß beendet",
                Width = 480,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ShowInTaskbar = false
            };
            ThemeHelper.StyleWindow(dialog);

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "TenzoraX wurde beim letzten Mal nicht ordnungsgemäß beendet.\n\nEin detaillierter Fehlerbericht wird automatisch erstellt, wenn technische Fehlerinformationen verfügbar sind.",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttonPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };

            System.Windows.Controls.Button MakeButton(string text, double width, bool isDefault)
            {
                return new System.Windows.Controls.Button
                {
                    Content = text,
                    Width = width,
                    Height = 28,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 10, 0)
                };
            }

            var crashBtn = MakeButton("Crash-Log öffnen", 130, false);
            crashBtn.Click += (s, e) =>
            {
                try
                {
                    if (File.Exists(crashLogPath))
                        Process.Start(new ProcessStartInfo(crashLogPath) { UseShellExecute = true });
                    else if (Directory.Exists(logsDir))
                        Process.Start(new ProcessStartInfo(logsDir) { UseShellExecute = true });
                    else
                        System.Windows.MessageBox.Show("Es wurde kein detaillierter Fehlerbericht gefunden.", "Crash-Log", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            };
            buttonPanel.Children.Add(crashBtn);

            var okBtn = MakeButton("OK", 80, true);
            okBtn.Click += (s, e) => dialog.Close();
            okBtn.IsDefault = true;
            okBtn.Margin = new Thickness(0);
            buttonPanel.Children.Add(okBtn);

            stack.Children.Add(buttonPanel);
            dialog.Content = stack;
            dialog.ShowDialog();
        }

        #endregion
    }

    public class MappingPartTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? ButtonTemplate { get; set; }
        public DataTemplate? SeparatorTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is MappingPart part)
                return part.IsButton ? ButtonTemplate : SeparatorTemplate;
            return base.SelectTemplate(item, container);
        }
    }
}
