using System;
using System.ComponentModel;
using System.Windows;
using SteamSaveMigrator.Wpf.Services;
using SteamSaveMigrator.Wpf.ViewModels;

namespace SteamSaveMigrator.Wpf;

public partial class MainWindow : Window
{
    private readonly TrayIconService _trayService = new();
    private bool _isExplicitExit = false;
    private bool _hasShownMinimizeBalloon = false;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Inicializa serviço de bandeja do sistema (ícones ocultos)
            _trayService.Initialize(
                mainWindow: this,
                onToggleWatcher: () => vm.ToggleGameWatcherCommand.Execute(null),
                onSyncNow: () => vm.RefreshCloudBackupsCommand.Execute(null),
                onExit: ExitApplicationCompletely
            );

            // Conecta eventos do ViewModel com a bandeja
            vm.NotificationRequested += (title, message, isSuccess) =>
            {
                var icon = isSuccess ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning;
                _trayService.ShowNotification(title, message, icon);
            };

            vm.WatcherStatusChanged += (isWatching) =>
            {
                _trayService.UpdateWatcherStatus(isWatching);
            };

            await vm.InitializeAsync();
            _trayService.UpdateWatcherStatus(vm.IsGameWatcherActive);

            // Se o aplicativo foi iniciado com o parâmetro --minimized (inicialização com o Windows/Steam)
            if (App.StartMinimized)
            {
                HideToSystemTray(showBalloon: true);
            }
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            if (DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
            {
                HideToSystemTray(showBalloon: !_hasShownMinimizeBalloon);
                _hasShownMinimizeBalloon = true;
            }
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExplicitExit)
        {
            _trayService.Dispose();
            return;
        }

        if (DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToSystemTray(showBalloon: !_hasShownMinimizeBalloon);
            _hasShownMinimizeBalloon = true;
        }
        else
        {
            _trayService.Dispose();
        }
    }

    public void HideToSystemTray(bool showBalloon = false)
    {
        Hide();
        ShowInTaskbar = false;

        if (showBalloon)
        {
            _trayService.ShowNotification(
                "SteamSaveMigrator",
                "O aplicativo está rodando em segundo plano nos ícones ocultos e monitorando seus jogos.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    public void RestoreFromSystemTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void ExitApplicationCompletely()
    {
        _isExplicitExit = true;
        _trayService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}