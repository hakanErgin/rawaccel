using System;
using System.ComponentModel;
using System.Windows;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CurveEditor.Controls;
using CurveEditor.Core.Interaction;
using CurveEditor.Services;
using CurveEditor.ViewModels;

namespace CurveEditor
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel vm;
        private readonly DispatcherTimer liveTimer;
        private readonly string screenshotPath;
        private RawMouseInput rawInput;
        private readonly PeakTracker peak = new PeakTracker(5);
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public MainWindow(string screenshotPath = null)
        {
            this.screenshotPath = screenshotPath;
            InitializeComponent();

            var settings = new SettingsService(AppDomain.CurrentDomain.BaseDirectory);
            vm = new MainViewModel(settings, Chart.View)
            {
                PromptForText = (title, prompt, initial) => InputDialog.Show(this, title, prompt, initial),
                NudgeStepProvider = () => Chart.NudgeStep(),
            };
            DataContext = vm;

            vm.CurveChanged += (s, e) => SyncChart();

            Chart.HandleSelected += (s, e) => vm.SelectedHandleId = e.Id;
            Chart.HandleDragStarted += (s, e) => vm.BeginDrag(e.Id);
            Chart.HandleMoved += (s, e) => vm.MoveHandle(e.Id, e.Target, e.IsNudge);
            Chart.HandleDragCompleted += (s, e) => vm.EndDrag(e.Id);
            Chart.AddPointRequested += (s, e) => vm.AddPoint(e.Point);
            Chart.RemoveRequested += (s, e) => vm.RemoveHandle(e.Id);
            Chart.FitRequested += (s, e) => vm.Fit();
            Chart.ResetViewRequested += (s, e) => vm.ResetView();
            Chart.ViewChanged += (s, e) =>
            {
                // handles without a natural position follow the view
                Chart.Handles = vm.Handles;
                vm.UpdateSelectionText();
            };

            liveTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            liveTimer.Tick += (s, e) =>
            {
                peak.Update(clock.Elapsed.TotalSeconds);
                Chart.PeakSpeed = peak.Peak;
                Chart.InvalidateVisual();
                // run until the 5 s peak window has settled
                if ((DateTime.UtcNow - Chart.LiveTime).TotalSeconds > 5.5) liveTimer.Stop();
            };

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (screenshotPath != null)
            {
                vm.LoadDemo();
                Chart.PeakSpeed = 38; // sample swipe so the screenshot shows the peak marker
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(SaveScreenshot));
                return;
            }

            try
            {
                vm.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read settings from the driver:\n" + ex.Message, "Raw Accel", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            vm.ResetView();
            Chart.Focus();
        }

        private void SaveScreenshot()
        {
            var root = (FrameworkElement)Content;
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(screenshotPath))
            {
                encoder.Save(file);
            }

            Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                rawInput = new RawMouseInput(source);
                rawInput.Moved += OnRawMouse;
            }
        }

        private void OnRawMouse(double dx, double dy, double timeMs)
        {
            var speed = vm.LiveInputSpeed(dx, dy, timeMs);
            if (speed == null) return;
            Chart.LiveSpeed = speed;
            peak.Add(clock.Elapsed.TotalSeconds, speed.Value);
            Chart.PeakSpeed = peak.Peak;
            Chart.LiveTime = DateTime.UtcNow;
            if (!liveTimer.IsEnabled) liveTimer.Start();
        }

        private void SyncChart()
        {
            Chart.Curve = vm.Curve;
            Chart.Ghost = vm.Ghost;
            Chart.Handles = vm.Handles;
            Chart.SelectedHandleId = vm.SelectedHandleId;
            Chart.Kind = vm.ChartKind;
            Chart.InvalidateVisual();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!vm.IsDirty || screenshotPath != null) return;
            var answer = MessageBox.Show(this, "You have changes that haven't been applied. Close anyway?", "Raw Accel",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            rawInput?.Dispose();
        }

        private void OnAddProfile(object sender, RoutedEventArgs e) => vm.AddProfile();

        private void OnRenameProfile(object sender, RoutedEventArgs e) => vm.RenameProfile();

        private void OnDevices(object sender, RoutedEventArgs e)
        {
            var dialog = new DevicesWindow(vm.Config, vm.Profiles) { Owner = this };
            if (dialog.ShowDialog() == true) vm.DevicesChanged();
        }

        private void OnShowPeakChanged(object sender, RoutedEventArgs e)
        {
            if (Chart == null) return;
            Chart.ShowPeak = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
            Chart.InvalidateVisual();
        }

        private void OnZoomToHandle(object sender, RoutedEventArgs e)
        {
            if (!Chart.ZoomToSelected()) vm.Status = "Select a handle first (click it), then zoom.";
            Chart.Focus();
        }

                private void OnKindSensitivity(object sender, RoutedEventArgs e) => SetKind(ChartKind.Sensitivity);

        private void OnKindVelocity(object sender, RoutedEventArgs e) => SetKind(ChartKind.Velocity);

        private void OnKindGain(object sender, RoutedEventArgs e) => SetKind(ChartKind.Gain);

        private void SetKind(ChartKind kind)
        {
            // radio buttons fire during InitializeComponent, before the view model exists
            if (vm == null) return;
            vm.ChartKind = kind;
        }
    }
}
