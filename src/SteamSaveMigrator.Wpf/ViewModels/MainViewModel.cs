using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using SteamSaveMigrator.Core;
using SteamSaveMigrator.Core.Detectors;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using SteamSaveMigrator.Core.Services;
using SteamSaveMigrator.Core.Utils;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SteamSaveMigrator.Wpf.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly SteamSaveMigratorEngine _engine = new();

    // VersÃ£o da AplicaÃ§Ã£o
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.3.1";
    public string AppVersionDisplay => $"v{AppVersion}";
    public string WindowTitle => $"SteamSave Migrator {AppVersionDisplay} â€¢ Migrador, Conquistas e Conversor de Saves Steam";
    public string FooterStatusDisplay => $"SteamSaveMigrator {AppVersionDisplay} â€¢ Backup em Lote, Conquistas & RestauraÃ§Ã£o AutomÃ¡tica";

    // Estados gerais
    private bool _isBusy;
    private string _statusMessage = "Pronto";
    private int _selectedTabIndex = 0;

    // Steam
    private SteamInstallationInfo _steamInfo = new();
    private string _steamStatusText = "Detectando Steam...";

    // Jogos
    private ObservableCollection<SteamGame> _games = new();
    private ICollectionView? _filteredGames;
    private string _searchText = string.Empty;
    private SteamGame? _selectedGame;

    // Backup Individual
    private SteamGame? _backupGame;
    private ObservableCollection<GameSaveLocation> _backupLocations = new();
    private string _backupZipPath = string.Empty;
    private bool _isBackingUp;
    private double _backupProgress;
    private string _backupProgressText = string.Empty;
    private string _backupResultSummary = string.Empty;

    // Backup em Lote (MÃºltiplos Jogos de Uma Vez)
    private string _batchBackupOutputDir = Path.Combine(Environment.CurrentDirectory, "backups");
    private bool _isBatchBackingUp;
    private double _batchOverallProgress;
    private string _batchOverallProgressText = string.Empty;
    private string _batchCurrentGameText = string.Empty;
    private string _batchBackupSummary = string.Empty;

    // RestauraÃ§Ã£o AutomÃ¡tica (Zero ComplicaÃ§Ã£o)
    private string _restoreZipPath = string.Empty;
    private ObservableCollection<string> _restoreZipQueue = new();
    private BackupManifest? _restoreManifest;
    private string _restoreSourceUser = string.Empty;
    private string _restoreSourceProfile = string.Empty;
    private string _restoreTargetUser = Environment.UserName;
    private string _restoreTargetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _restoreDryRun = false;
    private bool _restoreOverwrite = true;
    private bool _isRestoring;
    private double _restoreProgress;
    private string _restoreProgressText = string.Empty;
    private ObservableCollection<RestoredItemReport> _restoreReports = new();
    private string _restoreResultSummary = string.Empty;
    private string _autoMigrationDescription = string.Empty;
    private bool _isAutoDetected;

    // Perfil Steam para Origem e Destino
    private ObservableCollection<SteamUserAccount> _availableSteamAccounts = new();
    private SteamUserAccount? _selectedSourceSteamAccount;
    private SteamUserAccount? _selectedTargetSteamAccount;
    private bool _migrateSteamProfile = true;
    private string _restoreSourceSteamText = "NÃ£o identificado";
    private string _restoreSourceSteamDetails = string.Empty;
    private bool _hasSourceSteamAccount;

    // Perfil Windows para Origem e Destino
    private ObservableCollection<WindowsProfileInfo> _availableWindowsProfiles = new();
    private WindowsProfileInfo? _selectedSourceWindowsProfile;
    private WindowsProfileInfo? _selectedTargetWindowsProfile;
    private bool _migrateWindowsUser = true;

    // Backups Locais (Lista inteligente equivalente Ã  Nuvem)
    private ObservableCollection<LocalBackupInfo> _localBackups = new();
    private ICollectionView? _filteredLocalBackups;
    private string _localSearchText = string.Empty;
    private LocalBackupInfo? _selectedLocalBackup;
    private bool _isLocalBackupsLoading;

    // Conquistas (Achievements)
    private ObservableCollection<GameAchievementInfo> _restoreAchievements = new();
    private bool _hasAchievementsInBackup;
    private string _restoreAchievementsSummary = string.Empty;

    // Google Drive & Nuvem
    private bool _hasGoogleCredentials;
    private bool _isConfiguringCredentials;
    private string _customClientId = string.Empty;
    private string _customClientSecret = string.Empty;
    private string _credentialsStatusText = string.Empty;
    private bool _isGoogleDriveConnected;
    private string _googleDriveUserEmail = string.Empty;
    private string _googleDriveStatusText = "NÃ£o conectado ao Google Drive";
    private ObservableCollection<CloudBackupInfo> _cloudBackups = new();
    private ICollectionView? _filteredCloudBackups;
    private string _cloudSearchText = string.Empty;
    private CloudBackupInfo? _selectedCloudBackup;
    private bool _isCloudLoading;
    private bool _isCloudRestoring;
    private double _cloudRestoreProgress;
    private string _cloudRestoreProgressText = string.Empty;
    private string _cloudResultSummary = string.Empty;

    // Game Watcher (Monitor AutomÃ¡tico de Fechamento de Jogos)
    private SteamGameWatcherService? _gameWatcher;
    private bool _isGameWatcherActive;
    private string _watcherStatusBadge = "Inativo (Parado)";
    private string _watcherLastEventText = "Nenhum evento registrado ainda";
    private ObservableCollection<string> _watcherLogs = new();
    private bool _watcherAutoBackup = true;
    private bool _watcherAutoUpload = true;
    private int _watcherPollInterval = 2;
    private int _watcherPostExitDelay = 3;

    // ConfiguraÃ§Ãµes de InicializaÃ§Ã£o e MinimizaÃ§Ã£o na Bandeja
    private bool _startWithWindows;
    private bool _minimizeToTrayOnClose = true;
    private bool _autoStartWatcherOnLaunch = true;
    private bool _openSteamOnStartup = true;
    private string _settingsSavedFeedback = string.Empty;

    // Eventos para o serviÃ§o de bandeja
    public event Action<string, string, bool>? NotificationRequested;
    public event Action<bool>? WatcherStatusChanged;

    public MainViewModel()
    {
        // Carrega configuraÃ§Ãµes persistidas do usuÃ¡rio (com suporte transparente entre perfis Windows)
        var userSettings = AppSettingsService.LoadSettings();
        if (!string.IsNullOrWhiteSpace(userSettings.CustomBackupDirectory))
        {
            _batchBackupOutputDir = userSettings.CustomBackupDirectory;
        }
        else
        {
            _batchBackupOutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamSavesBackup");
        }

        try
        {
            if (!Directory.Exists(_batchBackupOutputDir))
            {
                Directory.CreateDirectory(_batchBackupOutputDir);
            }
        }
        catch { }

        _autoStartWatcherOnLaunch = userSettings.AutoStartWatcherOnLaunch;
        _watcherAutoBackup = userSettings.WatcherAutoBackup;
        _watcherAutoUpload = userSettings.WatcherAutoUpload;
        _watcherPollInterval = userSettings.WatcherPollInterval > 0 ? userSettings.WatcherPollInterval : 2;
        _watcherPostExitDelay = userSettings.WatcherPostExitDelay > 0 ? userSettings.WatcherPostExitDelay : 3;
        _minimizeToTrayOnClose = userSettings.MinimizeToTray;
        _openSteamOnStartup = userSettings.OpenSteamOnStartup;

        // Comandos Gerais & Steam
        RefreshSteamCommand = new RelayCommand(async () => await InitializeAsync());
        SaveSettingsCommand = new RelayCommand(SaveSettingsExplicitly);

        // Comandos de Jogos & SeleÃ§Ã£o MÃºltipla
        ScanGameSavesCommand = new RelayCommand(async () => await ScanSavesForSelectedGameAsync(), () => SelectedGame != null && !IsBusy);
        SelectAllGamesCommand = new RelayCommand(() => SetSelectionForAll(true));
        DeselectAllGamesCommand = new RelayCommand(() => SetSelectionForAll(false));
        SelectGamesWithSavesCommand = new RelayCommand(async () => await SelectOnlyGamesWithSavesAsync());
        GoToBatchBackupTabCommand = new RelayCommand(() => SelectedTabIndex = 1);

        // Comandos de Backup
        BrowseBackupOutputCommand = new RelayCommand(BrowseBackupOutput);
        StartBackupCommand = new RelayCommand(async () => await StartBackupAsync(), () => BackupGame != null && !IsBackingUp && !string.IsNullOrWhiteSpace(BackupZipPath));
        BrowseBatchBackupOutputDirCommand = new RelayCommand(BrowseBatchBackupOutputDir);
        OpenBackupOutputDirCommand = new RelayCommand(OpenBackupOutputDir);
        ResetBackupOutputDirCommand = new RelayCommand(ResetBackupOutputDir);
        StartBatchBackupCommand = new RelayCommand(async () => await StartBatchBackupAsync(), () => SelectedGamesCount > 0 && !IsBatchBackingUp);

        // Comandos de RestauraÃ§Ã£o AutomÃ¡tica & Backups Locais (Inteligente e Equiparado Ã  Nuvem)
        RefreshLocalBackupsCommand = new RelayCommand(async () => await LoadLocalBackupsAsync(), () => !IsLocalBackupsLoading);
        ClearLocalSearchCommand = new RelayCommand(() => LocalSearchText = string.Empty);
        DeleteSelectedLocalBackupCommand = new RelayCommand(async () => await DeleteSelectedLocalBackupAsync(), () => SelectedLocalBackup != null && !IsLocalBackupsLoading);
        BrowseRestoreZipCommand = new RelayCommand(async () => await BrowseRestoreZipAsync());
        BrowseMultipleRestoreZipsCommand = new RelayCommand(async () => await BrowseMultipleRestoreZipsAsync());
        StartAutoRestoreCommand = new RelayCommand(async () => await StartAutoRestoreAsync(), () => (RestoreManifest != null || _restoreZipQueue.Count > 0 || SelectedLocalBackup != null) && !IsRestoring);
        StartCustomRestoreCommand = new RelayCommand(async () => await StartCustomRestoreAsync(), () => RestoreManifest != null && !IsRestoring);
        SyncAchievementsCommand = new RelayCommand(async () => await SyncAchievementsAsync(), () => HasAchievementsInBackup && SelectedTargetSteamAccount != null && !IsRestoring);

        // Comandos Google Drive & Nuvem
        ConnectGoogleDriveCommand = new RelayCommand(async () => await ConnectGoogleDriveAsync(), () => !IsBusy);
        CancelGoogleDriveAuthCommand = new RelayCommand(CancelGoogleDriveAuth);
        DisconnectGoogleDriveCommand = new RelayCommand(async () => await DisconnectGoogleDriveAsync(), () => IsGoogleDriveConnected && !IsBusy);
        ReconnectGoogleDriveCommand = new RelayCommand(async () => await ReconnectGoogleDriveAsync(), () => !IsBusy);
        RefreshCloudBackupsCommand = new RelayCommand(async () => await LoadCloudBackupsAsync(), () => IsGoogleDriveConnected && !IsCloudLoading);
        RestoreSelectedCloudBackupCommand = new RelayCommand(async () => await RestoreSelectedCloudBackupAsync(), () => SelectedCloudBackup != null && !IsCloudRestoring);
        DeleteSelectedCloudBackupCommand = new RelayCommand(async () => await DeleteSelectedCloudBackupAsync(), () => SelectedCloudBackup != null && !IsCloudLoading);
        ClearCloudSearchCommand = new RelayCommand(() => CloudSearchText = string.Empty);
        ToggleConfiguringCredentialsCommand = new RelayCommand(() => IsConfiguringCredentials = !IsConfiguringCredentials);
        ImportCredentialsJsonCommand = new RelayCommand(ImportCredentialsJson);
        SaveCustomCredentialsCommand = new RelayCommand(SaveCustomCredentials, () => !string.IsNullOrWhiteSpace(CustomClientId) && !string.IsNullOrWhiteSpace(CustomClientSecret));
        OpenGoogleConsoleCommand = new RelayCommand(OpenGoogleConsole);

        // Comandos Game Watcher (Monitor de Jogos)
        ToggleGameWatcherCommand = new RelayCommand(async () => await ToggleGameWatcherAsync());
        ClearWatcherLogsCommand = new RelayCommand(() => WatcherLogs.Clear());

        // Inicializa estado do registro do Windows
        _startWithWindows = WindowsStartupService.IsStartupEnabled();
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Detectando instalaÃ§Ã£o da Steam e jogos...";

        await Task.Run(() =>
        {
            _engine.InitializeSteam();
        });

        SteamInfo = _engine.SteamInfo;
        AvailableSteamAccounts = new ObservableCollection<SteamUserAccount>(SteamInfo.Accounts);

        var savedSettings = AppSettingsService.LoadSettings();

        // Restaura a conta Steam de destino preferida salva, ou usa a conta ativa
        if (savedSettings.LastTargetSteamId3.HasValue && SteamInfo.Accounts.Any(a => a.SteamId3 == savedSettings.LastTargetSteamId3.Value))
        {
            SelectedTargetSteamAccount = SteamInfo.Accounts.First(a => a.SteamId3 == savedSettings.LastTargetSteamId3.Value);
        }
        else
        {
            SelectedTargetSteamAccount = SteamInfo.ActiveAccount ?? SteamInfo.PrimaryAccount;
        }

        if (savedSettings.LastSourceSteamId3.HasValue && SteamInfo.Accounts.Any(a => a.SteamId3 == savedSettings.LastSourceSteamId3.Value))
        {
            SelectedSourceSteamAccount = SteamInfo.Accounts.First(a => a.SteamId3 == savedSettings.LastSourceSteamId3.Value);
        }
        else
        {
            SelectedSourceSteamAccount = SteamInfo.ActiveAccount ?? SteamInfo.PrimaryAccount;
        }

        if (SteamInfo.IsFound)
        {
            var activeText = SteamInfo.ActiveAccount != null
                ? $" â€¢ Conectado: {SteamInfo.ActiveAccount.PersonaName}"
                : string.Empty;
            SteamStatusText = $"Steam conectada ({SteamInfo.Libraries.Count} bibliotecas, {SteamInfo.Accounts.Count} contas{activeText})";
            await LoadGamesAsync();
        }
        else
        {
            SteamStatusText = "Steam nÃ£o detectada no registro padrÃ£o";
            Games.Clear();
        }

        // Detecta perfis Windows disponÃ­veis nesta mÃ¡quina
        var winProfiles = WindowsKnownFolders.GetAvailableUserProfiles();
        AvailableWindowsProfiles = new ObservableCollection<WindowsProfileInfo>(winProfiles);

        if (!string.IsNullOrWhiteSpace(savedSettings.LastTargetWindowsUsername) && winProfiles.Any(p => p.Username.Equals(savedSettings.LastTargetWindowsUsername, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedTargetWindowsProfile = winProfiles.First(p => p.Username.Equals(savedSettings.LastTargetWindowsUsername, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedTargetWindowsProfile = winProfiles.FirstOrDefault(p => p.IsCurrentAccount) ?? winProfiles.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(savedSettings.LastSourceWindowsUsername) && winProfiles.Any(p => p.Username.Equals(savedSettings.LastSourceWindowsUsername, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedSourceWindowsProfile = winProfiles.First(p => p.Username.Equals(savedSettings.LastSourceWindowsUsername, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedSourceWindowsProfile = winProfiles.FirstOrDefault(p => p.IsCurrentAccount) ?? winProfiles.FirstOrDefault();
        }

        // Carrega backups locais salvos na mÃ¡quina (exibiÃ§Ã£o inteligente na aba Restaurar)
        _ = LoadLocalBackupsAsync();

        // Verifica autenticaÃ§Ã£o prÃ©via no Google Drive em segundo plano
        _ = CheckGoogleDriveStatusAsync();

        // Inicia o aplicativo da Steam automaticamente caso configurado e nÃ£o esteja em execuÃ§Ã£o
        if (_openSteamOnStartup)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    SteamDetector.LaunchSteamIfNotRunning();
                }
                catch { }
            });
        }

        // Se configurado para iniciar o monitor automaticamente, inicia agora
        if (AutoStartWatcherOnLaunch && !IsGameWatcherActive)
        {
            _ = ToggleGameWatcherAsync();
        }

        IsBusy = false;
        StatusMessage = "Pronto";
    }

    public async Task LoadGamesAsync()
    {
        StatusMessage = "Carregando biblioteca de jogos...";

        List<SteamGame> detected = new();
        await Task.Run(() =>
        {
            detected = _engine.GetInstalledGames();
        });

        Games = new ObservableCollection<SteamGame>(detected);
        _filteredGames = CollectionViewSource.GetDefaultView(Games);
        _filteredGames.Filter = FilterGameItem;
        OnPropertyChanged(nameof(FilteredGames));
        OnPropertyChanged(nameof(TotalGamesCount));
        NotifySelectedGamesChanged();

        if (Games.Count > 0 && SelectedGame == null)
        {
            SelectedGame = Games[0];
        }

        StatusMessage = $"{Games.Count} jogos instalados detectados.";
    }

    private bool FilterGameItem(object obj)
    {
        if (obj is not SteamGame game) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var term = SearchText.Trim();
        return game.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               game.AppId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
               game.InstallDir.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public void SetSelectionForAll(bool select)
    {
        foreach (var g in Games)
        {
            g.IsSelected = select;
        }
        _filteredGames?.Refresh();
        NotifySelectedGamesChanged();
    }

    public async Task SelectOnlyGamesWithSavesAsync()
    {
        IsBusy = true;
        StatusMessage = "Identificando jogos com saves locais...";

        await Task.Run(() =>
        {
            foreach (var g in Games)
            {
                var scanned = _engine.ScanSavesForGame(g);
                g.IsSelected = scanned.HasSaves;
            }
        });

        _filteredGames?.Refresh();
        NotifySelectedGamesChanged();
        IsBusy = false;
        StatusMessage = $"{SelectedGamesCount} jogo(s) com saves selecionados.";
    }

    public void NotifySelectedGamesChanged()
    {
        OnPropertyChanged(nameof(SelectedGamesCount));
        OnPropertyChanged(nameof(SelectedGamesList));
        (StartBatchBackupCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public int SelectedGamesCount => Games.Count(g => g.IsSelected);

    public IEnumerable<SteamGame> SelectedGamesList => Games.Where(g => g.IsSelected);

    public async Task ScanSavesForSelectedGameAsync()
    {
        if (SelectedGame == null) return;

        IsBusy = true;
        StatusMessage = $"Escaneando saves de {SelectedGame.Name}...";

        var game = SelectedGame;
        SteamGame scanned = null!;

        await Task.Run(() =>
        {
            scanned = _engine.ScanSavesForGame(game);
        });

        BackupGame = scanned;
        BackupLocations = new ObservableCollection<GameSaveLocation>(scanned.DetectedSaveLocations);

        if (!Directory.Exists(_batchBackupOutputDir)) Directory.CreateDirectory(_batchBackupOutputDir);

        var sanitized = string.Join("_", scanned.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        BackupZipPath = Path.Combine(_batchBackupOutputDir, $"{sanitized}_{scanned.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        // Vai para a aba de Backup
        SelectedTabIndex = 1;

        IsBusy = false;
        StatusMessage = $"{scanned.TotalSaveFiles} arquivo(s) de save encontrados para {scanned.Name}.";
    }

    private void BrowseBackupOutput()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Salvar arquivo de backup dos saves",
            Filter = "Arquivo Zip do SteamSaveMigrator (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            FileName = Path.GetFileName(BackupZipPath),
            InitialDirectory = Path.GetDirectoryName(BackupZipPath) ?? _batchBackupOutputDir
        };

        if (sfd.ShowDialog() == true)
        {
            BackupZipPath = sfd.FileName;
        }
    }

    private void BrowseBatchBackupOutputDir()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecione a pasta onde os backups locais serÃ£o salvos",
                InitialDirectory = Directory.Exists(BatchBackupOutputDir) ? BatchBackupOutputDir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                BatchBackupOutputDir = dialog.FolderName;
                StatusMessage = $"Pasta de backups alterada para: {dialog.FolderName}";
            }
        }
        catch
        {
            var ofd = new OpenFileDialog
            {
                Title = "Selecione uma pasta de destino para os backups",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Selecionar esta pasta",
                InitialDirectory = Directory.Exists(BatchBackupOutputDir) ? BatchBackupOutputDir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (ofd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(ofd.FileName);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    BatchBackupOutputDir = dir;
                    StatusMessage = $"Pasta de backups alterada para: {dir}";
                }
            }
        }
    }

    private void OpenBackupOutputDir()
    {
        try
        {
            if (!Directory.Exists(BatchBackupOutputDir))
            {
                Directory.CreateDirectory(BatchBackupOutputDir);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{BatchBackupOutputDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao abrir pasta: {ex.Message}";
        }
    }

    private void ResetBackupOutputDir()
    {
        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamSavesBackup");
        BatchBackupOutputDir = defaultDir;
        if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);
        StatusMessage = $"Pasta de backups redefinida para o padrÃ£o: {defaultDir}";
    }

    private async Task StartBackupAsync()
    {
        if (BackupGame == null || string.IsNullOrWhiteSpace(BackupZipPath)) return;

        IsBackingUp = true;
        BackupProgress = 0;
        BackupProgressText = "Iniciando compactaÃ§Ã£o...";
        BackupResultSummary = string.Empty;

        var progress = new Progress<BackupProgressReport>(report =>
        {
            if (report.TotalFiles > 0)
            {
                BackupProgress = (double)report.CurrentFileIndex / report.TotalFiles * 100;
                BackupProgressText = $"Salvando ({report.CurrentFileIndex}/{report.TotalFiles}): {report.CurrentFileName}";
            }
        });

        uint? srcSteamId = SelectedSourceSteamAccount?.SteamId3;
        string? srcPersona = SelectedSourceSteamAccount?.PersonaName;
        string? srcProfile = SelectedSourceWindowsProfile?.ProfilePath;

        try
        {
            var zipResult = await Task.Run(async () =>
            {
                return await _engine.BackupGameAsync(
                    BackupGame,
                    BackupZipPath,
                    userProfile: srcProfile,
                    sourceSteamId3: srcSteamId,
                    sourceSteamPersonaName: srcPersona,
                    progress: progress);
            });

            var fileInfo = new FileInfo(zipResult);
            BackupProgress = 100;
            BackupProgressText = "Backup concluÃ­do com sucesso!";
            BackupResultSummary = $"âœ… Backup salvo em: {zipResult} ({ByteSizeFormatter.FormatBytes(fileInfo.Length)})";
            StatusMessage = "Backup concluÃ­do com sucesso!";

            _ = LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            BackupProgressText = "Falha no backup.";
            BackupResultSummary = $"âŒ Erro ao criar backup: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    private async Task StartBatchBackupAsync()
    {
        var selectedGames = SelectedGamesList.ToList();
        if (selectedGames.Count == 0) return;

        IsBatchBackingUp = true;
        BatchOverallProgress = 0;
        BatchOverallProgressText = $"Iniciando backup de {selectedGames.Count} jogo(s)...";
        BatchBackupSummary = string.Empty;

        var progress = new Progress<BatchBackupProgressReport>(report =>
        {
            BatchOverallProgress = report.OverallPercent;
            BatchOverallProgressText = $"Jogo {report.CurrentGameIndex} de {report.TotalGames} ({report.OverallPercent:0}% geral)";
            BatchCurrentGameText = $"[{report.CurrentGameName}] {report.CurrentFileName} ({report.CurrentFileIndex}/{report.CurrentGameTotalFiles})";
        });

        uint? srcSteamId = SelectedSourceSteamAccount?.SteamId3;
        string? srcPersona = SelectedSourceSteamAccount?.PersonaName;
        string? srcProfile = SelectedSourceWindowsProfile?.ProfilePath;

        try
        {
            var result = await Task.Run(async () =>
            {
                return await _engine.BackupMultipleGamesAsync(
                    selectedGames,
                    BatchBackupOutputDir,
                    userProfile: srcProfile,
                    sourceSteamId3: srcSteamId,
                    sourceSteamPersonaName: srcPersona,
                    progress: progress);
            });

            BatchOverallProgress = 100;
            BatchOverallProgressText = "Backup em lote finalizado!";
            BatchCurrentGameText = string.Empty;
            BatchBackupSummary = $"ðŸŽ‰ Sucesso! {result.SuccessCount} jogo(s) salvos em '{BatchBackupOutputDir}'. Total compactado: {ByteSizeFormatter.FormatBytes(result.TotalSizeBytesArchived)} ({result.TotalFilesArchived} arquivos).";
            StatusMessage = $"Backup de {result.SuccessCount} jogos concluÃ­do com sucesso!";

            _ = LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            BatchBackupSummary = $"âŒ Erro durante o backup em lote: {ex.Message}";
        }
        finally
        {
            IsBatchBackingUp = false;
        }
    }

    private async Task BrowseRestoreZipAsync()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecione o arquivo de backup (.zip)",
            Filter = "Arquivo de Backup (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            InitialDirectory = BatchBackupOutputDir,
            Multiselect = true
        };

        if (ofd.ShowDialog() == true)
        {
            if (ofd.FileNames.Length > 1)
            {
                RestoreZipQueue = new ObservableCollection<string>(ofd.FileNames);
                RestoreZipPath = $"{ofd.FileNames.Length} arquivos de backup selecionados para restauraÃ§Ã£o";
                await LoadRestoreManifestAsync(ofd.FileNames[0]);
                UpdateAutoMigrationDescription();
                IsAutoDetected = true;
            }
            else
            {
                RestoreZipQueue.Clear();
                RestoreZipPath = ofd.FileName;
                await LoadRestoreManifestAsync(ofd.FileName);
            }
        }
    }

    private async Task BrowseMultipleRestoreZipsAsync()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecione mÃºltiplos arquivos de backup (.zip) para restaurar em lote",
            Filter = "Arquivos de Backup (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            InitialDirectory = BatchBackupOutputDir,
            Multiselect = true
        };

        if (ofd.ShowDialog() == true && ofd.FileNames.Length > 0)
        {
            RestoreZipQueue = new ObservableCollection<string>(ofd.FileNames);
            RestoreZipPath = $"{ofd.FileNames.Length} arquivos de backup selecionados para restauraÃ§Ã£o";
            await LoadRestoreManifestAsync(ofd.FileNames[0]);
            UpdateAutoMigrationDescription();
            IsAutoDetected = true;
        }
    }

    public async Task LoadRestoreManifestAsync(string zipPath)
    {
        IsBusy = true;
        StatusMessage = "Lendo manifesto do backup...";
        RestoreManifest = null;
        RestoreReports.Clear();
        RestoreResultSummary = string.Empty;

        try
        {
            var manifest = await Task.Run(async () =>
            {
                return await _engine.ReadBackupManifestAsync(zipPath);
            });

            if (manifest != null)
            {
                RestoreManifest = manifest;
                RestoreSourceUser = manifest.SourceUsername;
                RestoreSourceProfile = manifest.SourceUserProfile;

                // Garante que o destino padrÃ£o seja o usuÃ¡rio atual logado
                RestoreTargetUser = Environment.UserName;
                RestoreTargetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                // Identifica dados de perfil da Steam de origem gravados no backup
                uint? srcSteamId3 = manifest.SourceSteamId3 ?? RestoreService.DetectSourceSteamId(manifest);
                ulong? srcSteamId64 = manifest.SourceSteamId64 ?? (srcSteamId3.HasValue ? PathVariableConverter.SteamId3ToSteamId64(srcSteamId3.Value) : null);
                string srcPersona = !string.IsNullOrWhiteSpace(manifest.SourceSteamPersonaName)
                    ? manifest.SourceSteamPersonaName
                    : (srcSteamId3.HasValue ? $"SteamID3: {srcSteamId3.Value}" : "Perfil Steam nÃ£o especificado");

                if (srcSteamId3.HasValue)
                {
                    HasSourceSteamAccount = true;
                    RestoreSourceSteamText = srcPersona;
                    RestoreSourceSteamDetails = $"SteamID3: {srcSteamId3.Value} â€¢ SteamID64: {srcSteamId64}";
                }
                else
                {
                    HasSourceSteamAccount = false;
                    RestoreSourceSteamText = "NÃ£o vinculado a perfil especÃ­fico";
                    RestoreSourceSteamDetails = "Saves em pastas gerais do sistema";
                }

                if (SelectedTargetSteamAccount == null && AvailableSteamAccounts.Count > 0)
                {
                    SelectedTargetSteamAccount = SteamInfo.ActiveAccount ?? AvailableSteamAccounts[0];
                }

                if (manifest.Achievements != null && manifest.Achievements.Count > 0)
                {
                    RestoreAchievements = new ObservableCollection<GameAchievementInfo>(manifest.Achievements);
                    HasAchievementsInBackup = true;
                    int unlocked = manifest.Achievements.Count(a => a.IsUnlocked);
                    RestoreAchievementsSummary = $"ðŸ† {unlocked} de {manifest.Achievements.Count} conquistas obtidas no backup (serÃ£o vinculadas Ã  conta de destino)";
                }
                else
                {
                    RestoreAchievements = new ObservableCollection<GameAchievementInfo>();
                    HasAchievementsInBackup = false;
                    RestoreAchievementsSummary = string.Empty;
                }

                IsAutoDetected = true;
                UpdateAutoMigrationDescription();

                var achCountText = HasAchievementsInBackup ? $" â€¢ {RestoreAchievements.Count(a => a.IsUnlocked)} conquistas prontas" : "";
                StatusMessage = $"Backup pronto para restaurar: {manifest.GameName} ({manifest.TotalFiles} arquivos{achCountText}).";
            }
            else
            {
                RestoreAchievements.Clear();
                HasAchievementsInBackup = false;
                RestoreAchievementsSummary = string.Empty;
                IsAutoDetected = false;
                StatusMessage = "O arquivo zip nÃ£o contÃ©m manifest.json vÃ¡lido.";
            }
        }
        catch (Exception ex)
        {
            RestoreAchievements.Clear();
            HasAchievementsInBackup = false;
            RestoreAchievementsSummary = string.Empty;
            IsAutoDetected = false;
            StatusMessage = $"Erro ao abrir arquivo de backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateAutoMigrationDescription()
    {
        if (RestoreManifest == null && RestoreZipQueue.Count == 0 && SelectedLocalBackup == null) return;

        var steamMigrationText = string.Empty;
        if (SelectedTargetSteamAccount != null)
        {
            var activeTag = SelectedTargetSteamAccount.IsActiveNow ? " [CONECTADO AGORA]" : "";
            steamMigrationText = $"\nðŸŽ® MigraÃ§Ã£o de Perfil Steam:\n" +
                                 $"â€¢ Conta Destino: {SelectedTargetSteamAccount.PersonaName} (SteamID3: {SelectedTargetSteamAccount.SteamId3}){activeTag}\n" +
                                 $"Pastas 'userdata', saves e conquistas serÃ£o sincronizados com esta conta.";
        }

        var effectiveTargetUser = SelectedTargetWindowsProfile != null
            ? SelectedTargetWindowsProfile.Username
            : Environment.UserName;

        var effectiveTargetProfile = SelectedTargetWindowsProfile != null
            ? SelectedTargetWindowsProfile.ProfilePath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var achTag = HasAchievementsInBackup ? $"\n{RestoreAchievementsSummary}" : "";

        if (RestoreZipQueue.Count > 1)
        {
            AutoMigrationDescription = $"âš¡ RestauraÃ§Ã£o em lote de {RestoreZipQueue.Count} backups com 1 clique!\n" +
                                       $"â€¢ UsuÃ¡rio Windows Destino: '{effectiveTargetUser}'" +
                                       steamMigrationText + achTag;
        }
        else if (RestoreManifest != null)
        {
            AutoMigrationDescription = $"ðŸŽ¯ MigraÃ§Ã£o AutomÃ¡tica Detectada:\n" +
                                       $"â€¢ UsuÃ¡rio Windows: {RestoreSourceUser} âž” {effectiveTargetUser}\n" +
                                       $"â€¢ Pasta Perfil: {RestoreSourceProfile} âž” {effectiveTargetProfile}" +
                                       steamMigrationText + achTag;
        }
    }

    /// <summary>
    /// RestauraÃ§Ã£o 100% AutomÃ¡tica com 1 Clique (Zero ComplicaÃ§Ã£o e MigraÃ§Ã£o Direta de Perfil Steam, Windows e Conquistas)
    /// </summary>
    private async Task StartAutoRestoreAsync()
    {
        // Se houver mÃºltiplos zips selecionados
        if (RestoreZipQueue.Count > 1)
        {
            await StartBatchRestoreAsync();
            return;
        }

        // Se nenhum zip explÃ­cito foi digitado, mas um backup local estÃ¡ selecionado
        if ((string.IsNullOrWhiteSpace(RestoreZipPath) || !File.Exists(RestoreZipPath)) && SelectedLocalBackup != null)
        {
            RestoreZipPath = SelectedLocalBackup.FilePath;
        }

        if (string.IsNullOrWhiteSpace(RestoreZipPath) || !File.Exists(RestoreZipPath))
        {
            StatusMessage = "Selecione um arquivo de backup (.zip) para restaurar.";
            return;
        }

        IsRestoring = true;
        RestoreProgress = 0;
        RestoreProgressText = "Restaurando automaticamente e ajustando caminhos...";
        RestoreReports.Clear();
        RestoreResultSummary = string.Empty;

        var progress = new Progress<RestoreProgressReport>(report =>
        {
            if (report.TotalFiles > 0)
            {
                RestoreProgress = (double)report.CurrentFileIndex / report.TotalFiles * 100;
                RestoreProgressText = $"Restaurando ({report.CurrentFileIndex}/{report.TotalFiles}): {report.FileName}";
            }
        });

        // ConfiguraÃ§Ã£o direta estilo Nuvem (sem barreiras de checkboxes)
        uint? targetSteamId = SelectedTargetSteamAccount?.SteamId3;
        string? targetUser = SelectedTargetWindowsProfile?.Username ?? Environment.UserName;
        string? targetProfile = SelectedTargetWindowsProfile?.ProfilePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            var result = await Task.Run(async () =>
            {
                return await _engine.AutoRestoreAsync(
                    RestoreZipPath,
                    targetSteamId3: targetSteamId,
                    targetUsername: targetUser,
                    targetUserProfile: targetProfile,
                    overwriteExisting: RestoreOverwrite,
                    dryRun: RestoreDryRun,
                    progress: progress
                );
            });

            RestoreReports = new ObservableCollection<RestoredItemReport>(result.Items);
            RestoreProgress = 100;

            if (result.IsSuccess)
            {
                var steamSuccess = result.SteamProfileMigrated
                    ? $" â€¢ ðŸŽ® Perfil Steam migrado com sucesso para '{SelectedTargetSteamAccount?.PersonaName ?? targetSteamId?.ToString()}'"
                    : string.Empty;

                string achMsg = string.Empty;
                if (result.AchievementSyncDetails != null)
                {
                    if (result.AchievementSyncDetails.LiveSyncedViaSteamworks)
                    {
                        achMsg = $" â€¢ ðŸ† {result.AchievementSyncDetails.SyncedCount} conquista(s) ativadas ao vivo na Steam!";
                    }
                    else if (result.AchievementSyncDetails.ActiveConnectedSteamId3.HasValue)
                    {
                        achMsg = $" â€¢ âš ï¸ Conquistas: Steam conectada em '{result.AchievementSyncDetails.ActiveConnectedPersona}'. Entre na conta de destino na Steam e use 'Sincronizar Conquistas'.";
                    }
                    else
                    {
                        achMsg = $" â€¢ ðŸ† {result.AchievementSyncDetails.SyncedCount} conquista(s) sincronizadas em cache local.";
                    }
                }
                else if (result.AchievementsSynced > 0)
                {
                    achMsg = $" â€¢ ðŸ† {result.AchievementsSynced} conquista(s) vinculadas e sincronizadas Ã  conta Steam!";
                }
                else if (result.Achievements != null && result.Achievements.Count > 0)
                {
                    achMsg = $" â€¢ ðŸ† {result.Achievements.Count(a => a.IsUnlocked)} conquista(s) detectadas no save.";
                }

                RestoreProgressText = RestoreDryRun ? "SimulaÃ§Ã£o concluÃ­da com sucesso!" : "RestauraÃ§Ã£o automÃ¡tica concluÃ­da com sucesso!";
                var steamRunningWarning = !RestoreDryRun && System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
                    ? " âš ï¸ AVISO: A Steam estÃ¡ em execuÃ§Ã£o. Feche e reabra a Steam para que ela recarregue os saves e conquistas sincronizadas antes de abrir o jogo!"
                    : "";

                RestoreResultSummary = RestoreDryRun
                    ? $"ðŸ” SimulaÃ§Ã£o OK: {result.SuccessCount} arquivos mapeados para '{targetUser}'{steamSuccess}{achMsg} sem gravar no disco."
                    : $"ðŸŽ‰ Sucesso! {result.SuccessCount} arquivo(s) restaurados no perfil do usuÃ¡rio '{targetUser}'{steamSuccess}{achMsg}!{steamRunningWarning}";
            }
            else
            {
                RestoreProgressText = "RestauraÃ§Ã£o finalizada com pendÃªncias.";
                RestoreResultSummary = $"âš ï¸ Finalizado com {result.ErrorCount} erro(s). Veja os detalhes abaixo.";
            }
        }
        catch (Exception ex)
        {
            RestoreProgressText = "Erro na restauraÃ§Ã£o.";
            RestoreResultSummary = $"âŒ Erro crÃ­tico: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private async Task StartBatchRestoreAsync()
    {
        IsRestoring = true;
        RestoreProgress = 0;
        RestoreProgressText = $"Restaurando {RestoreZipQueue.Count} backups em lote...";
        RestoreReports.Clear();
        RestoreResultSummary = string.Empty;

        // ConfiguraÃ§Ã£o direta estilo Nuvem
        uint? targetSteamId = SelectedTargetSteamAccount?.SteamId3;
        string? targetUser = SelectedTargetWindowsProfile?.Username ?? Environment.UserName;
        string? targetProfile = SelectedTargetWindowsProfile?.ProfilePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            var batchResult = await Task.Run(async () =>
            {
                return await _engine.AutoRestoreBatchAsync(
                    RestoreZipQueue,
                    targetSteamId3: targetSteamId,
                    targetUsername: targetUser,
                    targetUserProfile: targetProfile,
                    overwriteExisting: RestoreOverwrite,
                    dryRun: RestoreDryRun
                );
            });

            var allItems = new List<RestoredItemReport>();
            foreach (var r in batchResult.Results)
            {
                allItems.AddRange(r.Items);
            }

            RestoreReports = new ObservableCollection<RestoredItemReport>(allItems);
            RestoreProgress = 100;

            var steamBatchMsg = MigrateSteamProfile && SelectedTargetSteamAccount != null
                ? $" e perfil Steam '{SelectedTargetSteamAccount.PersonaName}'"
                : string.Empty;

            if (batchResult.IsSuccess)
            {
                RestoreProgressText = "RestauraÃ§Ã£o em lote concluÃ­da!";
                RestoreResultSummary = $"ðŸŽ‰ Sucesso! Todos os {batchResult.SuccessCount} backups foram restaurados automaticamente ({batchResult.TotalFilesRestored} arquivos migrados para '{Environment.UserName}'{steamBatchMsg}).";
            }
            else
            {
                RestoreProgressText = "RestauraÃ§Ã£o em lote finalizada com erros.";
                RestoreResultSummary = $"âš ï¸ ConcluÃ­do: {batchResult.SuccessCount} sucessos, {batchResult.FailureCount} falhas.";
            }
        }
        catch (Exception ex)
        {
            RestoreProgressText = "Erro na restauraÃ§Ã£o em lote.";
            RestoreResultSummary = $"âŒ Erro: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private async Task StartCustomRestoreAsync()
    {
        await StartAutoRestoreAsync();
    }

    // Propriedades observÃ¡veis
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public SteamInstallationInfo SteamInfo
    {
        get => _steamInfo;
        set
        {
            if (SetProperty(ref _steamInfo, value))
            {
                OnPropertyChanged(nameof(IsSteamFound));
            }
        }
    }

    public bool IsSteamFound => _steamInfo.IsFound;

    public string SteamStatusText
    {
        get => _steamStatusText;
        set => SetProperty(ref _steamStatusText, value);
    }

    public ObservableCollection<SteamGame> Games
    {
        get => _games;
        set => SetProperty(ref _games, value);
    }

    public ICollectionView? FilteredGames => _filteredGames;

    public int TotalGamesCount => _games.Count;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _filteredGames?.Refresh();
            }
        }
    }

    public SteamGame? SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }

    public SteamGame? BackupGame
    {
        get => _backupGame;
        set => SetProperty(ref _backupGame, value);
    }

    public ObservableCollection<GameSaveLocation> BackupLocations
    {
        get => _backupLocations;
        set => SetProperty(ref _backupLocations, value);
    }

    public string BackupZipPath
    {
        get => _backupZipPath;
        set => SetProperty(ref _backupZipPath, value);
    }

    public bool IsBackingUp
    {
        get => _isBackingUp;
        set => SetProperty(ref _isBackingUp, value);
    }

    public double BackupProgress
    {
        get => _backupProgress;
        set => SetProperty(ref _backupProgress, value);
    }

    public string BackupProgressText
    {
        get => _backupProgressText;
        set => SetProperty(ref _backupProgressText, value);
    }

    public string BackupResultSummary
    {
        get => _backupResultSummary;
        set => SetProperty(ref _backupResultSummary, value);
    }

    public string BatchBackupOutputDir
    {
        get => _batchBackupOutputDir;
        set
        {
            if (SetProperty(ref _batchBackupOutputDir, value))
            {
                ApplyBackupDirectoryChange(value);
            }
        }
    }

    public string LocalBackupsDirectoryDescription => $"Exibindo {FilteredLocalBackupsCount} backup(s) locais em: {BatchBackupOutputDir}";

    private void ApplyBackupDirectoryChange(string newDir)
    {
        if (string.IsNullOrWhiteSpace(newDir)) return;

        try
        {
            if (!Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
            }
        }
        catch { }

        SaveCurrentSettings();

        if (BackupGame != null)
        {
            var sanitized = string.Join("_", BackupGame.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            BackupZipPath = Path.Combine(newDir, $"{sanitized}_{BackupGame.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        }

        if (_gameWatcher != null)
        {
            _gameWatcher.UpdateOutputDir(newDir);
        }

        OnPropertyChanged(nameof(LocalBackupsDirectoryDescription));

        // Recarrega a lista de backups locais da aba Restaurar imediatamente
        _ = LoadLocalBackupsAsync();
    }

    private void SaveCurrentSettings()
    {
        try
        {
            var s = new UserSettings
            {
                CustomBackupDirectory = _batchBackupOutputDir,
                AutoStartWatcherOnLaunch = _autoStartWatcherOnLaunch,
                WatcherAutoBackup = _watcherAutoBackup,
                WatcherAutoUpload = _watcherAutoUpload,
                WatcherPollInterval = _watcherPollInterval,
                WatcherPostExitDelay = _watcherPostExitDelay,
                StartWithWindows = _startWithWindows,
                MinimizeToTray = _minimizeToTrayOnClose,
                OpenSteamOnStartup = _openSteamOnStartup,
                LastTargetSteamId3 = SelectedTargetSteamAccount?.SteamId3,
                LastTargetWindowsUsername = SelectedTargetWindowsProfile?.Username ?? string.Empty,
                LastSourceSteamId3 = SelectedSourceSteamAccount?.SteamId3,
                LastSourceWindowsUsername = SelectedSourceWindowsProfile?.Username ?? string.Empty
            };
            AppSettingsService.SaveSettings(s);
        }
        catch { }
    }

    public bool IsBatchBackingUp
    {
        get => _isBatchBackingUp;
        set => SetProperty(ref _isBatchBackingUp, value);
    }

    public double BatchOverallProgress
    {
        get => _batchOverallProgress;
        set => SetProperty(ref _batchOverallProgress, value);
    }

    public string BatchOverallProgressText
    {
        get => _batchOverallProgressText;
        set => SetProperty(ref _batchOverallProgressText, value);
    }

    public string BatchCurrentGameText
    {
        get => _batchCurrentGameText;
        set => SetProperty(ref _batchCurrentGameText, value);
    }

    public string BatchBackupSummary
    {
        get => _batchBackupSummary;
        set => SetProperty(ref _batchBackupSummary, value);
    }

    public string RestoreZipPath
    {
        get => _restoreZipPath;
        set => SetProperty(ref _restoreZipPath, value);
    }

    public ObservableCollection<string> RestoreZipQueue
    {
        get => _restoreZipQueue;
        set => SetProperty(ref _restoreZipQueue, value);
    }

    public BackupManifest? RestoreManifest
    {
        get => _restoreManifest;
        set => SetProperty(ref _restoreManifest, value);
    }

    public string RestoreSourceUser
    {
        get => _restoreSourceUser;
        set => SetProperty(ref _restoreSourceUser, value);
    }

    public string RestoreSourceProfile
    {
        get => _restoreSourceProfile;
        set => SetProperty(ref _restoreSourceProfile, value);
    }

    public string RestoreTargetUser
    {
        get => _restoreTargetUser;
        set
        {
            if (SetProperty(ref _restoreTargetUser, value))
            {
                RestoreTargetProfile = WindowsKnownFolders.GetFoldersForUsername(value).UserProfile;
            }
        }
    }

    public string RestoreTargetProfile
    {
        get => _restoreTargetProfile;
        set => SetProperty(ref _restoreTargetProfile, value);
    }

    public bool RestoreDryRun
    {
        get => _restoreDryRun;
        set => SetProperty(ref _restoreDryRun, value);
    }

    public bool RestoreOverwrite
    {
        get => _restoreOverwrite;
        set => SetProperty(ref _restoreOverwrite, value);
    }

    public bool IsRestoring
    {
        get => _isRestoring;
        set => SetProperty(ref _isRestoring, value);
    }

    public double RestoreProgress
    {
        get => _restoreProgress;
        set => SetProperty(ref _restoreProgress, value);
    }

    public string RestoreProgressText
    {
        get => _restoreProgressText;
        set => SetProperty(ref _restoreProgressText, value);
    }

    public ObservableCollection<RestoredItemReport> RestoreReports
    {
        get => _restoreReports;
        set => SetProperty(ref _restoreReports, value);
    }

    public string RestoreResultSummary
    {
        get => _restoreResultSummary;
        set => SetProperty(ref _restoreResultSummary, value);
    }

    public string AutoMigrationDescription
    {
        get => _autoMigrationDescription;
        set => SetProperty(ref _autoMigrationDescription, value);
    }

    public bool IsAutoDetected
    {
        get => _isAutoDetected;
        set => SetProperty(ref _isAutoDetected, value);
    }

    public ObservableCollection<SteamUserAccount> AvailableSteamAccounts
    {
        get => _availableSteamAccounts;
        set => SetProperty(ref _availableSteamAccounts, value);
    }

    public SteamUserAccount? SelectedSourceSteamAccount
    {
        get => _selectedSourceSteamAccount;
        set => SetProperty(ref _selectedSourceSteamAccount, value);
    }

    public SteamUserAccount? SelectedTargetSteamAccount
    {
        get => _selectedTargetSteamAccount;
        set
        {
            if (SetProperty(ref _selectedTargetSteamAccount, value))
            {
                UpdateAutoMigrationDescription();
            }
        }
    }

    public bool MigrateSteamProfile
    {
        get => _migrateSteamProfile;
        set
        {
            if (SetProperty(ref _migrateSteamProfile, value))
            {
                UpdateAutoMigrationDescription();
            }
        }
    }

    public string RestoreSourceSteamText
    {
        get => _restoreSourceSteamText;
        set => SetProperty(ref _restoreSourceSteamText, value);
    }

    public string RestoreSourceSteamDetails
    {
        get => _restoreSourceSteamDetails;
        set => SetProperty(ref _restoreSourceSteamDetails, value);
    }

    public bool HasSourceSteamAccount
    {
        get => _hasSourceSteamAccount;
        set => SetProperty(ref _hasSourceSteamAccount, value);
    }

    // Comandos
    public ICommand RefreshSteamCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ScanGameSavesCommand { get; }
    public ICommand SelectAllGamesCommand { get; }
    public ICommand DeselectAllGamesCommand { get; }
    public ICommand SelectGamesWithSavesCommand { get; }
    public ICommand GoToBatchBackupTabCommand { get; }

    public ICommand BrowseBackupOutputCommand { get; }
    public ICommand StartBackupCommand { get; }
    public ICommand BrowseBatchBackupOutputDirCommand { get; }
    public ICommand OpenBackupOutputDirCommand { get; }
    public ICommand ResetBackupOutputDirCommand { get; }
    public ICommand StartBatchBackupCommand { get; }

    public ICommand BrowseRestoreZipCommand { get; }
    public ICommand BrowseMultipleRestoreZipsCommand { get; }
    public ICommand StartAutoRestoreCommand { get; }
    public ICommand StartCustomRestoreCommand { get; }
    public ICommand RefreshLocalBackupsCommand { get; }
    public ICommand ClearLocalSearchCommand { get; }
    public ICommand DeleteSelectedLocalBackupCommand { get; }
    public ICommand SyncAchievementsCommand { get; }

    // Comandos Nuvem & Watcher
    public ICommand ConnectGoogleDriveCommand { get; }
    public ICommand CancelGoogleDriveAuthCommand { get; }
    public ICommand DisconnectGoogleDriveCommand { get; }
    public ICommand ReconnectGoogleDriveCommand { get; }
    public ICommand RefreshCloudBackupsCommand { get; }
    public ICommand RestoreSelectedCloudBackupCommand { get; }
    public ICommand DeleteSelectedCloudBackupCommand { get; }
    public ICommand ToggleConfiguringCredentialsCommand { get; }
    public ICommand ImportCredentialsJsonCommand { get; }
    public ICommand SaveCustomCredentialsCommand { get; }
    public ICommand OpenGoogleConsoleCommand { get; }
    public ICommand ToggleGameWatcherCommand { get; }
    public ICommand ClearWatcherLogsCommand { get; }

    // ==========================================
    // PROPRIEDADES PÃšBLICAS: GOOGLE DRIVE & MONITOR
    // ==========================================

    public bool HasGoogleCredentials
    {
        get => _hasGoogleCredentials;
        set => SetProperty(ref _hasGoogleCredentials, value);
    }

    public bool IsConfiguringCredentials
    {
        get => _isConfiguringCredentials;
        set => SetProperty(ref _isConfiguringCredentials, value);
    }

    public string CustomClientId
    {
        get => _customClientId;
        set
        {
            if (SetProperty(ref _customClientId, value))
            {
                (SaveCustomCredentialsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CustomClientSecret
    {
        get => _customClientSecret;
        set
        {
            if (SetProperty(ref _customClientSecret, value))
            {
                (SaveCustomCredentialsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CredentialsStatusText
    {
        get => _credentialsStatusText;
        set => SetProperty(ref _credentialsStatusText, value);
    }

    public bool IsGoogleDriveConnected
    {
        get => _isGoogleDriveConnected;
        set => SetProperty(ref _isGoogleDriveConnected, value);
    }

    public string GoogleDriveUserEmail
    {
        get => _googleDriveUserEmail;
        set => SetProperty(ref _googleDriveUserEmail, value);
    }

    public string GoogleDriveStatusText
    {
        get => _googleDriveStatusText;
        set => SetProperty(ref _googleDriveStatusText, value);
    }

    public ObservableCollection<CloudBackupInfo> CloudBackups
    {
        get => _cloudBackups;
        set
        {
            if (SetProperty(ref _cloudBackups, value))
            {
                _filteredCloudBackups = CollectionViewSource.GetDefaultView(_cloudBackups);
                _filteredCloudBackups.Filter = FilterCloudBackupItem;
                OnPropertyChanged(nameof(FilteredCloudBackups));
                OnPropertyChanged(nameof(FilteredCloudBackupsCount));
            }
        }
    }

    public ICollectionView? FilteredCloudBackups => _filteredCloudBackups;

    public string CloudSearchText
    {
        get => _cloudSearchText;
        set
        {
            if (SetProperty(ref _cloudSearchText, value))
            {
                _filteredCloudBackups?.Refresh();
                OnPropertyChanged(nameof(FilteredCloudBackupsCount));
            }
        }
    }

    public int FilteredCloudBackupsCount
    {
        get
        {
            if (_filteredCloudBackups == null) return _cloudBackups.Count;
            int count = 0;
            foreach (var _ in _filteredCloudBackups) count++;
            return count;
        }
    }

    private bool FilterCloudBackupItem(object obj)
    {
        if (obj is not CloudBackupInfo item) return false;
        if (string.IsNullOrWhiteSpace(_cloudSearchText)) return true;

        var term = _cloudSearchText.Trim();
        if (item.GameName.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.AppId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(item.SourceSteamPersona) && item.SourceSteamPersona.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.SourceSteamId3.HasValue && item.SourceSteamId3.Value.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(item.SourceUsername) && item.SourceUsername.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    public ObservableCollection<WindowsProfileInfo> AvailableWindowsProfiles
    {
        get => _availableWindowsProfiles;
        set => SetProperty(ref _availableWindowsProfiles, value);
    }

    public WindowsProfileInfo? SelectedTargetWindowsProfile
    {
        get => _selectedTargetWindowsProfile;
        set
        {
            if (SetProperty(ref _selectedTargetWindowsProfile, value))
            {
                if (value != null)
                {
                    RestoreTargetUser = value.Username;
                    RestoreTargetProfile = value.ProfilePath;
                }
                UpdateAutoMigrationDescription();
            }
        }
    }

    public WindowsProfileInfo? SelectedSourceWindowsProfile
    {
        get => _selectedSourceWindowsProfile;
        set => SetProperty(ref _selectedSourceWindowsProfile, value);
    }

    public bool MigrateWindowsUser
    {
        get => _migrateWindowsUser;
        set
        {
            if (SetProperty(ref _migrateWindowsUser, value))
            {
                UpdateAutoMigrationDescription();
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                WindowsStartupService.SetStartup(value, arguments: "--minimized");
                SaveCurrentSettings();
                StatusMessage = value
                    ? "SteamSaveMigrator configurado para iniciar junto com o Windows / Steam!"
                    : "InicializaÃ§Ã£o automÃ¡tica com o Windows desativada.";
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public bool AutoStartWatcherOnLaunch
    {
        get => _autoStartWatcherOnLaunch;
        set
        {
            if (SetProperty(ref _autoStartWatcherOnLaunch, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public ICommand ClearCloudSearchCommand { get; }

    public CloudBackupInfo? SelectedCloudBackup
    {
        get => _selectedCloudBackup;
        set => SetProperty(ref _selectedCloudBackup, value);
    }

    public bool IsCloudLoading
    {
        get => _isCloudLoading;
        set => SetProperty(ref _isCloudLoading, value);
    }

    public bool IsCloudRestoring
    {
        get => _isCloudRestoring;
        set => SetProperty(ref _isCloudRestoring, value);
    }

    public double CloudRestoreProgress
    {
        get => _cloudRestoreProgress;
        set => SetProperty(ref _cloudRestoreProgress, value);
    }

    public string CloudRestoreProgressText
    {
        get => _cloudRestoreProgressText;
        set => SetProperty(ref _cloudRestoreProgressText, value);
    }

    public string CloudResultSummary
    {
        get => _cloudResultSummary;
        set => SetProperty(ref _cloudResultSummary, value);
    }

    public bool IsGameWatcherActive
    {
        get => _isGameWatcherActive;
        set => SetProperty(ref _isGameWatcherActive, value);
    }

    public string WatcherStatusBadge
    {
        get => _watcherStatusBadge;
        set => SetProperty(ref _watcherStatusBadge, value);
    }

    public string WatcherLastEventText
    {
        get => _watcherLastEventText;
        set => SetProperty(ref _watcherLastEventText, value);
    }

    public ObservableCollection<string> WatcherLogs
    {
        get => _watcherLogs;
        set => SetProperty(ref _watcherLogs, value);
    }

    public bool WatcherAutoBackup
    {
        get => _watcherAutoBackup;
        set
        {
            if (SetProperty(ref _watcherAutoBackup, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public bool WatcherAutoUpload
    {
        get => _watcherAutoUpload;
        set
        {
            if (SetProperty(ref _watcherAutoUpload, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public int WatcherPollInterval
    {
        get => _watcherPollInterval;
        set
        {
            if (SetProperty(ref _watcherPollInterval, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public int WatcherPostExitDelay
    {
        get => _watcherPostExitDelay;
        set
        {
            if (SetProperty(ref _watcherPostExitDelay, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public bool OpenSteamOnStartup
    {
        get => _openSteamOnStartup;
        set
        {
            if (SetProperty(ref _openSteamOnStartup, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public string SettingsSavedFeedback
    {
        get => _settingsSavedFeedback;
        set => SetProperty(ref _settingsSavedFeedback, value);
    }

    public void SaveSettingsExplicitly()
    {
        ApplyBackupDirectoryChange(BatchBackupOutputDir);
        SettingsSavedFeedback = "âœ… ConfiguraÃ§Ãµes e pasta salvas e ativas!";
        StatusMessage = $"ConfiguraÃ§Ãµes salvas com sucesso! Pasta de backups ativa: {BatchBackupOutputDir}";

        _ = Task.Delay(4000).ContinueWith(_ =>
        {
            SettingsSavedFeedback = string.Empty;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ==========================================
    // MÃ‰TODOS DE SUPORTE: GOOGLE DRIVE & MONITOR
    // ==========================================

    public async Task CheckGoogleDriveStatusAsync()
    {
        try
        {
            HasGoogleCredentials = _engine.GoogleDrive.HasConfiguredCredentials;
            if (!string.IsNullOrWhiteSpace(_engine.GoogleDrive.CurrentClientId))
            {
                CustomClientId = _engine.GoogleDrive.CurrentClientId;
            }

            var isAuth = await _engine.GoogleDrive.IsAuthenticatedAsync();
            IsGoogleDriveConnected = isAuth;

            if (isAuth)
            {
                var email = await _engine.GoogleDrive.GetUserEmailAsync();
                GoogleDriveUserEmail = email ?? "Conta Google";
                GoogleDriveStatusText = $"Conectado ({GoogleDriveUserEmail})";
                await LoadCloudBackupsAsync();
            }
            else
            {
                GoogleDriveUserEmail = string.Empty;
                if (!HasGoogleCredentials)
                {
                    GoogleDriveStatusText = "Aguardando credenciais do Google Drive...";
                    CredentialsStatusText = "Aguardando credenciais OAuth do Google Drive.";
                }
                else
                {
                    GoogleDriveStatusText = "Pronto para conectar! Clique em 'Conectar Google Drive' para fazer login com sua conta Google.";
                }
                CloudBackups.Clear();
            }
        }
        catch (Exception ex)
        {
            GoogleDriveStatusText = $"Status Google Drive: {ex.Message}";
        }
    }

    private CancellationTokenSource? _googleDriveAuthCts;

    public void CancelGoogleDriveAuth()
    {
        try
        {
            _googleDriveAuthCts?.Cancel();
            _googleDriveAuthCts?.Dispose();
        }
        catch { }
        finally
        {
            _googleDriveAuthCts = null;
        }

        _engine.GoogleDrive.CancelAuthentication();
        IsCloudLoading = false;
        StatusMessage = "Tentativa de login cancelada. Clique em 'Conectar Google Drive' para tentar novamente.";
        GoogleDriveStatusText = "NÃ£o conectado (tentativa cancelada)";
        CommandManager.InvalidateRequerySuggested();
    }

    public async Task ConnectGoogleDriveAsync()
    {
        if (!_engine.GoogleDrive.HasConfiguredCredentials)
        {
            GoogleDriveStatusText = "Credenciais do Google Drive nÃ£o configuradas.";
            StatusMessage = "Chaves do Google Cloud ausentes.";
            return;
        }

        // Se o usuÃ¡rio clicou de novo enquanto uma janela/tentativa anterior ainda estava aberta ou pendente,
        // cancela a anterior imediatamente para liberar a porta e permitir abrir a nova janela sem travamento!
        if (IsCloudLoading || _googleDriveAuthCts != null)
        {
            CancelGoogleDriveAuth();
            await Task.Delay(400); // Pausa para liberaÃ§Ã£o completa dos sockets locais do HttpListener
        }

        _googleDriveAuthCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        IsCloudLoading = true;
        StatusMessage = "Iniciando autenticaÃ§Ã£o no Google Drive... (autorize na janela do navegador)";
        GoogleDriveStatusText = "Aguardando autorizaÃ§Ã£o no navegador...";
        CommandManager.InvalidateRequerySuggested();

        try
        {
            var success = await _engine.GoogleDrive.AuthenticateAsync(_googleDriveAuthCts.Token);
            if (success)
            {
                var email = await _engine.GoogleDrive.GetUserEmailAsync();
                IsGoogleDriveConnected = true;
                GoogleDriveUserEmail = email ?? "Conta Google";
                GoogleDriveStatusText = $"Conectado ({GoogleDriveUserEmail})";
                StatusMessage = "Google Drive conectado com sucesso!";
                IsConfiguringCredentials = false;
                await LoadCloudBackupsAsync();
            }
            else
            {
                StatusMessage = "AutenticaÃ§Ã£o cancelada ou nÃ£o concluÃ­da no navegador.";
                GoogleDriveStatusText = "NÃ£o conectado (autorizaÃ§Ã£o cancelada)";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "AutenticaÃ§Ã£o cancelada. Clique em 'Conectar Google Drive' para tentar novamente.";
            GoogleDriveStatusText = "NÃ£o conectado ao Google Drive";
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("401") || ex.Message.Contains("invalid_client"))
            {
                StatusMessage = "Erro 401 (invalid_client): O Client ID nÃ£o existe ou foi excluÃ­do no Google Cloud.";
                GoogleDriveStatusText = "Erro 401: Client ID nÃ£o encontrado no Google Cloud.";
            }
            else if (ex.Message.Contains("403") || ex.Message.Contains("access_denied"))
            {
                StatusMessage = "Erro 403: E-mail nÃ£o cadastrado em 'UsuÃ¡rios de teste' no Google Cloud Console.";
                GoogleDriveStatusText = "Erro 403: Acesso negado. Adicione este e-mail como UsuÃ¡rio de teste no console.";
            }
            else
            {
                StatusMessage = $"Falha ao conectar no Google Drive: {ex.Message}";
                GoogleDriveStatusText = $"Erro de conexÃ£o: {ex.Message}";
            }
        }
        finally
        {
            IsCloudLoading = false;
            try { _googleDriveAuthCts?.Dispose(); } catch { }
            _googleDriveAuthCts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ImportCredentialsJson()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecione o arquivo credentials.json baixado do Google Cloud Console",
            Filter = "Arquivo JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            FileName = "credentials.json"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var success = _engine.GoogleDrive.ConfigureCredentialsFromJson(ofd.FileName);
                if (success)
                {
                    HasGoogleCredentials = true;
                    CustomClientId = _engine.GoogleDrive.CurrentClientId ?? string.Empty;
                    CredentialsStatusText = "âœ… Arquivo credentials.json importado com sucesso!";
                    GoogleDriveStatusText = "Credenciais configuradas com sucesso! Clique em 'Conectar ao Google Drive'.";
                    StatusMessage = "Credenciais do Google Drive configuradas!";
                }
                else
                {
                    CredentialsStatusText = "âŒ NÃ£o foi possÃ­vel extrair client_id e client_secret do arquivo selecionado.";
                }
            }
            catch (Exception ex)
            {
                CredentialsStatusText = $"âŒ Erro ao importar: {ex.Message}";
            }
        }
    }

    private void SaveCustomCredentials()
    {
        if (string.IsNullOrWhiteSpace(CustomClientId) || string.IsNullOrWhiteSpace(CustomClientSecret))
        {
            CredentialsStatusText = "âŒ Preencha tanto o Client ID quanto o Client Secret.";
            return;
        }

        try
        {
            _engine.GoogleDrive.ConfigureClientSecrets(CustomClientId.Trim(), CustomClientSecret.Trim());
            HasGoogleCredentials = true;
            CredentialsStatusText = "âœ… Credenciais salvas com sucesso!";
            GoogleDriveStatusText = "Credenciais configuradas! Pronto para conectar.";
            StatusMessage = "Credenciais do Google Drive salvas com sucesso!";
        }
        catch (Exception ex)
        {
            CredentialsStatusText = $"âŒ Erro ao salvar credenciais: {ex.Message}";
        }
    }

    private void OpenGoogleConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://console.cloud.google.com/apis/credentials",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignora se nÃ£o conseguir abrir o navegador
        }
    }

    public async Task DisconnectGoogleDriveAsync()
    {
        IsCloudLoading = true;
        StatusMessage = "Desconectando do Google Drive...";

        try
        {
            await _engine.GoogleDrive.SignOutAsync();
            IsGoogleDriveConnected = false;
            GoogleDriveUserEmail = string.Empty;
            GoogleDriveStatusText = "NÃ£o conectado ao Google Drive";
            CloudBackups.Clear();
            SelectedCloudBackup = null;
            StatusMessage = "Google Drive desconectado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao desconectar: {ex.Message}";
        }
        finally
        {
            IsCloudLoading = false;
        }
    }

    public async Task ReconnectGoogleDriveAsync()
    {
        await DisconnectGoogleDriveAsync();
        await ConnectGoogleDriveAsync();
    }

    public async Task LoadCloudBackupsAsync()
    {
        if (!IsGoogleDriveConnected) return;

        IsCloudLoading = true;
        StatusMessage = "Buscando backups salvos no Google Drive...";

        try
        {
            var list = await _engine.GoogleDrive.ListBackupsAsync();
            CloudBackups = new ObservableCollection<CloudBackupInfo>(list);
            StatusMessage = $"{list.Count} backup(s) encontrados na nuvem Google Drive.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao listar backups na nuvem: {ex.Message}";
        }
        finally
        {
            IsCloudLoading = false;
        }
    }

    public async Task RestoreSelectedCloudBackupAsync()
    {
        if (SelectedCloudBackup == null) return;

        IsCloudRestoring = true;
        CloudRestoreProgress = 0;
        CloudRestoreProgressText = "Iniciando download da nuvem...";
        CloudResultSummary = string.Empty;

        try
        {
            var progress = new Progress<double>(p =>
            {
                CloudRestoreProgress = p * 100;
                CloudRestoreProgressText = p < 1.0
                    ? $"Baixando backup do Google Drive... {p:P0}"
                    : "Download concluÃ­do! Restaurando dados e perfil Steam...";
            });

            var targetSteamId3 = SelectedTargetSteamAccount?.SteamId3;
            var targetUser = SelectedTargetWindowsProfile?.Username;
            var targetProfile = SelectedTargetWindowsProfile?.ProfilePath;

            var res = await _engine.RestoreCloudBackupAsync(
                SelectedCloudBackup.FileId,
                targetSteamId3: targetSteamId3,
                targetUsername: targetUser,
                targetUserProfile: targetProfile,
                overwriteExisting: true,
                dryRun: false,
                downloadProgress: progress);

            if (res.IsSuccess)
            {
                var steamInfoMsg = res.SteamProfileMigrated
                    ? $" e convertidos para o perfil Steam '{SelectedTargetSteamAccount?.PersonaName ?? SelectedTargetSteamAccount?.SteamId3.ToString()}'"
                    : "";
                var targetUserMsg = !string.IsNullOrWhiteSpace(targetUser) ? targetUser : Environment.UserName;

                var steamRunningWarning = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
                    ? " âš ï¸ AVISO: A Steam estÃ¡ em execuÃ§Ã£o. Feche e reabra a Steam para que ela recarregue os saves sincronizados antes de abrir o jogo!"
                    : "";

                var achMsg = res.AchievementsSynced > 0
                    ? $" â€¢ ðŸ† {res.AchievementsSynced} conquista(s) vinculadas e sincronizadas Ã  conta Steam!"
                    : (res.Achievements != null && res.Achievements.Count > 0 ? $" â€¢ ðŸ† {res.Achievements.Count(a => a.IsUnlocked)} conquista(s) sincronizadas." : "");

                CloudResultSummary = $"ðŸŽ‰ Sucesso! {res.SuccessCount} arquivo(s) restaurados no perfil Windows '{targetUserMsg}'{steamInfoMsg}{achMsg} com perfeiÃ§Ã£o!{steamRunningWarning}";
                StatusMessage = "Backup da nuvem restaurado com sucesso!";
            }
            else
            {
                CloudResultSummary = $"âŒ Falha ao restaurar: {res.ErrorCount} erro(s).";
                StatusMessage = "Erro ao restaurar backup da nuvem.";
            }
        }
        catch (Exception ex)
        {
            CloudResultSummary = $"âŒ Erro inesperado: {ex.Message}";
            StatusMessage = "Falha durante a restauraÃ§Ã£o do Google Drive.";
        }
        finally
        {
            IsCloudRestoring = false;
        }
    }

    public async Task DeleteSelectedCloudBackupAsync()
    {
        if (SelectedCloudBackup == null) return;

        var toDelete = SelectedCloudBackup;
        IsCloudLoading = true;
        StatusMessage = $"Excluindo backup de '{toDelete.GameName}' da nuvem...";

        try
        {
            await _engine.GoogleDrive.DeleteBackupAsync(toDelete.FileId);
            CloudBackups.Remove(toDelete);
            SelectedCloudBackup = null;
            StatusMessage = "Backup excluÃ­do da nuvem com sucesso.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao excluir backup da nuvem: {ex.Message}";
        }
        finally
        {
            IsCloudLoading = false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task ToggleGameWatcherAsync()
    {
        if (IsGameWatcherActive)
        {
            if (_gameWatcher != null)
            {
                await _gameWatcher.StopAsync();
                _gameWatcher = null;
            }
            IsGameWatcherActive = false;
            WatcherStatusBadge = "Inativo (Parado)";
            AddWatcherLog("ðŸ›‘ Monitor de jogos parado pelo usuÃ¡rio.");
            StatusMessage = "Monitor de jogos desativado.";
            WatcherStatusChanged?.Invoke(false);
        }
        else
        {
            var config = new GameWatcherConfig
            {
                LocalBackupOutputDir = BatchBackupOutputDir,
                AutoBackupOnGameExit = WatcherAutoBackup,
                AutoUploadToGoogleDrive = WatcherAutoUpload && IsGoogleDriveConnected,
                PollIntervalSeconds = WatcherPollInterval,
                PostExitDelaySeconds = WatcherPostExitDelay
            };

            _gameWatcher = _engine.CreateWatcher(config);
            _gameWatcher.OnWatcherEvent += OnWatcherEventReceived;
            _gameWatcher.Start();

            IsGameWatcherActive = true;
            WatcherStatusBadge = "ðŸŸ¢ Ativo e Monitorando";
            AddWatcherLog($"ðŸŸ¢ Monitor iniciado! VigiarÃ¡ processos Steam a cada {config.PollIntervalSeconds}s.");
            StatusMessage = "Monitor de jogos ativado.";
            WatcherStatusChanged?.Invoke(true);
        }
    }

    private void OnWatcherEventReceived(GameWatcherEvent e)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            var time = e.Timestamp.ToString("HH:mm:ss");
            var gameName = e.Game?.Name ?? "Jogo Steam";
            string logLine;

            switch (e.Type)
            {
                case GameWatcherEventType.GameStarted:
                    logLine = $"[{time}] ðŸŽ® JOGO INICIADO: {gameName} (AppID: {e.Game?.AppId})";
                    WatcherLastEventText = $"Jogo em execuÃ§Ã£o: {gameName}";
                    break;

                case GameWatcherEventType.GameExited:
                    logLine = $"[{time}] â¹ï¸ JOGO FECHADO: {gameName}! Aguardando dados de save...";
                    WatcherLastEventText = $"Jogo fechado: {gameName}. Preparando backup...";
                    NotificationRequested?.Invoke("Jogo Steam Fechado", $"ðŸŽ® {gameName} foi fechado. Realizando backup automÃ¡tico...", false);
                    break;

                case GameWatcherEventType.BackupStarted:
                    logLine = $"[{time}] ðŸ’¾ BACKUP INICIADO: Compactando saves de {gameName}...";
                    break;

                case GameWatcherEventType.BackupCompleted:
                    logLine = $"[{time}] âœ… BACKUP LOCAL PRONTO: {Path.GetFileName(e.LocalZipPath ?? "")}";
                    WatcherLastEventText = $"Backup local salvo: {Path.GetFileName(e.LocalZipPath ?? "")}";
                    _ = LoadLocalBackupsAsync();
                    break;

                case GameWatcherEventType.CloudUploadStarted:
                    logLine = $"[{time}] â˜ï¸ UPLOAD NUVEM: Enviando saves de {gameName} para o Google Drive...";
                    break;

                case GameWatcherEventType.CloudUploadCompleted:
                    logLine = $"[{time}] ðŸŽ‰ SINCRONIZADO NO GOOGLE DRIVE: {gameName} salvo na nuvem!";
                    WatcherLastEventText = $"Salvo na nuvem: {gameName}";
                    NotificationRequested?.Invoke("Backup na Nuvem ConcluÃ­do", $"ðŸŽ‰ Saves de {gameName} sincronizados no Google Drive com sucesso!", true);
                    _ = LoadCloudBackupsAsync();
                    break;

                case GameWatcherEventType.Error:
                    logLine = $"[{time}] âŒ ERRO: {e.Message}";
                    WatcherLastEventText = $"Erro: {e.Message}";
                    if (e.Message.Contains("nÃ£o autenticado") || e.Message.Contains("insufficient") || e.Message.Contains("Forbidden"))
                    {
                        IsGoogleDriveConnected = false;
                        GoogleDriveStatusText = "NÃ£o autenticado. Clique em 'Conectar Google Drive'.";
                    }
                    NotificationRequested?.Invoke("Aviso do Monitor", e.Message, false);
                    break;

                default:
                    logLine = $"[{time}] {e.Message}";
                    break;
            }

            AddWatcherLog(logLine);
        });
    }

    private void AddWatcherLog(string message)
    {
        WatcherLogs.Insert(0, message);
        while (WatcherLogs.Count > 100)
        {
            WatcherLogs.RemoveAt(WatcherLogs.Count - 1);
        }
    }

    // ==========================================
    // BACKUPS LOCAIS (EQUIPARADO Ã€ NUVEM)
    // ==========================================

    public ObservableCollection<LocalBackupInfo> LocalBackups
    {
        get => _localBackups;
        set
        {
            if (SetProperty(ref _localBackups, value))
            {
                _filteredLocalBackups = CollectionViewSource.GetDefaultView(_localBackups);
                _filteredLocalBackups.Filter = FilterLocalBackupItem;
                OnPropertyChanged(nameof(FilteredLocalBackups));
                OnPropertyChanged(nameof(FilteredLocalBackupsCount));
            }
        }
    }

    public ICollectionView? FilteredLocalBackups => _filteredLocalBackups;

    public string LocalSearchText
    {
        get => _localSearchText;
        set
        {
            if (SetProperty(ref _localSearchText, value))
            {
                _filteredLocalBackups?.Refresh();
                OnPropertyChanged(nameof(FilteredLocalBackupsCount));
            }
        }
    }

    public int FilteredLocalBackupsCount
    {
        get
        {
            if (_filteredLocalBackups == null) return _localBackups.Count;
            int count = 0;
            foreach (var _ in _filteredLocalBackups) count++;
            return count;
        }
    }

    public LocalBackupInfo? SelectedLocalBackup
    {
        get => _selectedLocalBackup;
        set
        {
            if (SetProperty(ref _selectedLocalBackup, value))
            {
                if (value != null && !string.IsNullOrWhiteSpace(value.FilePath))
                {
                    RestoreZipPath = value.FilePath;
                    _ = LoadRestoreManifestAsync(value.FilePath);
                }
            }
        }
    }

    public bool IsLocalBackupsLoading
    {
        get => _isLocalBackupsLoading;
        set => SetProperty(ref _isLocalBackupsLoading, value);
    }

    // ==========================================
    // MÃ“DULO DE CONQUISTAS (ACHIEVEMENTS)
    // ==========================================

    public ObservableCollection<GameAchievementInfo> RestoreAchievements
    {
        get => _restoreAchievements;
        set => SetProperty(ref _restoreAchievements, value);
    }

    public bool HasAchievementsInBackup
    {
        get => _hasAchievementsInBackup;
        set => SetProperty(ref _hasAchievementsInBackup, value);
    }

    public string RestoreAchievementsSummary
    {
        get => _restoreAchievementsSummary;
        set => SetProperty(ref _restoreAchievementsSummary, value);
    }

    private bool FilterLocalBackupItem(object obj)
    {
        if (obj is not LocalBackupInfo item) return false;
        if (string.IsNullOrWhiteSpace(_localSearchText)) return true;

        var term = _localSearchText.Trim();
        if (item.GameName.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.AppId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(item.SourceSteamPersona) && item.SourceSteamPersona.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.SourceSteamId3.HasValue && item.SourceSteamId3.Value.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(item.SourceUsername) && item.SourceUsername.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    public async Task LoadLocalBackupsAsync()
    {
        IsLocalBackupsLoading = true;
        try
        {
            var list = await _engine.GetLocalBackupsAsync(BatchBackupOutputDir);
            LocalBackups = new ObservableCollection<LocalBackupInfo>(list);
            if (SelectedLocalBackup == null && LocalBackups.Count > 0)
            {
                SelectedLocalBackup = LocalBackups[0];
            }
            OnPropertyChanged(nameof(FilteredLocalBackupsCount));
            OnPropertyChanged(nameof(LocalBackupsDirectoryDescription));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao listar backups locais: {ex.Message}";
        }
        finally
        {
            IsLocalBackupsLoading = false;
        }
    }

    public async Task DeleteSelectedLocalBackupAsync()
    {
        if (SelectedLocalBackup == null) return;
        var toDelete = SelectedLocalBackup;
        try
        {
            if (File.Exists(toDelete.FilePath))
            {
                File.Delete(toDelete.FilePath);
            }
            LocalBackups.Remove(toDelete);
            SelectedLocalBackup = LocalBackups.FirstOrDefault();
            StatusMessage = $"Backup local de '{toDelete.GameName}' excluÃ­do com sucesso.";
            OnPropertyChanged(nameof(FilteredLocalBackupsCount));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao excluir backup local: {ex.Message}";
        }
    }

    public async Task SyncAchievementsAsync()
    {
        if (RestoreManifest == null || RestoreManifest.Achievements == null || RestoreManifest.Achievements.Count == 0)
        {
            StatusMessage = "Nenhuma conquista disponÃ­vel no backup para sincronizar.";
            return;
        }

        var targetSteamId = SelectedTargetSteamAccount?.SteamId3;
        if (!targetSteamId.HasValue)
        {
            StatusMessage = "Selecione uma conta Steam de destino para sincronizar as conquistas.";
            return;
        }

        try
        {
            StatusMessage = "ðŸ† Conectando Ã  Steam e sincronizando conquistas...";
            var achService = new SteamAchievementService();
            var syncResult = achService.SyncAchievements(
                _engine.SteamInfo.SteamPath,
                RestoreManifest.AppId,
                targetSteamId.Value,
                RestoreManifest.Achievements,
                RestoreManifest.SourceSteamId3
            );

            var targetName = SelectedTargetSteamAccount?.PersonaName ?? targetSteamId.Value.ToString();
            StatusMessage = syncResult.Message;
            RestoreResultSummary = syncResult.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao sincronizar conquistas: {ex.Message}";
            RestoreResultSummary = $"âŒ Erro ao sincronizar conquistas: {ex.Message}";
        }
    }
}


