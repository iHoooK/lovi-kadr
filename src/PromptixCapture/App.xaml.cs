using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PromptixCapture;

public partial class App : Application
{
    private const string MutexName = "Local\\LoviKadr.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private AppController? _controller;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 2 && e.Args[0] == "--self-test")
        {
            Shutdown(Services.WpfSelfTest.Run(Path.GetFullPath(e.Args[1])));
            return;
        }

        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Helpers.NativeMethods.BroadcastShowSettingsMessage();
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _controller = new AppController();
        _controller.Initialize(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Services.AppLog.Error("UI exception", e.Exception);
        MessageBox.Show(
            "Произошла ошибка. Подробности сохранены в локальном журнале.\n\n" + e.Exception.Message,
            "ЛовиКадр",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Services.AppLog.Error("Unhandled exception", exception);
    }

    public static string DisplayVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}
