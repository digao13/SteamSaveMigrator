using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace SteamSaveMigrator.Wpf.Services;

/// <summary>
/// Gerencia a exibição e os eventos do ícone da aplicação na bandeja do sistema (ícones ocultos do Windows).
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _toggleWatcherItem;
    private Window? _mainWindow;
    private Action? _onToggleWatcher;
    private Action? _onSyncNow;
    private Action? _onExit;

    public bool IsInitialized => _notifyIcon != null;

    public void Initialize(
        Window mainWindow,
        Action onToggleWatcher,
        Action onSyncNow,
        Action onExit)
    {
        _mainWindow = mainWindow;
        _onToggleWatcher = onToggleWatcher;
        _onSyncNow = onSyncNow;
        _onExit = onExit;

        // Cria o menu de contexto da bandeja
        _contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("🎮 Abrir SteamSaveMigrator")
        {
            Font = new Font(_contextMenu.Font, System.Drawing.FontStyle.Bold)
        };
        openItem.Click += (s, e) => RestoreMainWindow();

        _statusItem = new ToolStripMenuItem("📊 Status: Inativo")
        {
            Enabled = false
        };

        _toggleWatcherItem = new ToolStripMenuItem("⚡ Iniciar Monitoramento");
        _toggleWatcherItem.Click += (s, e) => _onToggleWatcher?.Invoke();

        var syncItem = new ToolStripMenuItem("☁️ Atualizar Nuvem");
        syncItem.Click += (s, e) => _onSyncNow?.Invoke();

        var exitItem = new ToolStripMenuItem("❌ Sair do SteamSaveMigrator");
        exitItem.Click += (s, e) => _onExit?.Invoke();

        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(_toggleWatcherItem);
        _contextMenu.Items.Add(syncItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        // Ícone temático da aplicação para a bandeja do sistema
        Icon? appIcon = null;

        // 1. Tenta carregar do recurso empacotado da aplicação WPF
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/app_icon.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(iconUri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                appIcon = new Icon(stream);
            }
        }
        catch { }

        // 2. Tenta carregar do arquivo físico em Assets/app_icon.ico
        if (appIcon == null)
        {
            try
            {
                var localIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_icon.ico");
                if (File.Exists(localIconPath))
                {
                    appIcon = new Icon(localIconPath);
                }
            }
            catch { }
        }

        // 3. Fallback: extrai do executável
        if (appIcon == null)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    appIcon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch { }
        }

        appIcon ??= SystemIcons.Application;

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "SteamSaveMigrator - Monitor de Saves Steam",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // Duplo clique ou clique simples restaura a janela
        _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
    }

    public void UpdateWatcherStatus(bool isWatching)
    {
        if (_statusItem != null)
        {
            _statusItem.Text = isWatching
                ? "📊 Monitor: 🟢 Ativo (Vigiando Jogos)"
                : "📊 Monitor: ⚪ Pausado (Inativo)";
        }

        if (_toggleWatcherItem != null)
        {
            _toggleWatcherItem.Text = isWatching
                ? "⏸️ Pausar Monitoramento"
                : "▶️ Iniciar Monitoramento";
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Text = isWatching
                ? "SteamSaveMigrator - 🟢 Monitorando Jogos"
                : "SteamSaveMigrator - ⚪ Monitor Pausado";
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3500)
    {
        if (_notifyIcon != null && _notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(timeoutMs, title, message, icon);
        }
    }

    public void RestoreMainWindow()
    {
        if (_mainWindow == null) return;

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    public void HideToTray(bool showBalloon = false)
    {
        if (_mainWindow == null) return;

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _mainWindow.Hide();
            if (showBalloon)
            {
                ShowNotification(
                    "SteamSaveMigrator",
                    "O aplicativo continua em execução em segundo plano nos ícones ocultos.",
                    ToolTipIcon.Info);
            }
        });
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_contextMenu != null)
        {
            _contextMenu.Dispose();
            _contextMenu = null;
        }
    }
}
