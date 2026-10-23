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
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.5.1";
    public string AppVersionDisplay => $"v{AppVersion}";
    public string WindowTitle => $"SteamSave Migrator {AppVersionDisplay} \u2022 Migrador, Conquistas e Conversor de Saves Steam";
    public string FooterStatusDisplay => $"SteamSaveMigrator {AppVersionDisplay} \u2022 Backup em Lote, Conquistas & RestauraÃ§Ã£o AutomÃ¡tica";

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
    private bool _autoUploadToCloudOnBackup = true;
    private bool _autoUploadToCloudNonSteamBackup = true;
    private int _watcherPollInterval = 2;
    private int _watcherPostExitDelay = 3;

    // ConfiguraÃ§Ãµes de InicializaÃ§Ã£o e MinimizaÃ§Ã£o na Bandeja
    private bool _startWithWindows;
    private bool _minimizeToTrayOnClose = true;
    private bool _autoStartWatcherOnLaunch = true;
    private bool _openSteamOnStartup = true;
    private string _settingsSavedFeedback = string.Empty;

    // Backups Non-Steam (CorreÃ§Ãµes: CODEX, PLAZA, CPY, HOODLUM, Rune, Voices38, etc.)
    private ObservableCollection<NonSteamGameSave> _nonSteamGames = new();
    private ICollectionView? _filteredNonSteamGames;
    private NonSteamGameSave? _selectedNonSteamGame;
    private string _nonSteamSearchText = string.Empty;
    private string _selectedNonSteamFilter = "Todos";
    private bool _isNonSteamScanning;
    private string _nonSteamScanStatus = "Nenhuma varredura realizada ainda. Clique em 'Escanear Pastas de CorreÃ§Ãµes' para buscar.";
    private ObservableCollection<string> _customNonSteamFolders = new();
    private ObservableCollection<NonSteamSearchLocation> _monitoredSearchLocations = new();
    private bool _showMonitoredLocations;
    private bool _isNonSteamBackingUp;
    private double _nonSteamBackupProgress;
    private string _nonSteamBackupProgressText = string.Empty;
    private string _nonSteamBackupResultSummary = string.Empty;

    // Eventos para o serviÃ§o de bandeja
    public event Action<string, string, bool>? NotificationRequested;
    public event Action<bool>? WatcherStatusChanged;

    public MainViewModel()
    {
        // Carrega configuraÃ§Ãµes persistidas do usuÃ¡rio (com suporte transparente entre perfis Windows)
        var userSettings = AppSettingsService.LoadSettings();
        _batchBackupOutputDir = AppSettingsService.GetEffectiveBackupDirectory();

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
        _autoUploadToCloudOnBackup = userSettings.AutoUploadToCloudOnBackup;
        _autoUploadToCloudNonSteamBackup = userSettings.AutoUploadToCloudNonSteamBackup;
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
        GoToGamesTabCommand = new RelayCommand(() => SelectedTabIndex = 0);

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
        UploadLocalBackupToCloudCommand = new RelayCommand(async item => await UploadLocalBackupToCloudAsync(item as LocalBackupInfo ?? SelectedLocalBackup), _ => IsGoogleDriveConnected && !IsLocalBackupsLoading);
        BrowseRestoreZipCommand = new RelayCommand(async () => await BrowseRestoreZipAsync());
        BrowseMultipleRestoreZipsCommand = new RelayCommand(async () => await BrowseMultipleRestoreZipsAsync());
        StartAutoRestoreCommand = new RelayCommand(async () => await StartAutoRestoreAsync(), () => (RestoreManifest != null || _restoreZipQueue.Count > 0 || SelectedLocalBackup != null) && !IsRestoring);
        StartCustomRestoreCommand = new RelayCommand(async () => await StartCustomRestoreAsync(), () => RestoreManifest != null && !IsRestoring);
        SyncAchievementsCommand = new RelayCommand(async () => await SyncAchievementsAsync(), () => HasAchievementsInBackup && SelectedTargetSteamAccount != null && !IsRestoring);

        // Comandos Non-Steam (CorreÃ§Ãµes: CODEX, PLAZA, CPY, HOODLUM, Rune, Voices38, etc.)
        ScanNonSteamSavesCommand = new RelayCommand(async () => await ScanNonSteamSavesAsync(), () => !IsNonSteamScanning && !IsNonSteamBackingUp);
        SelectAllNonSteamCommand = new RelayCommand(() => SetSelectionForAllNonSteam(true));
        DeselectAllNonSteamCommand = new RelayCommand(() => SetSelectionForAllNonSteam(false));
        AddCustomNonSteamFolderCommand = new RelayCommand(AddCustomNonSteamFolder);
        RemoveCustomNonSteamFolderCommand = new RelayCommand(folder => RemoveCustomNonSteamFolder(folder as string));
        BackupSelectedNonSteamCommand = new RelayCommand(async () => await StartBatchBackupNonSteamAsync(), () => NonSteamSelectedCount > 0 && !IsNonSteamBackingUp && !IsNonSteamScanning);
        BackupSingleNonSteamCommand = new RelayCommand(async save => await BackupSingleNonSteamAsync(save as NonSteamGameSave ?? SelectedNonSteamGame), _ => SelectedNonSteamGame != null && !IsNonSteamBackingUp);
        UploadNonSteamGameToCloudCommand = new RelayCommand(async save => await UploadNonSteamGameToCloudAsync(save as NonSteamGameSave ?? SelectedNonSteamGame), _ => IsGoogleDriveConnected && !IsNonSteamBackingUp);
        OpenNonSteamFolderCommand = new RelayCommand(folder => OpenFolderInExplorer(folder as string));
        ClearNonSteamSearchCommand = new RelayCommand(() => NonSteamSearchText = string.Empty);
        ToggleMonitoredLocationsCommand = new RelayCommand(() => ShowMonitoredLocations = !ShowMonitoredLocations);

        _filteredNonSteamGames = CollectionViewSource.GetDefaultView(_nonSteamGames);
        _filteredNonSteamGames.Filter = FilterNonSteamGameItem;
        RefreshMonitoredLocations();

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
        OpenOAuthGuideCommand = new RelayCommand(OpenOAuthGuide);
        ExportOAuthGuideCommand = new RelayCommand(ExportOAuthGuide);

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
                ? $" \u2022 Conectado: {SteamInfo.ActiveAccount.PersonaName}"
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

        // Detecta saves de correÃ§Ãµes Non-Steam em segundo plano
        _ = ScanNonSteamSavesAsync();

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

        var currentDir = AppSettingsService.NormalizeDirectoryPath(BatchBackupOutputDir);
        if (string.IsNullOrWhiteSpace(currentDir))
        {
            currentDir = AppSettingsService.GetEffectiveBackupDirectory();
            _batchBackupOutputDir = currentDir;
        }

        try
        {
            if (!Directory.Exists(currentDir)) Directory.CreateDirectory(currentDir);
        }
        catch { }

        var sanitized = string.Join("_", scanned.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        BackupZipPath = Path.Combine(currentDir, $"{sanitized}_{scanned.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        // Vai para a aba de Backup
        SelectedTabIndex = 1;

        IsBusy = false;
        StatusMessage = $"{scanned.TotalSaveFiles} arquivo(s) de save encontrados para {scanned.Name}.";
    }

    private void BrowseBackupOutput()
    {
        var initialDir = !string.IsNullOrWhiteSpace(BackupZipPath) && Directory.Exists(Path.GetDirectoryName(BackupZipPath))
            ? Path.GetDirectoryName(BackupZipPath)!
            : (Directory.Exists(BatchBackupOutputDir) ? BatchBackupOutputDir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        var sfd = new SaveFileDialog
        {
            Title = "Salvar arquivo de backup dos saves",
            Filter = "Arquivo Zip do SteamSaveMigrator (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            FileName = !string.IsNullOrWhiteSpace(BackupZipPath) ? Path.GetFileName(BackupZipPath) : "backup.zip",
            InitialDirectory = initialDir,
            RestoreDirectory = true
        };

        if (sfd.ShowDialog() == true)
        {
            BackupZipPath = sfd.FileName;
            var chosenFolder = Path.GetDirectoryName(sfd.FileName);
            if (!string.IsNullOrWhiteSpace(chosenFolder))
            {
                ApplyBackupDirectoryChange(chosenFolder);
                StatusMessage = $"Pasta de backups atualizada e salva: {chosenFolder}";
            }
        }
    }

    private void BrowseBatchBackupOutputDir()
    {
        var initialDir = Directory.Exists(BatchBackupOutputDir)
            ? BatchBackupOutputDir
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecione a pasta onde os backups locais serÃ£o salvos",
                InitialDirectory = initialDir,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                ApplyBackupDirectoryChange(dialog.FolderName);
                StatusMessage = $"Pasta de backups alterada e salva permanentemente: {dialog.FolderName}";
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
                InitialDirectory = initialDir,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(ofd.FileName);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    ApplyBackupDirectoryChange(dir);
                    StatusMessage = $"Pasta de backups alterada e salva permanentemente: {dir}";
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
        ApplyBackupDirectoryChange(defaultDir);
        StatusMessage = $"Pasta de backups redefinida e salva para o padrÃ£o: {defaultDir}";
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
            BackupProgressText = "Backup local concluÃ­do!";
            BackupResultSummary = $"\u2705 Backup local salvo em: {zipResult} ({ByteSizeFormatter.FormatBytes(fileInfo.Length)})";
            StatusMessage = "Backup local concluÃ­do com sucesso!";

            // Upload automÃ¡tico para a nuvem se configurado e conectado
            if (AutoUploadToCloudOnBackup && IsGoogleDriveConnected)
            {
                BackupProgressText = "Enviando cÃ³pia para o Google Drive...";
                StatusMessage = $"Enviando backup de {BackupGame.Name} para o Google Drive...";
                try
                {
                    var cloudProg = new Progress<double>(pct =>
                    {
                        BackupProgressText = $"Enviando para o Google Drive: {pct:0}%";
                    });

                    await _engine.GoogleDrive.UploadBackupAsync(
                        zipResult,
                        BackupGame.Name,
                        BackupGame.AppId,
                        sourceSteamPersona: srcPersona,
                        sourceSteamId3: srcSteamId,
                        sourceSteamId64: SelectedSourceSteamAccount?.SteamId64,
                        sourceUsername: SelectedSourceWindowsProfile?.Username,
                        progress: cloudProg);

                    BackupProgressText = "Backup local e em nuvem concluÃ­dos com sucesso!";
                    BackupResultSummary += " \u2022 \u2601\uFE0F Salvo tambÃ©m no Google Drive!";
                    StatusMessage = $"Backup de {BackupGame.Name} salvo localmente e no Google Drive!";
                    _ = LoadCloudBackupsAsync();
                }
                catch (Exception cloudEx)
                {
                    BackupResultSummary += $" \u2022 \u26A0\uFE0F Falha no upload para nuvem: {cloudEx.Message}";
                    StatusMessage = $"Backup local salvo, mas falhou ao enviar para nuvem: {cloudEx.Message}";
                }
            }

            _ = LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            BackupProgressText = "Falha no backup.";
            BackupResultSummary = $"\u274C Erro ao criar backup: {ex.Message}";
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
            BatchOverallProgressText = "Backup local em lote finalizado!";
            BatchCurrentGameText = string.Empty;
            BatchBackupSummary = $"\U0001F389 Sucesso! {result.SuccessCount} jogo(s) salvos em '{BatchBackupOutputDir}'. Total compactado: {ByteSizeFormatter.FormatBytes(result.TotalSizeBytesArchived)} ({result.TotalFilesArchived} arquivos).";
            StatusMessage = $"Backup de {result.SuccessCount} jogos concluÃ­do com sucesso!";

            // Upload em lote para o Google Drive se configurado e conectado
            if (AutoUploadToCloudOnBackup && IsGoogleDriveConnected && result.SuccessCount > 0)
            {
                int cloudSuccess = 0;
                var backupFiles = Directory.GetFiles(BatchBackupOutputDir, "*.zip")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(result.SuccessCount * 2)
                    .ToList();

                BatchCurrentGameText = "Enviando cÃ³pias para o Google Drive...";
                for (int i = 0; i < selectedGames.Count; i++)
                {
                    var g = selectedGames[i];
                    var matchingZip = backupFiles.FirstOrDefault(f => Path.GetFileName(f).Contains(g.AppId.ToString()));
                    if (matchingZip != null && File.Exists(matchingZip))
                    {
                        BatchOverallProgressText = $"Enviando para a nuvem ({i + 1}/{selectedGames.Count}): {g.Name}";
                        try
                        {
                            await _engine.GoogleDrive.UploadBackupAsync(
                                matchingZip,
                                g.Name,
                                g.AppId,
                                sourceSteamPersona: srcPersona,
                                sourceSteamId3: srcSteamId,
                                sourceSteamId64: SelectedSourceSteamAccount?.SteamId64,
                                sourceUsername: SelectedSourceWindowsProfile?.Username);
                            cloudSuccess++;
                        }
                        catch { }
                    }
                }

                if (cloudSuccess > 0)
                {
                    BatchBackupSummary += $" \u2022 \u2601\uFE0F {cloudSuccess} backup(s) sincronizados no Google Drive!";
                    StatusMessage += $" ({cloudSuccess} salvos no Google Drive)";
                    _ = LoadCloudBackupsAsync();
                }
            }

            _ = LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            BatchBackupSummary = $"\u274C Erro durante o backup em lote: {ex.Message}";
        }
        finally
        {
            IsBatchBackingUp = false;
        }
    }

    private async Task BrowseRestoreZipAsync()
    {
        var initialDir = Directory.Exists(BatchBackupOutputDir)
            ? BatchBackupOutputDir
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var ofd = new OpenFileDialog
        {
            Title = "Selecione o arquivo de backup (.zip)",
            Filter = "Arquivo de Backup (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            InitialDirectory = initialDir,
            RestoreDirectory = true,
            Multiselect = true
        };

        if (ofd.ShowDialog() == true && ofd.FileNames.Length > 0)
        {
            var chosenDir = Path.GetDirectoryName(ofd.FileNames[0]);
            if (!string.IsNullOrWhiteSpace(chosenDir) && Directory.Exists(chosenDir))
            {
                if (!string.Equals(chosenDir, BatchBackupOutputDir, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBackupDirectoryChange(chosenDir);
                }
            }

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
                    RestoreSourceSteamDetails = $"SteamID3: {srcSteamId3.Value} \u2022 SteamID64: {srcSteamId64}";
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
                    RestoreAchievementsSummary = $"\U0001F3C6 {unlocked} de {manifest.Achievements.Count} conquistas obtidas no backup (serÃ£o vinculadas Ã  conta de destino)";
                }
                else
                {
                    RestoreAchievements = new ObservableCollection<GameAchievementInfo>();
                    HasAchievementsInBackup = false;
                    RestoreAchievementsSummary = string.Empty;
                }

                IsAutoDetected = true;
                UpdateAutoMigrationDescription();

                var achCountText = HasAchievementsInBackup ? $" \u2022 {RestoreAchievements.Count(a => a.IsUnlocked)} conquistas prontas" : "";
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
            steamMigrationText = $"\n\U0001F3AE MigraÃ§Ã£o de Perfil Steam:\n" +
                                 $"\u2022 Conta Destino: {SelectedTargetSteamAccount.PersonaName} (SteamID3: {SelectedTargetSteamAccount.SteamId3}){activeTag}\n" +
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
            AutoMigrationDescription = $"\u26A1 RestauraÃ§Ã£o em lote de {RestoreZipQueue.Count} backups com 1 clique!\n" +
                                       $"\u2022 UsuÃ¡rio Windows Destino: '{effectiveTargetUser}'" +
                                       steamMigrationText + achTag;
        }
        else if (RestoreManifest != null)
        {
            AutoMigrationDescription = $"\U0001F3AF MigraÃ§Ã£o AutomÃ¡tica Detectada:\n" +
                                       $"\u2022 UsuÃ¡rio Windows: {RestoreSourceUser} \u2794Â {effectiveTargetUser}\n" +
                                       $"\u2022 Pasta Perfil: {RestoreSourceProfile} \u2794Â {effectiveTargetProfile}" +
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
                    ? $" \u2022 \U0001F3AE Perfil Steam migrado com sucesso para '{SelectedTargetSteamAccount?.PersonaName ?? targetSteamId?.ToString()}'"
                    : string.Empty;

                string achMsg = string.Empty;
                if (result.AchievementSyncDetails != null)
                {
                    if (result.AchievementSyncDetails.LiveSyncedViaSteamworks)
                    {
                        achMsg = $" \u2022 \U0001F3C6 {result.AchievementSyncDetails.SyncedCount} conquista(s) ativadas ao vivo na Steam!";
                    }
                    else if (result.AchievementSyncDetails.ActiveConnectedSteamId3.HasValue)
                    {
                        achMsg = $" \u2022 \u26A0\uFE0F Conquistas: Steam conectada em '{result.AchievementSyncDetails.ActiveConnectedPersona}'. Entre na conta de destino na Steam e use 'Sincronizar Conquistas'.";
                    }
                    else
                    {
                        achMsg = $" \u2022 \U0001F3C6 {result.AchievementSyncDetails.SyncedCount} conquista(s) sincronizadas em cache local.";
                    }
                }
                else if (result.AchievementsSynced > 0)
                {
                    achMsg = $" \u2022 \U0001F3C6 {result.AchievementsSynced} conquista(s) vinculadas e sincronizadas Ã  conta Steam!";
                }
                else if (result.Achievements != null && result.Achievements.Count > 0)
                {
                    achMsg = $" \u2022 \U0001F3C6 {result.Achievements.Count(a => a.IsUnlocked)} conquista(s) detectadas no save.";
                }

                RestoreProgressText = RestoreDryRun ? "SimulaÃ§Ã£o concluÃ­da com sucesso!" : "RestauraÃ§Ã£o automÃ¡tica concluÃ­da com sucesso!";
                var steamRunningWarning = !RestoreDryRun && System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
                    ? " \u26A0\uFE0FÂ AVISO: A Steam estÃ¡ em execuÃ§Ã£o. Feche e reabra a Steam para que ela recarregue os saves e conquistas sincronizadas antes de abrir o jogo!"
                    : "";

                RestoreResultSummary = RestoreDryRun
                    ? $"\U0001F50D  SimulaÃ§Ã£o OK: {result.SuccessCount} arquivos mapeados para '{targetUser}'{steamSuccess}{achMsg} sem gravar no disco."
                    : $"\U0001F389 Sucesso! {result.SuccessCount} arquivo(s) restaurados no perfil do usuÃ¡rio '{targetUser}'{steamSuccess}{achMsg}!{steamRunningWarning}";
            }
            else
            {
                RestoreProgressText = "RestauraÃ§Ã£o finalizada com pendÃªncias.";
                RestoreResultSummary = $"\u26A0\uFE0FÂ Finalizado com {result.ErrorCount} erro(s). Veja os detalhes abaixo.";
            }
        }
        catch (Exception ex)
        {
            RestoreProgressText = "Erro na restauraÃ§Ã£o.";
            RestoreResultSummary = $"\u274C Erro crÃ­tico: {ex.Message}";
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
                RestoreResultSummary = $"\U0001F389 Sucesso! Todos os {batchResult.SuccessCount} backups foram restaurados automaticamente ({batchResult.TotalFilesRestored} arquivos migrados para '{Environment.UserName}'{steamBatchMsg}).";
            }
            else
            {
                RestoreProgressText = "RestauraÃ§Ã£o em lote finalizada com erros.";
                RestoreResultSummary = $"\u26A0\uFE0FÂ ConcluÃ­do: {batchResult.SuccessCount} sucessos, {batchResult.FailureCount} falhas.";
            }
        }
        catch (Exception ex)
        {
            RestoreProgressText = "Erro na restauraÃ§Ã£o em lote.";
            RestoreResultSummary = $"\u274C Erro: {ex.Message}";
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
            var normalized = AppSettingsService.NormalizeDirectoryPath(value);
            if (SetProperty(ref _batchBackupOutputDir, normalized))
            {
                ApplyBackupDirectoryChange(normalized);
            }
        }
    }

    public string LocalBackupsDirectoryDescription => $"Exibindo {FilteredLocalBackupsCount} backup(s) locais em: {BatchBackupOutputDir}";

    private void ApplyBackupDirectoryChange(string newDir)
    {
        var normalized = AppSettingsService.NormalizeDirectoryPath(newDir);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        _batchBackupOutputDir = normalized;

        try
        {
            if (!Directory.Exists(normalized))
            {
                Directory.CreateDirectory(normalized);
            }
        }
        catch { }

        // Persiste imediatamente a alteraÃ§Ã£o de pasta de forma atÃ´mica
        AppSettingsService.UpdateCustomBackupDirectory(normalized);
        SaveCurrentSettings();

        if (BackupGame != null)
        {
            var sanitized = string.Join("_", BackupGame.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            BackupZipPath = Path.Combine(normalized, $"{sanitized}_{BackupGame.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        }

        if (_gameWatcher != null)
        {
            _gameWatcher.UpdateOutputDir(normalized);
        }

        OnPropertyChanged(nameof(BatchBackupOutputDir));
        OnPropertyChanged(nameof(LocalBackupsDirectoryDescription));

        // Recarrega a lista de backups locais da aba Restaurar imediatamente
        _ = LoadLocalBackupsAsync();
    }

    public void SaveCurrentSettings()
    {
        try
        {
            var s = new UserSettings
            {
                CustomBackupDirectory = _batchBackupOutputDir,
                AutoStartWatcherOnLaunch = _autoStartWatcherOnLaunch,
                WatcherAutoBackup = _watcherAutoBackup,
                WatcherAutoUpload = _watcherAutoUpload,
                AutoUploadToCloudOnBackup = _autoUploadToCloudOnBackup,
                AutoUploadToCloudNonSteamBackup = _autoUploadToCloudNonSteamBackup,
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
    public ICommand GoToGamesTabCommand { get; }

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
    public ICommand UploadLocalBackupToCloudCommand { get; }
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
    public ICommand OpenOAuthGuideCommand { get; }
    public ICommand ExportOAuthGuideCommand { get; }
    public ICommand ToggleGameWatcherCommand { get; }
    public ICommand ClearWatcherLogsCommand { get; }

    // Comandos Non-Steam (CorreÃ§Ãµes: CODEX, PLAZA, CPY, HOODLUM, Rune, Voices38, etc.)
    public ICommand ScanNonSteamSavesCommand { get; }
    public ICommand SelectAllNonSteamCommand { get; }
    public ICommand DeselectAllNonSteamCommand { get; }
    public ICommand AddCustomNonSteamFolderCommand { get; }
    public ICommand RemoveCustomNonSteamFolderCommand { get; }
    public ICommand BackupSelectedNonSteamCommand { get; }
    public ICommand BackupSingleNonSteamCommand { get; }
    public ICommand UploadNonSteamGameToCloudCommand { get; }
    public ICommand OpenNonSteamFolderCommand { get; }
    public ICommand ClearNonSteamSearchCommand { get; }
    public ICommand ToggleMonitoredLocationsCommand { get; }

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

    public bool AutoUploadToCloudOnBackup
    {
        get => _autoUploadToCloudOnBackup;
        set
        {
            if (SetProperty(ref _autoUploadToCloudOnBackup, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public bool AutoUploadToCloudNonSteamBackup
    {
        get => _autoUploadToCloudNonSteamBackup;
        set
        {
            if (SetProperty(ref _autoUploadToCloudNonSteamBackup, value))
            {
                SaveCurrentSettings();
            }
        }
    }

    public string OAuthGuideText => _engine.GoogleDrive.GetOAuthSetupGuideText();

    public void SaveSettingsExplicitly()
    {
        SaveCurrentSettings();
        ApplyBackupDirectoryChange(BatchBackupOutputDir);
        SettingsSavedFeedback = "\u2705 ConfiguraÃ§Ãµes e pasta salvas e ativas!";
        StatusMessage = $"ConfiguraÃ§Ãµes salvas com sucesso! Pasta de backups ativa: {BatchBackupOutputDir}";

        _ = Task.Delay(4000).ContinueWith(_ =>
        {
            SettingsSavedFeedback = string.Empty;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void OpenOAuthGuide()
    {
        try
        {
            var guideFile = _engine.GoogleDrive.ExportOAuthSetupGuideToFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{guideFile}\"",
                UseShellExecute = true
            });
            StatusMessage = $"Guia passo a passo exportado e aberto no Bloco de Notas: {guideFile}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao abrir guia OAuth: {ex.Message}";
        }
    }

    public void ExportOAuthGuide()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var guideFile = _engine.GoogleDrive.ExportOAuthSetupGuideToFile(desktopDir);
            StatusMessage = $"Guia de configuraÃ§Ã£o salvo com sucesso na Ãrea de Trabalho: {Path.GetFileName(guideFile)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao salvar guia OAuth: {ex.Message}";
        }
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
                    CredentialsStatusText = "\u2705 Arquivo credentials.json importado com sucesso!";
                    GoogleDriveStatusText = "Credenciais configuradas com sucesso! Clique em 'Conectar ao Google Drive'.";
                    StatusMessage = "Credenciais do Google Drive configuradas!";
                }
                else
                {
                    CredentialsStatusText = "\u274C NÃ£o foi possÃ­vel extrair client_id e client_secret do arquivo selecionado.";
                }
            }
            catch (Exception ex)
            {
                CredentialsStatusText = $"\u274C Erro ao importar: {ex.Message}";
            }
        }
    }

    private void SaveCustomCredentials()
    {
        if (string.IsNullOrWhiteSpace(CustomClientId) || string.IsNullOrWhiteSpace(CustomClientSecret))
        {
            CredentialsStatusText = "\u274C Preencha tanto o Client ID quanto o Client Secret.";
            return;
        }

        try
        {
            _engine.GoogleDrive.ConfigureClientSecrets(CustomClientId.Trim(), CustomClientSecret.Trim());
            HasGoogleCredentials = true;
            CredentialsStatusText = "\u2705 Credenciais salvas com sucesso!";
            GoogleDriveStatusText = "Credenciais configuradas! Pronto para conectar.";
            StatusMessage = "Credenciais do Google Drive salvas com sucesso!";
        }
        catch (Exception ex)
        {
            CredentialsStatusText = $"\u274C Erro ao salvar credenciais: {ex.Message}";
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
                    ? " \u26A0\uFE0FÂ AVISO: A Steam estÃ¡ em execuÃ§Ã£o. Feche e reabra a Steam para que ela recarregue os saves sincronizados antes de abrir o jogo!"
                    : "";

                var achMsg = res.AchievementsSynced > 0
                    ? $" \u2022 \U0001F3C6 {res.AchievementsSynced} conquista(s) vinculadas e sincronizadas Ã  conta Steam!"
                    : (res.Achievements != null && res.Achievements.Count > 0 ? $" \u2022 \U0001F3C6 {res.Achievements.Count(a => a.IsUnlocked)} conquista(s) sincronizadas." : "");

                CloudResultSummary = $"\U0001F389 Sucesso! {res.SuccessCount} arquivo(s) restaurados no perfil Windows '{targetUserMsg}'{steamInfoMsg}{achMsg} com perfeiÃ§Ã£o!{steamRunningWarning}";
                StatusMessage = "Backup da nuvem restaurado com sucesso!";
            }
            else
            {
                CloudResultSummary = $"\u274C Falha ao restaurar: {res.ErrorCount} erro(s).";
                StatusMessage = "Erro ao restaurar backup da nuvem.";
            }
        }
        catch (Exception ex)
        {
            CloudResultSummary = $"\u274C Erro inesperado: {ex.Message}";
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
            WatcherStatusBadge = "\U0001F7E2 Ativo e Monitorando";
            AddWatcherLog("\U0001F6D1 Monitor de jogos parado pelo usuÃ¡rio.");
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
            WatcherStatusBadge = "\U0001F7E2 Ativo e Monitorando";
            AddWatcherLog($"\U0001F7E2 Monitor iniciado! VigiarÃ¡ processos Steam a cada {config.PollIntervalSeconds}s.");
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
                    logLine = $"[{time}] \U0001F3AE JOGO INICIADO: {gameName} (AppID: {e.Game?.AppId})";
                    WatcherLastEventText = $"Jogo em execuÃ§Ã£o: {gameName}";
                    break;

                case GameWatcherEventType.GameExited:
                    logLine = $"[{time}] \u23F9\uFE0F JOGO FECHADO: {gameName}! Aguardando dados de save...";
                    WatcherLastEventText = $"Jogo fechado: {gameName}. Preparando backup...";
                    NotificationRequested?.Invoke("Jogo Steam Fechado", $"\U0001F3AE {gameName} foi fechado. Realizando backup automÃ¡tico...", false);
                    break;

                case GameWatcherEventType.BackupStarted:
                    logLine = $"[{time}] \U0001F4BE BACKUP INICIADO: Compactando saves de {gameName}...";
                    break;

                case GameWatcherEventType.BackupCompleted:
                    logLine = $"[{time}] \u2705 BACKUP LOCAL PRONTO: {Path.GetFileName(e.LocalZipPath ?? "")}";
                    WatcherLastEventText = $"Backup local salvo: {Path.GetFileName(e.LocalZipPath ?? "")}";
                    _ = LoadLocalBackupsAsync();
                    break;

                case GameWatcherEventType.CloudUploadStarted:
                    logLine = $"[{time}] \u2601\uFE0F UPLOAD NUVEM: Enviando saves de {gameName} para o Google Drive...";
                    break;

                case GameWatcherEventType.CloudUploadCompleted:
                    logLine = $"[{time}] \U0001F389 SINCRONIZADO NO GOOGLE DRIVE: {gameName} salvo na nuvem!";
                    WatcherLastEventText = $"Salvo na nuvem: {gameName}";
                    NotificationRequested?.Invoke("Backup na Nuvem ConcluÃ­do", $"\U0001F389 Saves de {gameName} sincronizados no Google Drive com sucesso!", true);
                    _ = LoadCloudBackupsAsync();
                    break;

                case GameWatcherEventType.Error:
                    logLine = $"[{time}] \u274C ERRO: {e.Message}";
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

    public async Task UploadLocalBackupToCloudAsync(LocalBackupInfo? item)
    {
        var target = item ?? SelectedLocalBackup;
        if (target == null || !File.Exists(target.FilePath))
        {
            StatusMessage = "Selecione um arquivo de backup local vÃ¡lido para enviar.";
            return;
        }

        if (!IsGoogleDriveConnected)
        {
            StatusMessage = "Google Drive nÃ£o conectado. Conecte sua conta na aba 'Nuvem' antes de enviar.";
            return;
        }

        IsLocalBackupsLoading = true;
        StatusMessage = $"Enviando '{target.GameName}' para o Google Drive...";
        try
        {
            var progress = new Progress<double>(pct =>
            {
                StatusMessage = $"Enviando '{target.GameName}' para a nuvem: {pct:0}%";
            });

            await _engine.GoogleDrive.UploadBackupAsync(
                target.FilePath,
                target.GameName,
                target.AppId,
                sourceSteamPersona: target.SourceSteamPersona,
                sourceSteamId3: target.SourceSteamId3,
                sourceSteamId64: target.SourceSteamId64,
                sourceUsername: target.SourceUsername,
                achievementsCount: target.AchievementsCount,
                achievementsUnlockedCount: target.AchievementsUnlockedCount,
                progress: progress);

            StatusMessage = $"âœ… Backup de '{target.GameName}' enviado com sucesso para o Google Drive!";
            _ = LoadCloudBackupsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"âŒ Falha ao enviar para o Google Drive: {ex.Message}";
        }
        finally
        {
            IsLocalBackupsLoading = false;
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
            StatusMessage = "\U0001F3C6 Conectando Ã  Steam e sincronizando conquistas...";
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
            RestoreResultSummary = $"\u274C Erro ao sincronizar conquistas: {ex.Message}";
        }
    }

    // =========================================================================
    // ABA NON-STEAM: PROPRIEDADES E MÃ‰TODOS PARA JOGOS E CORREÃ‡Ã•ES (CODEX, RUNE, ETC.)
    // =========================================================================

    public ObservableCollection<NonSteamGameSave> NonSteamGames
    {
        get => _nonSteamGames;
        set
        {
            if (SetProperty(ref _nonSteamGames, value))
            {
                _filteredNonSteamGames = CollectionViewSource.GetDefaultView(_nonSteamGames);
                _filteredNonSteamGames.Filter = FilterNonSteamGameItem;
                OnPropertyChanged(nameof(FilteredNonSteamGames));
                OnPropertyChanged(nameof(NonSteamTotalCount));
                OnPropertyChanged(nameof(NonSteamSelectedCount));
                OnPropertyChanged(nameof(NonSteamTotalSizeFormatted));
                OnPropertyChanged(nameof(FilteredNonSteamGamesCount));
            }
        }
    }

    public ICollectionView? FilteredNonSteamGames => _filteredNonSteamGames;

    public NonSteamGameSave? SelectedNonSteamGame
    {
        get => _selectedNonSteamGame;
        set
        {
            if (SetProperty(ref _selectedNonSteamGame, value))
            {
                (BackupSingleNonSteamCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NonSteamSearchText
    {
        get => _nonSteamSearchText;
        set
        {
            if (SetProperty(ref _nonSteamSearchText, value))
            {
                _filteredNonSteamGames?.Refresh();
                OnPropertyChanged(nameof(FilteredNonSteamGamesCount));
            }
        }
    }

    public string SelectedNonSteamFilter
    {
        get => _selectedNonSteamFilter;
        set
        {
            if (SetProperty(ref _selectedNonSteamFilter, value))
            {
                _filteredNonSteamGames?.Refresh();
                OnPropertyChanged(nameof(FilteredNonSteamGamesCount));
            }
        }
    }

    public ObservableCollection<string> NonSteamFilterOptions { get; } = new()
    {
        "Todos", "CODEX", "Rune", "PLAZA", "CPY", "HOODLUM", "Voices38", "Goldberg", "EMPRESS", "Outros"
    };

    public bool IsNonSteamScanning
    {
        get => _isNonSteamScanning;
        set
        {
            if (SetProperty(ref _isNonSteamScanning, value))
            {
                (ScanNonSteamSavesCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BackupSelectedNonSteamCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NonSteamScanStatus
    {
        get => _nonSteamScanStatus;
        set => SetProperty(ref _nonSteamScanStatus, value);
    }

    public ObservableCollection<string> CustomNonSteamFolders
    {
        get => _customNonSteamFolders;
        set => SetProperty(ref _customNonSteamFolders, value);
    }

    public ObservableCollection<NonSteamSearchLocation> MonitoredSearchLocations
    {
        get => _monitoredSearchLocations;
        set => SetProperty(ref _monitoredSearchLocations, value);
    }

    public bool ShowMonitoredLocations
    {
        get => _showMonitoredLocations;
        set => SetProperty(ref _showMonitoredLocations, value);
    }

    public int NonSteamTotalCount => _nonSteamGames.Count;

    public int NonSteamSelectedCount => _nonSteamGames.Count(g => g.IsSelected);

    public int FilteredNonSteamGamesCount => _filteredNonSteamGames?.Cast<object>().Count() ?? 0;

    public string NonSteamTotalSizeFormatted
    {
        get
        {
            long total = _nonSteamGames.Where(g => g.IsSelected).Sum(g => g.TotalSizeBytes);
            if (total <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = total;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:0.##} {units[unitIndex]}";
        }
    }

    public bool IsNonSteamBackingUp
    {
        get => _isNonSteamBackingUp;
        set
        {
            if (SetProperty(ref _isNonSteamBackingUp, value))
            {
                (BackupSelectedNonSteamCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BackupSingleNonSteamCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ScanNonSteamSavesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public double NonSteamBackupProgress
    {
        get => _nonSteamBackupProgress;
        set => SetProperty(ref _nonSteamBackupProgress, value);
    }

    public string NonSteamBackupProgressText
    {
        get => _nonSteamBackupProgressText;
        set => SetProperty(ref _nonSteamBackupProgressText, value);
    }

    public string NonSteamBackupResultSummary
    {
        get => _nonSteamBackupResultSummary;
        set => SetProperty(ref _nonSteamBackupResultSummary, value);
    }

    private bool FilterNonSteamGameItem(object obj)
    {
        if (obj is not NonSteamGameSave item) return false;

        // Filtro por categoria / correÃ§Ã£o
        if (!string.IsNullOrWhiteSpace(SelectedNonSteamFilter) && !SelectedNonSteamFilter.Equals("Todos", StringComparison.OrdinalIgnoreCase))
        {
            if (SelectedNonSteamFilter.Equals("Outros", StringComparison.OrdinalIgnoreCase))
            {
                var mainCracks = new[] { "CODEX", "Rune", "PLAZA", "CPY", "HOODLUM", "Voices38", "Goldberg", "EMPRESS" };
                if (mainCracks.Any(c => item.CrackSource.Equals(c, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
            else if (!item.CrackSource.Equals(SelectedNonSteamFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Filtro por texto de pesquisa
        if (string.IsNullOrWhiteSpace(NonSteamSearchText)) return true;

        var term = NonSteamSearchText.Trim();
        if (item.GameName.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.AppId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.CrackSource.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.SavePath.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    public async Task ScanNonSteamSavesAsync()
    {
        if (IsNonSteamScanning) return;

        IsNonSteamScanning = true;
        NonSteamScanStatus = "Escaneando diretÃ³rios de correÃ§Ãµes e emuladores Steam...";

        var progress = new Progress<string>(msg =>
        {
            NonSteamScanStatus = msg;
        });

        try
        {
            var detected = await _engine.DetectNonSteamSavesAsync(CustomNonSteamFolders, progress);

            // Vincula o evento PropertyChanged em cada item para atualizar seleÃ§Ã£o em tempo real
            foreach (var item in detected)
            {
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NonSteamGameSave.IsSelected))
                    {
                        NotifyNonSteamSelectionChanged();
                    }
                };
            }

            NonSteamGames = new ObservableCollection<NonSteamGameSave>(detected);
            if (SelectedNonSteamGame == null && NonSteamGames.Count > 0)
            {
                SelectedNonSteamGame = NonSteamGames[0];
            }

            RefreshMonitoredLocations();
            NotifyNonSteamSelectionChanged();

            NonSteamScanStatus = detected.Count > 0
                ? $"Varredura concluÃ­da: {detected.Count} jogo(s) com saves identificados nas pastas de correÃ§Ãµes."
                : "Varredura concluÃ­da: Nenhum save de correÃ§Ãµes Steam foi encontrado nas pastas padrÃ£o.";
        }
        catch (Exception ex)
        {
            NonSteamScanStatus = $"Erro durante a varredura: {ex.Message}";
        }
        finally
        {
            IsNonSteamScanning = false;
        }
    }

    public void SetSelectionForAllNonSteam(bool select)
    {
        foreach (var item in NonSteamGames)
        {
            item.IsSelected = select;
        }
        _filteredNonSteamGames?.Refresh();
        NotifyNonSteamSelectionChanged();
    }

    public void NotifyNonSteamSelectionChanged()
    {
        OnPropertyChanged(nameof(NonSteamSelectedCount));
        OnPropertyChanged(nameof(NonSteamTotalSizeFormatted));
        (BackupSelectedNonSteamCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void AddCustomNonSteamFolder()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecione uma pasta com saves de jogos para verificar",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var folder = Path.GetFullPath(dialog.FolderName);
                if (!CustomNonSteamFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    CustomNonSteamFolders.Add(folder);
                    _ = ScanNonSteamSavesAsync();
                }
            }
        }
        catch
        {
            var ofd = new OpenFileDialog
            {
                Title = "Selecione uma pasta para verificar saves",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Selecionar esta pasta"
            };

            if (ofd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(ofd.FileName);
                if (!string.IsNullOrWhiteSpace(dir) && !CustomNonSteamFolders.Contains(dir, StringComparer.OrdinalIgnoreCase))
                {
                    CustomNonSteamFolders.Add(dir);
                    _ = ScanNonSteamSavesAsync();
                }
            }
        }
    }

    public void RemoveCustomNonSteamFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        CustomNonSteamFolders.Remove(folder);
        _ = ScanNonSteamSavesAsync();
    }

    public async Task StartBatchBackupNonSteamAsync()
    {
        var selected = NonSteamGames.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0 || IsNonSteamBackingUp) return;

        IsNonSteamBackingUp = true;
        NonSteamBackupProgress = 0;
        NonSteamBackupProgressText = $"Iniciando backup em lote de {selected.Count} jogo(s) Non-Steam...";
        NonSteamBackupResultSummary = string.Empty;

        var currentOutputDir = AppSettingsService.NormalizeDirectoryPath(BatchBackupOutputDir);
        if (string.IsNullOrWhiteSpace(currentOutputDir))
        {
            currentOutputDir = AppSettingsService.GetEffectiveBackupDirectory();
            _batchBackupOutputDir = currentOutputDir;
        }

        try
        {
            if (!Directory.Exists(currentOutputDir))
            {
                Directory.CreateDirectory(currentOutputDir);
            }
        }
        catch { }

        var progress = new Progress<BatchBackupProgressReport>(report =>
        {
            if (report.TotalGames > 0)
            {
                NonSteamBackupProgress = (double)report.CurrentGameIndex / report.TotalGames * 100;
                NonSteamBackupProgressText = $"[{report.CurrentGameIndex}/{report.TotalGames}] Compactando {report.CurrentGameName}...";
            }
        });

        try
        {
            var result = await _engine.BackupMultipleNonSteamGamesAsync(
                selected,
                currentOutputDir,
                null,
                progress);

            NonSteamBackupProgress = 100;
            NonSteamBackupProgressText = "Backup em lote concluÃ­do com sucesso!";
            NonSteamBackupResultSummary = $"âœ… {result.SuccessCount} jogo(s) salvos com sucesso em '{currentOutputDir}'. Total: {result.TotalSizeBytesArchived / (1024.0 * 1024.0):F2} MB compactados.";
            StatusMessage = $"Backup em lote Non-Steam concluÃ­do: {result.SuccessCount} jogos salvos.";

            // Upload automÃ¡tico para a nuvem se configurado e conectado
            if (AutoUploadToCloudNonSteamBackup && IsGoogleDriveConnected && result.SuccessCount > 0)
            {
                int cloudSuccess = 0;
                NonSteamBackupProgressText = "Enviando cÃ³pias Non-Steam para o Google Drive...";
                var filesToUpload = result.GeneratedZipFiles != null && result.GeneratedZipFiles.Count > 0
                    ? result.GeneratedZipFiles
                    : Directory.GetFiles(currentOutputDir, "*.zip")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .Take(result.SuccessCount)
                        .ToList();

                for (int i = 0; i < filesToUpload.Count; i++)
                {
                    var zipPath = filesToUpload[i];
                    if (File.Exists(zipPath))
                    {
                        var matchingGame = selected.FirstOrDefault(s => zipPath.Contains(s.AppId.ToString())) ?? selected.ElementAtOrDefault(i);
                        var gName = matchingGame?.GameName ?? Path.GetFileNameWithoutExtension(zipPath);
                        var aId = matchingGame?.AppId ?? 0;
                        var crack = matchingGame?.CrackSource ?? "NonSteam";

                        NonSteamBackupProgressText = $"Enviando para o Google Drive ({i + 1}/{filesToUpload.Count}): {gName} ({crack})";
                        try
                        {
                            await _engine.GoogleDrive.UploadBackupAsync(
                                zipPath,
                                $"{gName} ({crack})",
                                aId,
                                sourceSteamPersona: crack,
                                sourceUsername: Environment.UserName);
                            cloudSuccess++;
                        }
                        catch { }
                    }
                }

                if (cloudSuccess > 0)
                {
                    NonSteamBackupResultSummary += $" \u2022 â˜ï¸ {cloudSuccess} backup(s) sincronizados no Google Drive!";
                    StatusMessage += $" ({cloudSuccess} salvos no Google Drive)";
                    _ = LoadCloudBackupsAsync();
                }
            }

            // Atualiza a lista de backups locais da aba Restaurar Saves
            await LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            NonSteamBackupProgressText = "Erro durante o backup.";
            NonSteamBackupResultSummary = $"âŒ Falha ao realizar backup em lote: {ex.Message}";
            StatusMessage = $"Erro no backup Non-Steam: {ex.Message}";
        }
        finally
        {
            IsNonSteamBackingUp = false;
        }
    }

    public async Task BackupSingleNonSteamAsync(NonSteamGameSave? save)
    {
        if (save == null || IsNonSteamBackingUp) return;

        var currentDir = AppSettingsService.NormalizeDirectoryPath(BatchBackupOutputDir);
        if (string.IsNullOrWhiteSpace(currentDir))
        {
            currentDir = AppSettingsService.GetEffectiveBackupDirectory();
            _batchBackupOutputDir = currentDir;
        }

        try
        {
            if (!Directory.Exists(currentDir)) Directory.CreateDirectory(currentDir);
        }
        catch { }

        var sanitized = string.Join("_", save.GameName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var defaultZipName = $"{sanitized}_{save.CrackSource}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

        var sfd = new SaveFileDialog
        {
            Title = $"Salvar Backup de {save.GameName} ({save.CrackSource})",
            Filter = "Arquivo de Backup SteamSave (*.zip)|*.zip",
            InitialDirectory = currentDir,
            FileName = defaultZipName
        };

        if (sfd.ShowDialog() != true) return;

        IsNonSteamBackingUp = true;
        NonSteamBackupProgress = 0;
        NonSteamBackupProgressText = $"Iniciando backup de {save.GameName}...";

        var progress = new Progress<BackupProgressReport>(report =>
        {
            if (report.TotalFiles > 0)
            {
                NonSteamBackupProgress = (double)report.CurrentFileIndex / report.TotalFiles * 100;
                NonSteamBackupProgressText = $"Salvando ({report.CurrentFileIndex}/{report.TotalFiles}): {report.CurrentFileName}";
            }
        });

        try
        {
            await _engine.BackupNonSteamGameAsync(save, sfd.FileName, null, progress);
            NonSteamBackupProgress = 100;
            NonSteamBackupProgressText = "Backup individual concluÃ­do com sucesso!";
            NonSteamBackupResultSummary = $"âœ… Backup de '{save.GameName}' salvo com sucesso em '{sfd.FileName}'.";
            StatusMessage = $"Backup de {save.GameName} salvo.";

            // Upload automÃ¡tico para a nuvem se configurado e conectado
            if (AutoUploadToCloudNonSteamBackup && IsGoogleDriveConnected)
            {
                NonSteamBackupProgressText = "Enviando cÃ³pia Non-Steam para o Google Drive...";
                StatusMessage = $"Enviando backup de {save.GameName} para o Google Drive...";
                try
                {
                    var cloudProg = new Progress<double>(pct =>
                    {
                        NonSteamBackupProgressText = $"Enviando para o Google Drive: {pct:0}%";
                    });

                    await _engine.GoogleDrive.UploadBackupAsync(
                        sfd.FileName,
                        $"{save.GameName} ({save.CrackSource})",
                        save.AppId,
                        sourceSteamPersona: save.CrackSource,
                        sourceUsername: Environment.UserName,
                        progress: cloudProg);

                    NonSteamBackupProgressText = "Backup local e em nuvem concluÃ­dos com sucesso!";
                    NonSteamBackupResultSummary += " \u2022 â˜ï¸ Salvo tambÃ©m no Google Drive!";
                    StatusMessage = $"Backup de {save.GameName} salvo localmente e no Google Drive!";
                    _ = LoadCloudBackupsAsync();
                }
                catch (Exception cloudEx)
                {
                    NonSteamBackupResultSummary += $" \u2022 âš ï¸ Falha no upload para nuvem: {cloudEx.Message}";
                    StatusMessage = $"Backup local salvo, mas falhou ao enviar para nuvem: {cloudEx.Message}";
                }
            }

            await LoadLocalBackupsAsync();
        }
        catch (Exception ex)
        {
            NonSteamBackupProgressText = "Erro durante o backup.";
            NonSteamBackupResultSummary = $"âŒ Erro ao salvar backup: {ex.Message}";
            StatusMessage = $"Erro no backup: {ex.Message}";
        }
        finally
        {
            IsNonSteamBackingUp = false;
        }
    }

    public async Task UploadNonSteamGameToCloudAsync(NonSteamGameSave? save)
    {
        var target = save ?? SelectedNonSteamGame;
        if (target == null)
        {
            StatusMessage = "Selecione um jogo Non-Steam para enviar para o Google Drive.";
            return;
        }

        if (!IsGoogleDriveConnected)
        {
            StatusMessage = "Google Drive nÃ£o conectado. Conecte sua conta na aba 'Nuvem' antes de enviar.";
            return;
        }

        if (IsNonSteamBackingUp) return;

        IsNonSteamBackingUp = true;
        NonSteamBackupProgress = 0;
        NonSteamBackupProgressText = $"Criando pacote para {target.GameName}...";

        var currentDir = AppSettingsService.NormalizeDirectoryPath(BatchBackupOutputDir);
        if (string.IsNullOrWhiteSpace(currentDir))
        {
            currentDir = AppSettingsService.GetEffectiveBackupDirectory();
            _batchBackupOutputDir = currentDir;
        }

        try
        {
            if (!Directory.Exists(currentDir)) Directory.CreateDirectory(currentDir);
        }
        catch { }

        var sanitized = string.Join("_", target.GameName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var outputZip = Path.Combine(currentDir, $"{sanitized}_{target.CrackSource}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        var progress = new Progress<BackupProgressReport>(report =>
        {
            if (report.TotalFiles > 0)
            {
                NonSteamBackupProgress = (double)report.CurrentFileIndex / report.TotalFiles * 50.0;
                NonSteamBackupProgressText = $"Compactando saves ({report.CurrentFileIndex}/{report.TotalFiles}): {report.CurrentFileName}";
            }
        });

        try
        {
            await _engine.BackupNonSteamGameAsync(target, outputZip, null, progress);
            NonSteamBackupProgress = 50;
            NonSteamBackupProgressText = "Enviando arquivo para o Google Drive...";
            StatusMessage = $"Enviando {target.GameName} ({target.CrackSource}) para o Google Drive...";

            var cloudProg = new Progress<double>(pct =>
            {
                NonSteamBackupProgress = 50.0 + (pct * 0.5);
                NonSteamBackupProgressText = $"Enviando para o Google Drive: {pct:0}%";
            });

            await _engine.GoogleDrive.UploadBackupAsync(
                outputZip,
                $"{target.GameName} ({target.CrackSource})",
                target.AppId,
                sourceSteamPersona: target.CrackSource,
                sourceUsername: Environment.UserName,
                progress: cloudProg);

            NonSteamBackupProgress = 100;
            NonSteamBackupProgressText = "Backup e upload para nuvem concluÃ­dos com sucesso!";
            NonSteamBackupResultSummary = $"âœ… '{target.GameName}' salvo com sucesso em '{outputZip}' e sincronizado no Google Drive!";
            StatusMessage = $"Backup de {target.GameName} salvo e enviado para o Google Drive!";

            await LoadLocalBackupsAsync();
            _ = LoadCloudBackupsAsync();
        }
        catch (Exception ex)
        {
            NonSteamBackupProgressText = "Erro durante backup/envio.";
            NonSteamBackupResultSummary = $"âŒ Erro ao enviar para a nuvem: {ex.Message}";
            StatusMessage = $"Erro no envio para nuvem: {ex.Message}";
        }
        finally
        {
            IsNonSteamBackingUp = false;
        }
    }

    public void OpenFolderInExplorer(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        try
        {
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (File.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                StatusMessage = "O caminho especificado nÃ£o existe no sistema.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao abrir pasta no Explorer: {ex.Message}";
        }
    }

    public void RefreshMonitoredLocations()
    {
        try
        {
            var locs = NonSteamSaveDetector.GetKnownSearchLocations();
            MonitoredSearchLocations = new ObservableCollection<NonSteamSearchLocation>(locs);
        }
        catch { }
    }
}







