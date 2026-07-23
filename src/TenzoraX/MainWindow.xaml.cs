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
        public bool IsPaused { get; set; } = false;
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

        public bool BatteryEnabled { get; set; } = false;
        public double BatteryHours { get; set; } = 15;
        public double BatteryActiveMinutes { get; set; } = 0;
        public bool BatteryTrayEnabled { get; set; } = true;
        public bool BatteryAnimationEnabled { get; set; } = true;
        public bool BatterySettingsCollapsed { get; set; } = false;
        public string OutputMode { get; set; } = "VK";
        public bool SoundEnabled { get; set; } = true;
        public double SoundVolume { get; set; } = 0.5;
        public bool RunAsAdministrator { get; set; } = false;
        public bool HasCustomPosition { get; set; } = false;
    }

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private NotifyIcon? _notifyIcon;
        private bool _isExplicitClose = false;
        private AppSettings _settings = new();
        private LanguageManager Lang => LanguageManager.Instance;
        private string ConfigFilePath => Path.Combine(GetDocumentsPath(), ConfigFileName);
        private const string ConfigFileName = "config.json";

        private readonly InputCaptureEngine _capture = new();
        private readonly Dictionary<string, System.Windows.Controls.Button> _gamepadButtonsUi = new();
        private readonly HashSet<string> _physicallyHeldButtons = new();

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

        // Battery tracking
        private System.Windows.Threading.DispatcherTimer? _batteryTimer;
        private DateTime _lastBatteryInputTime = DateTime.MinValue;
        private bool _batteryWasActiveLastTick = false;
        private const double BatteryActiveTimeoutSeconds = 5;
        private int _batteryLastIconPercent = -1;
        private bool _batterySettingsCollapsed
        {
            get => _settings.BatterySettingsCollapsed;
            set => _settings.BatterySettingsCollapsed = value;
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    LogCrash("Unhandled", ex);
            };
            Dispatcher.UnhandledException += (s, e) =>
            {
                LogCrash("Dispatcher", e.Exception);
                e.Handled = true;
            };
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
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

            Icon = CreateControllerIconSource();
            InitializeGamepadButtonMap();
            LoadSettings();

            // Auto-elevate if setting is enabled (silent – no dialog)
            if (_settings.RunAsAdministrator && !InputSimulator.IsRunningAsAdmin())
            {
                try
                {
                    var cmdArgs = Environment.GetCommandLineArgs();
                    var argStr = cmdArgs.Length > 1
                        ? string.Join(" ", cmdArgs.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
                        : "";
                    var psi = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = argStr
                    };
                    Process.Start(psi);
                }
                catch { }
                Environment.Exit(0);
            }

            InitLanguage();
            DataContext = LanguageManager.Instance;
            LoadButtonPositions();
            UpdateButtonsAppearance();
            LoadControllerBackground();
            InitializeTrayIcon();
            InitializeDragHandlers();

            ControllerManager.Instance.GamepadsChanged += OnGamepadsChanged;
            ControllerManager.Instance.StateChanged += OnControllerStateChanged;
            ControllerManager.Instance.Initialize();
            RefreshGamepadsList();

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
            ChkPause.IsChecked = _settings.IsPaused;
            ChkAutostart.IsChecked = _settings.AutostartEnabled;
            ChkStartMinimized.IsChecked = _settings.StartMinimized;
            ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;

            // Output mode
            ComboOutputMode.Items.Add(Lang.OutputMode_VK);
            ComboOutputMode.Items.Add(Lang.OutputMode_ScanCode);
            ComboOutputMode.SelectedIndex = _settings.OutputMode == "ScanCode" ? 1 : 0;
            ApplyOutputMode();

            // Admin mode
            UpdateAdminUI();

            // Sound feedback
            ChkSound.IsChecked = _settings.SoundEnabled;
            SoundManager.Enabled = _settings.SoundEnabled;
            SliderSoundVolume.Value = _settings.SoundVolume * 100;
            SoundManager.Volume = _settings.SoundVolume;

            // Battery UI initialization
            ChkBatteryEnable.IsChecked = _settings.BatteryEnabled;
            PanelBatterySettings.Visibility = _settings.BatteryEnabled && !_batterySettingsCollapsed ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleBattery.Content = _batterySettingsCollapsed ? "▼" : "▲";
            TxtBatterySummary.Visibility = _settings.BatteryEnabled && _batterySettingsCollapsed ? Visibility.Visible : Visibility.Collapsed;
            TxtBatteryHours.Text = _settings.BatteryHours.ToString("0.#");
            UpdateBatteryCalculatedLabel();
            ChkBatteryTray.IsChecked = _settings.BatteryTrayEnabled;
            ChkBatteryAnimation.IsChecked = _settings.BatteryAnimationEnabled;
            InitBatteryTimer();
            ApplyBatteryToTray();
            UpdateBatterySummary();

            // Final position reapply & centering after layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterControllerIfDefault();
                ReapplyButtonPositions();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Re-center when window is resized (only if user hasn't manually positioned)
            CanvasGamepad.SizeChanged += CanvasGamepad_SizeChanged;

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

                if (newLeft < 0) newLeft = 0;
                if (newTop < 0) newTop = 0;
                if (newLeft > CanvasGamepad.ActualWidth - _draggedElement.ActualWidth)
                    newLeft = CanvasGamepad.ActualWidth - _draggedElement.ActualWidth;
                if (newTop > CanvasGamepad.ActualHeight - _draggedElement.ActualHeight)
                    newTop = CanvasGamepad.ActualHeight - _draggedElement.ActualHeight;

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

        private void CenterControllerIfDefault()
        {
            if (_settings.HasCustomPosition) return;

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
            if (_settings.HasCustomPosition) return;
            CenterControllerIfDefault();
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
            Dispatcher.Invoke(RefreshGamepadsList);
        }

        private void RefreshGamepadsList()
        {
            ComboGamepads.Items.Clear();
            var controllers = ControllerManager.Instance.ConnectedControllerNames;
            if (controllers.Count == 0)
            {
                ComboGamepads.Items.Add(Lang.Combo_NoController);
                ComboGamepads.SelectedIndex = 0;
                TxtStatus.Text = Lang.Status_Disconnected;
                TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
                TxtBattery.Text = Lang.Battery_NA;
            }
            else
            {
                foreach (var c in controllers)
                    ComboGamepads.Items.Add(c);
                ComboGamepads.SelectedIndex = ControllerManager.Instance.ActiveGamepadIndex >= 0 ? ControllerManager.Instance.ActiveGamepadIndex : 0;
                TxtStatus.Text = Lang.Status_Connected;
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
        }

        private void ComboGamepads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboGamepads.SelectedIndex >= 0 && ControllerManager.Instance.ConnectedControllerNames.Count > 0)
                ControllerManager.Instance.ActiveGamepadIndex = ComboGamepads.SelectedIndex;
        }

        private void OnControllerStateChanged(object? sender, ControllerStateEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Track battery input time
                if (_settings.BatteryEnabled && e.ControllerName != "None" && e.PressedButtons.Count > 0)
                    _lastBatteryInputTime = DateTime.UtcNow;

                if (e.ControllerName == "None")
                {
                    TxtStatus.Text = Lang.Status_Disconnected;
                    TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
                    TxtBattery.Text = Lang.Battery_NA;
                }
                else
                {
                    TxtStatus.Text = Lang.Status_Connected;
                    TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                }

                // Batterie-Status immer aktualisieren (auch im Pause-Modus)
                TxtBattery.Text = e.BatteryInfo;
                UpdateBatteryBar(e.BatteryInfo);

                // Wenn pausiert, keine Button-Farben und keine Eingaben verarbeiten
                if (_settings.IsPaused)
                {
                    // Trotzdem die Stick-Positionen aktualisieren für visuelles Feedback
                    Canvas.SetLeft(Btn_L3, 12 + (e.LeftStickX * 12));
                    Canvas.SetTop(Btn_L3, 12 + (e.LeftStickY * -12));
                    Canvas.SetLeft(Btn_R3, 12 + (e.RightStickX * 12));
                    Canvas.SetTop(Btn_R3, 12 + (e.RightStickY * -12));
                    
                    // Gespeicherte Mappings funktionieren immer
                    ProfileManager.Instance.ProcessControllerInput(e.PressedButtons);
                    return;
                }

                foreach (var pair in _gamepadButtonsUi)
                {
                    string btnKey = pair.Key;
                    System.Windows.Controls.Button btn = pair.Value;

                    if (e.PressedButtons.Contains(btnKey))
                    {
                        btn.Background = System.Windows.Media.Brushes.Cyan;
                        btn.BorderBrush = System.Windows.Media.Brushes.White;
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
                        if (btnKey == "A") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 44, 30));
                        else if (btnKey == "B") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 20, 24));
                        else if (btnKey == "X") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 32, 48));
                        else if (btnKey == "Y") btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 36, 21));
                        else btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 36));

                        btn.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 61));
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
                // Map -1..1 to 0..24 range (centered at 12)
                Canvas.SetLeft(Btn_L3, 12 + (e.LeftStickX * 12));
                Canvas.SetTop(Btn_L3, 12 + (e.LeftStickY * -12));
                Canvas.SetLeft(Btn_R3, 12 + (e.RightStickX * 12));
                Canvas.SetTop(Btn_R3, 12 + (e.RightStickY * -12));
            }

                // Gespeicherte Mappings funktionieren immer (auch im Pause-Modus)
                ProfileManager.Instance.ProcessControllerInput(e.PressedButtons);
            });
        }

        private void UpdateBatteryBar(string batteryInfo)
        {
            if (_settings.BatteryEnabled)
            {
                UpdateCalculatedBatteryBar();
                return;
            }

            // Fallback to old XInput battery bar
            double widthFraction;
            System.Windows.Media.Brush color;

            if (batteryInfo == "100%")
            {
                widthFraction = 1.0;
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            }
            else if (batteryInfo == "67%")
            {
                widthFraction = 0.67;
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            }
            else if (batteryInfo == "33%")
            {
                widthFraction = 0.33;
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60));
            }
            else if (batteryInfo == "0%")
            {
                widthFraction = 0.05;
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            }
            else
            {
                widthFraction = 0;
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));
            }

            BatteryLevel.Width = Math.Max(2, 22 * widthFraction);
            BatteryLevel.Background = color;
        }

        #region Battery Tracking

        private void InitBatteryTimer()
        {
            _batteryTimer?.Stop();
            if (!_settings.BatteryEnabled) return;

            _batteryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _batteryTimer.Tick += BatteryTimer_Tick;
            _batteryTimer.Start();
        }

        private void BatteryTimer_Tick(object? sender, EventArgs e)
        {
            if (!_settings.BatteryEnabled || _settings.IsPaused) return;

            bool isActive = (DateTime.UtcNow - _lastBatteryInputTime).TotalSeconds < BatteryActiveTimeoutSeconds;

            if (isActive)
            {
                _settings.BatteryActiveMinutes += 1.0 / 60.0;
                SaveSettings();
            }

            _batteryWasActiveLastTick = isActive;
            UpdateCalculatedBatteryBar();
            UpdateBatterySummary();
        }

        private double GetCalculatedBatteryPercent()
        {
            if (_settings.BatteryHours <= 0) return 100;
            double totalMinutes = _settings.BatteryHours * 60;
            double consumptionPerMinute = 100.0 / totalMinutes;
            double used = _settings.BatteryActiveMinutes * consumptionPerMinute;
            double percent = 100.0 - used;
            return Math.Max(0, Math.Min(100, percent));
        }

        private void UpdateCalculatedBatteryBar()
        {
            double percent = GetCalculatedBatteryPercent();
            string batteryInfo = $"{percent:F1}%";
            TxtBattery.Text = batteryInfo;

            double widthFraction = percent / 100.0;
            System.Windows.Media.Brush color;
            if (percent > 60)
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            else if (percent > 30)
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            else if (percent > 10)
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60));
            else
                color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));

            BatteryLevel.Width = Math.Max(2, 22 * widthFraction);
            BatteryLevel.Background = color;

            ApplyBatteryToTray();
        }

        private void UpdateBatteryCalculatedLabel()
        {
            double totalMinutes = _settings.BatteryHours * 60;
            double consumptionPerMinute = totalMinutes > 0 ? 100.0 / totalMinutes : 0;
            TxtBatteryCalculated.Text = Lang.Format("Battery_Calculated", (int)totalMinutes, consumptionPerMinute);
        }

        #endregion

        #region Battery UI Handlers

        private void ChkBatteryEnable_Changed(object sender, RoutedEventArgs e)
        {
            _settings.BatteryEnabled = ChkBatteryEnable.IsChecked == true;
            PanelBatterySettings.Visibility = _settings.BatteryEnabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateBatterySummary();
            SaveSettings();
            InitBatteryTimer();
            if (!_settings.BatteryEnabled)
                RefreshGamepadsList(); // restore original XInput battery reading
            else
            {
                _lastBatteryInputTime = DateTime.UtcNow;
                UpdateCalculatedBatteryBar();
            }
        }

        private void TxtBatteryHours_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyBatteryHours();
        }

        private void TxtBatteryHours_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyBatteryHours();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void ApplyBatteryHours()
        {
            if (double.TryParse(TxtBatteryHours.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double hours))
            {
                if (hours < 0.5) hours = 0.5;
                if (hours > 999) hours = 999;
                _settings.BatteryHours = hours;
                TxtBatteryHours.Text = hours.ToString("0.#");
                SaveSettings();
                UpdateBatteryCalculatedLabel();
                UpdateCalculatedBatteryBar();
            }
            else
            {
                TxtBatteryHours.Text = _settings.BatteryHours.ToString("0.#");
            }
        }

        private void ChkBatteryTray_Changed(object sender, RoutedEventArgs e)
        {
            _settings.BatteryTrayEnabled = ChkBatteryTray.IsChecked == true;
            SaveSettings();
            ApplyBatteryToTray();
        }

        private void ChkBatteryAnimation_Changed(object sender, RoutedEventArgs e)
        {
            _settings.BatteryAnimationEnabled = ChkBatteryAnimation.IsChecked == true;
            SaveSettings();
        }

        private void BtnResetBattery_Click(object sender, RoutedEventArgs e)
        {
            _settings.BatteryActiveMinutes = 0;
            _lastBatteryInputTime = DateTime.UtcNow;
            SaveSettings();
            UpdateCalculatedBatteryBar();
        }

        private void ApplyBatteryToTray()
        {
            if (_notifyIcon == null) return;

            if (!_settings.BatteryEnabled || !_settings.BatteryTrayEnabled)
            {
                _notifyIcon.Icon = CreateControllerIcon();
                _notifyIcon.Text = "TenzoraX";
                _batteryLastIconPercent = -1;
                return;
            }

            double percent = GetCalculatedBatteryPercent();
            int intPercent = (int)Math.Round(percent);

            // Build tooltip
            string tip = $"TenzoraX\n{this.Lang.Battery_Label} {percent:F1}%";
            if (_settings.IsPaused)
                tip += $"\n{this.Lang.Status_Paused}";
            else if ((DateTime.UtcNow - _lastBatteryInputTime).TotalSeconds < BatteryActiveTimeoutSeconds)
                tip += $"\n{(Lang.CurrentLang == "de" ? "Controller aktiv" : "Controller active")}";
            _notifyIcon.Text = tip;

            // Only recreate icon when integer percentage changes
            if (intPercent == _batteryLastIconPercent) return;
            _batteryLastIconPercent = intPercent;

            _notifyIcon.Icon = CreateBatteryIcon(percent);
        }

        private static void DrawRoundedRectangle(System.Drawing.Graphics g, System.Drawing.Pen pen, float x, float y, float w, float h, float r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }

        private System.Drawing.Icon CreateBatteryIcon(double percent)
        {
            var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                System.Drawing.Color color;
                if (percent > 60)
                    color = System.Drawing.Color.FromArgb(16, 185, 129);
                else if (percent > 30)
                    color = System.Drawing.Color.FromArgb(245, 158, 11);
                else
                    color = System.Drawing.Color.FromArgb(239, 68, 68);

                // Battery body
                float bodyX = 5, bodyY = 9, bodyW = 20, bodyH = 14;
                float cornerR = 3;
                using (var pen = new System.Drawing.Pen(color, 1.5f))
                    DrawRoundedRectangle(g, pen, bodyX, bodyY, bodyW, bodyH, cornerR);

                // Battery terminal (cap on top)
                float termX = bodyX + bodyW / 2 - 2, termY = bodyY - 3, termW = 4, termH = 3;
                using (var fillBrush = new System.Drawing.SolidBrush(color))
                    g.FillRectangle(fillBrush, termX, termY, termW, termH);

                // Inner fill
                float fillX = bodyX + 2, fillY = bodyY + 2;
                float fillMaxW = bodyW - 4;
                float fillH = bodyH - 4;
                float fillW = fillMaxW * (float)(percent / 100.0);
                if (fillW > 0)
                {
                    using (var fillBrush = new System.Drawing.SolidBrush(color))
                        g.FillRectangle(fillBrush, fillX, fillY, fillW, fillH);
                }
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private void BtnToggleBattery_Click(object sender, RoutedEventArgs e)
        {
            _batterySettingsCollapsed = !_batterySettingsCollapsed;
            PanelBatterySettings.Visibility = _batterySettingsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            BtnToggleBattery.Content = _batterySettingsCollapsed ? "▼" : "▲";
            TxtBatterySummary.Visibility = _batterySettingsCollapsed ? Visibility.Visible : Visibility.Collapsed;
            SaveSettings();
        }

        private void BtnConfirmBattery_Click(object sender, RoutedEventArgs e)
        {
            // Validate and save hours
            if (double.TryParse(TxtBatteryHours.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double hours))
            {
                if (hours < 0.5) hours = 0.5;
                if (hours > 999) hours = 999;
                _settings.BatteryHours = hours;
                TxtBatteryHours.Text = hours.ToString("0.#");
            }
            else
            {
                TxtBatteryHours.Text = _settings.BatteryHours.ToString("0.#");
            }

            // Reset battery to 100% and enable battery system
            _settings.BatteryActiveMinutes = 0;
            _lastBatteryInputTime = DateTime.UtcNow;
            _settings.BatteryEnabled = true;
            ChkBatteryEnable.IsChecked = true;
            PanelBatterySettings.Visibility = Visibility.Visible;

            SaveSettings();
            UpdateBatteryCalculatedLabel();
            UpdateCalculatedBatteryBar();
            InitBatteryTimer();

            // Collapse settings after confirm
            _batterySettingsCollapsed = true;
            PanelBatterySettings.Visibility = Visibility.Collapsed;
            UpdateBatterySummary();
        }

        private void UpdateBatterySummary()
        {
            if (!_settings.BatteryEnabled)
            {
                TxtBatterySummary.Visibility = Visibility.Collapsed;
                return;
            }

            double percent = GetCalculatedBatteryPercent();
            string status;
            if (_settings.IsPaused)
                status = Lang.CurrentLang == "de" ? "pausiert" : "paused";
            else if ((DateTime.UtcNow - _lastBatteryInputTime).TotalSeconds < BatteryActiveTimeoutSeconds)
                status = Lang.CurrentLang == "de" ? "aktiv" : "active";
            else
                status = Lang.CurrentLang == "de" ? "bereit" : "ready";

            TxtBatterySummary.Text = $"{Lang.Battery_Label} {percent:F1}% – {status}";
        }

        private void ApplyOutputMode()
        {
            string mode = _settings.OutputMode;
            InputSimulator.OutputMode = mode;

            if (!InputSimulator.IsRunningAsAdmin())
            {
                Debug.WriteLine("Warning: Not running as admin. SendInput may be blocked from reaching elevated applications (e.g. RTSS running as admin).");
            }
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

        private void ChkSound_Changed(object sender, RoutedEventArgs e)
        {
            _settings.SoundEnabled = ChkSound.IsChecked == true;
            SoundManager.Enabled = _settings.SoundEnabled;
            SaveSettings();
        }

        private void SliderSoundVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _settings.SoundVolume = SliderSoundVolume.Value / 100.0;
            SoundManager.Volume = _settings.SoundVolume;
            SaveSettings();
        }

        #endregion

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
            UpdateBatterySummary();
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

            var existing = ProfileManager.Instance.ActiveProfile.Mappings.FirstOrDefault(m => m.Combo.SequenceEqual(_capture.CurrentCombo));
            if (existing != null)
                existing.Action = new List<string>(_capture.CurrentAction);
            else
                ProfileManager.Instance.ActiveProfile.Mappings.Add(new Mapping { Combo = new List<string>(_capture.CurrentCombo), Action = new List<string>(_capture.CurrentAction) });

            ProfileManager.Instance.SaveActiveProfile();
            _capture.Reset();
            TxtSelectedCombo.Text = Lang.Mapping_ComboPlaceholder;
            TxtSelectedAction.Text = Lang.Mapping_ActionPlaceholder;
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
                ProfileManager.Instance.ActiveProfile.Mappings.Remove(mapping);
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

                    // Migration: existing config with a non-default ControllerLeft = user customized
                    if (!_settings.HasCustomPosition)
                    {
                        double left = _settings.ControllerLeft;
                        if (Math.Abs(left - 50) > 1 && Math.Abs(left) > 1)
                            _settings.HasCustomPosition = true;
                    }
                }
                catch { _settings = new AppSettings(); }
            }
            else
            {
                _settings = new AppSettings();
                SaveSettings();
            }

            if (_settings.RelativeButtonPositions.Count == 0)
            {
                SetDefaultRelativePositions();
                SaveSettings();
            }

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

        private void SaveSettings()
        {
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

            TxtAdminStatus.Text = InputSimulator.IsRunningAsAdmin() ? Lang.Edit_AdminElevated : Lang.Edit_AdminUser;

            if (!_capture.IsCapturing)
                BtnRecord.Content = Lang.Mapping_Record;
            else
                BtnRecord.Content = Lang.Mapping_StopRecord;
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

        private void UpdateAdminUI()
        {
            bool isAdmin = InputSimulator.IsRunningAsAdmin();
            TxtAdminStatus.Text = isAdmin ? Lang.Edit_AdminElevated : Lang.Edit_AdminUser;
            TxtAdminStatus.Foreground = isAdmin
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            ChkAdminMode.IsChecked = _settings.RunAsAdministrator;
        }

        private void ChkAdminMode_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = ChkAdminMode.IsChecked == true;
            _settings.RunAsAdministrator = newValue;
            SaveSettings();

            if (newValue && !InputSimulator.IsRunningAsAdmin())
            {
                var result = System.Windows.MessageBox.Show(Lang.Dialog_RestartAsAdminMsg, Lang.Dialog_RestartAsAdminTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var argStr = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)
                            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        var psi = new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath,
                            UseShellExecute = true,
                            Verb = "runas",
                            Arguments = argStr
                        };
                        Process.Start(psi);
                        Environment.Exit(0);
                    }
                    catch
                    {
                        System.Windows.MessageBox.Show("Failed to restart as administrator.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
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

        private void ChkPause_Changed(object sender, RoutedEventArgs e)
        {
            _settings.IsPaused = ChkPause.IsChecked == true;
            SaveSettings();
            if (_settings.IsPaused)
            {
                TxtStatus.Text = Lang.Status_Paused;
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                RefreshGamepadsList();
            }
            UpdateTrayMenu();
        }

        private void ChkAutostart_Changed(object sender, RoutedEventArgs e)
        {
            _settings.AutostartEnabled = ChkAutostart.IsChecked == true;
            SaveSettings();
            SetAutostartRegistry(_settings.AutostartEnabled);
        }

        private void ChkStartMinimized_Changed(object sender, RoutedEventArgs e)
        {
            _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
            SaveSettings();
        }

        private void ChkMinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            SaveSettings();
        }

        private void SetAutostartRegistry(bool enable)
        {
            try
            {
                string keyName = "TenzoraX";
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                        key.SetValue(keyName, $"\"{exePath}\" --minimized");
                    else
                        key.DeleteValue(keyName, false);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Lang.Format("Dialog_AutostartErrorMsg", ex.Message), Lang.Dialog_AutostartErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
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
            contextMenu.Items.Add(profilesItem);

            var pauseItem = new System.Windows.Forms.ToolStripMenuItem(Lang.Tray_Pause, null, (s, e) =>
            {
                ChkPause.IsChecked = !ChkPause.IsChecked;
            })
            {
                Checked = _settings.IsPaused
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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();

            if (!_isExplicitClose)
            {
                if (_settings.MinimizeToTray)
                {
                    e.Cancel = true;
                    HideToTray(false);
                }
                else
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                    }
                }
            }
            else
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
        }

        private System.Drawing.Icon CreateControllerIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/assets/icons/app.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        return new System.Drawing.Icon(stream);
                    }
                }
            }
            catch { }

            // Fallback: draw a simple placeholder icon
            var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 40, 50)), 4, 10, 24, 14);
                g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 210, 255)), 12, 14, 8, 8);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
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
                    {
                        var icon = new System.Drawing.Icon(stream);
                        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { }
            // Fallback: draw a simple placeholder
            var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 40, 50)), 4, 10, 24, 14);
                g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 210, 255)), 12, 14, 8, 8);
            }
            return Imaging.CreateBitmapSourceFromHIcon(bmp.GetHicon(), Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private void HideToTray(bool showTip)
        {
            Hide();
            if (showTip && _notifyIcon != null)
                _notifyIcon.ShowBalloonTip(2000, "TenzoraX", Lang.Tray_BalloonText, System.Windows.Forms.ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        #endregion
    }
}
