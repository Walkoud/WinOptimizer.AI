using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using System.Windows.Threading;
using System.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstanceMutex = new Mutex(true, "WinOptimizer.AI.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("WinOptimizer.AI is already running.", "WinOptimizer.AI", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Initialize notification manager
            NotificationManager.Initialize();

            try
            {
                var window = new MainWindow();
                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                WriteFatalLog(ex);
                MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
            }

            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteFatalLog(e.Exception);
            MessageBox.Show(e.Exception.ToString(), "Unhandled UI error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(-1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                WriteFatalLog(ex);
            }
        }

        private void WriteFatalLog(Exception ex)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch
            {
            }
        }
    }
}
