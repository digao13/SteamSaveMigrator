using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace SteamSaveMigrator.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// Garante que apenas uma instância do aplicativo execute por vez (Single Instance),
/// restaurando e focando a janela principal caso o usuário execute o programa novamente.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "Global\\SteamSaveMigrator_SingleInstance_Mutex";
    private const string EventName = "Global\\SteamSaveMigrator_ShowInstance_Event";

    private static Mutex? _mutex;
    private static EventWaitHandle? _eventWaitHandle;
    private static RegisteredWaitHandle? _registeredWait;

    public static bool StartMinimized { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Fallback caso outro usuário do Windows na mesma máquina tenha aberto com restrição
            _mutex = new Mutex(true, "Local\\SteamSaveMigrator_SingleInstance_Mutex", out createdNew);
        }

        if (!createdNew)
        {
            // Já existe uma instância rodando.
            // Sinaliza a instância existente para restaurar a janela principal.
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
                {
                    existingEvent.Set();
                    existingEvent.Dispose();
                }
            }
            catch { }

            // Encerra imediatamente esta segunda instância
            Shutdown();
            return;
        }

        // Instância primária: cria o EventWaitHandle para escutar solicitações de novas instâncias
        try
        {
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _eventWaitHandle,
                (state, timedOut) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow mw)
                        {
                            mw.RestoreFromSystemTray();
                        }
                    }));
                },
                null,
                -1,
                false);
        }
        catch { }

        base.OnStartup(e);

        // Inicia a Steam junto com o SteamSaveMigrator caso configurado (padrão ativado)
        try
        {
            var userSettings = Core.Services.AppSettingsService.LoadSettings();
            if (userSettings.OpenSteamOnStartup)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Core.Detectors.SteamDetector.LaunchSteamIfNotRunning();
                    }
                    catch { }
                });
            }
        }
        catch { }

        if (e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "-m", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase)))
        {
            StartMinimized = true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _registeredWait?.Unregister(null);
            _eventWaitHandle?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }

        base.OnExit(e);
    }
}



