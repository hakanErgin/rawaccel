using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace CurveEditor
{
    public partial class App : Application
    {
        private Mutex mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            mutex = new Mutex(true, "RawAccelCurveEditor", out bool first);
            if (!first)
            {
                MessageBox.Show("The Raw Accel curve editor is already running.", "Raw Accel");
                Shutdown();
                return;
            }

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            DispatcherUnhandledException += (s, args) =>
            {
                Report(args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Report((Exception)args.ExceptionObject);

            try
            {
                VersionHelper.ValidOrThrow();
            }
            catch (Exception ex)
            {
                // wrong or missing driver: the same check the grapher does at startup
                MessageBox.Show(ex.Message, "Raw Accel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            mutex?.Dispose();
            base.OnExit(e);
        }

        private static void Report(Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "curve-editor-error.log"), ex.ToString());
            }
            catch (IOException)
            {
            }
            MessageBox.Show(ex.Message, "Raw Accel curve editor error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
