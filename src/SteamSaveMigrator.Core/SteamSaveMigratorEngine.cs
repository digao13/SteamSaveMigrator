using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Detectors;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using SteamSaveMigrator.Core.Services;

namespace SteamSaveMigrator.Core;

/// <summary>
/// Motor principal de orquestração do SteamSaveMigrator.
/// </summary>
public class SteamSaveMigratorEngine
{
    private readonly BackupService _backupService = new();
    private readonly RestoreService _restoreService = new();

    public IGoogleDriveService GoogleDrive { get; }

    public SteamInstallationInfo SteamInfo { get; private set; } = new();

    public SteamSaveMigratorEngine(IGoogleDriveService? googleDriveService = null)
    {
        GoogleDrive = googleDriveService ?? new GoogleDriveService();
    }

    public SteamInstallationInfo InitializeSteam(string? customSteamPath = null)
    {
        SteamInfo = SteamDetector.Detect(customSteamPath);
        if (SteamInfo.IsFound)
        {
            var libs = GameDetector.DetectLibraries(SteamInfo.SteamPath);
            SteamInfo = SteamInfo with { Libraries = libs };
        }
        return SteamInfo;
    }

    public List<SteamGame> GetInstalledGames(bool includeRedistributables = false)
    {
        if (!SteamInfo.IsFound)
        {
            InitializeSteam();
        }

        if (!SteamInfo.IsFound)
        {
            return new List<SteamGame>();
        }

        return GameDetector.DetectInstalledGames(SteamInfo, includeRedistributables);
    }

    public SteamGame ScanSavesForGame(SteamGame game, string? userProfile = null)
    {
        var locations = SaveLocationDetector.DetectSaveLocations(game, SteamInfo, userProfile);
        return game with { DetectedSaveLocations = locations };
    }

    public SaveScanResult ScanAll(bool includeRedistributables = false, string? userProfile = null)
    {
        var games = GetInstalledGames(includeRedistributables);
        var scannedGames = new List<SteamGame>();

        foreach (var game in games)
        {
            var scanned = ScanSavesForGame(game, userProfile);
            scannedGames.Add(scanned);
        }

        return new SaveScanResult
        {
            SteamInfo = SteamInfo,
            Games = scannedGames
        };
    }

    public Task<string> BackupGameAsync(
        SteamGame game,
        string outputZipPath,
        string? userProfile = null,
        uint? sourceSteamId3 = null,
        string? sourceSteamPersonaName = null,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Se ainda não detectou saves, detecta agora
        var effectiveGame = game.DetectedSaveLocations.Count == 0
            ? ScanSavesForGame(game, userProfile)
            : game;

        var effectiveId3 = sourceSteamId3 ?? SteamInfo.ActiveAccount?.SteamId3;
        var effectivePersona = sourceSteamPersonaName ?? SteamInfo.ActiveAccount?.PersonaName;

        return _backupService.CreateBackupAsync(
            effectiveGame,
            outputZipPath,
            userProfile,
            SteamInfo.SteamPath,
            effectiveId3,
            effectivePersona,
            progress,
            cancellationToken);
    }

    public Task<BackupManifest?> ReadBackupManifestAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        return _restoreService.ReadManifestAsync(zipPath, cancellationToken);
    }

    public Task<RestoreResult> RestoreBackupAsync(
        string zipPath,
        PathConversionOptions? options = null,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new PathConversionOptions();
        if (string.IsNullOrWhiteSpace(effectiveOptions.TargetSteamPath) && SteamInfo.IsFound)
        {
            effectiveOptions = effectiveOptions with { TargetSteamPath = SteamInfo.SteamPath };
        }

        // Se não especificou conta Steam de destino, utiliza a conta conectada nesta máquina por padrão
        if (!effectiveOptions.TargetSteamId3.HasValue && SteamInfo.IsFound && SteamInfo.ActiveAccount != null)
        {
            effectiveOptions = effectiveOptions with
            {
                TargetSteamId3 = SteamInfo.ActiveAccount.SteamId3,
                TargetSteamPersonaName = SteamInfo.ActiveAccount.PersonaName
            };
        }

        return _restoreService.RestoreBackupAsync(zipPath, effectiveOptions, progress, cancellationToken);
    }

    public Task<BatchBackupResult> BackupMultipleGamesAsync(
        IEnumerable<SteamGame> games,
        string outputDirectory,
        string? userProfile = null,
        uint? sourceSteamId3 = null,
        string? sourceSteamPersonaName = null,
        IProgress<BatchBackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Garante que todos os jogos tenham seus saves escaneados
        var preparedGames = new List<SteamGame>();
        foreach (var g in games)
        {
            var effective = g.DetectedSaveLocations.Count == 0
                ? ScanSavesForGame(g, userProfile)
                : g;
            preparedGames.Add(effective);
        }

        var effectiveId3 = sourceSteamId3 ?? SteamInfo.ActiveAccount?.SteamId3;
        var effectivePersona = sourceSteamPersonaName ?? SteamInfo.ActiveAccount?.PersonaName;

        return _backupService.CreateBatchBackupAsync(
            preparedGames,
            outputDirectory,
            userProfile,
            SteamInfo.SteamPath,
            effectiveId3,
            effectivePersona,
            progress,
            cancellationToken);
    }

    public Task<RestoreResult> AutoRestoreAsync(
        string zipPath,
        uint? targetSteamId3 = null,
        string? targetSteamPath = null,
        string? targetUsername = null,
        string? targetUserProfile = null,
        bool overwriteExisting = true,
        bool dryRun = false,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSteamId3 = targetSteamId3 ?? SteamInfo.ActiveAccount?.SteamId3;
        var effectiveSteamPath = targetSteamPath ?? (SteamInfo.IsFound ? SteamInfo.SteamPath : null);

        return _restoreService.AutoRestoreAsync(
            zipPath,
            effectiveSteamId3,
            effectiveSteamPath,
            targetUsername,
            targetUserProfile,
            overwriteExisting,
            dryRun,
            progress,
            cancellationToken);
    }

    public Task<BatchRestoreResult> AutoRestoreBatchAsync(
        IEnumerable<string> zipPaths,
        uint? targetSteamId3 = null,
        string? targetSteamPath = null,
        string? targetUsername = null,
        string? targetUserProfile = null,
        bool overwriteExisting = true,
        bool dryRun = false,
        IProgress<(int CurrentPackage, int TotalPackages, string GameName)>? packageProgress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSteamId3 = targetSteamId3 ?? SteamInfo.ActiveAccount?.SteamId3;
        var effectiveSteamPath = targetSteamPath ?? (SteamInfo.IsFound ? SteamInfo.SteamPath : null);

        return _restoreService.AutoRestoreBatchAsync(
            zipPaths,
            effectiveSteamId3,
            effectiveSteamPath,
            targetUsername,
            targetUserProfile,
            overwriteExisting,
            dryRun,
            packageProgress,
            cancellationToken);
    }

    public static string ConvertPath(string inputPath, string sourceUser, string targetUser)
    {
        var srcProfile = WindowsKnownFolders.GetFoldersForUsername(sourceUser).UserProfile;
        var token = PathVariableConverter.Tokenize(inputPath, srcProfile);
        var options = new PathConversionOptions
        {
            SourceUsername = sourceUser,
            SourceUserProfile = srcProfile,
            TargetUsername = targetUser,
            TargetUserProfile = WindowsKnownFolders.GetFoldersForUsername(targetUser).UserProfile
        };
        return PathVariableConverter.Resolve(token, options);
    }

    /// <summary>
    /// Cria uma instância do monitor de processos para backup automático ao fechar jogos.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public SteamGameWatcherService CreateWatcher(GameWatcherConfig? config = null)
    {
        return new SteamGameWatcherService(this, GoogleDrive, config);
    }

    /// <summary>
    /// Baixa um backup armazenado no Google Drive e executa a restauração automática.
    /// </summary>
    public async Task<RestoreResult> RestoreCloudBackupAsync(
        string fileId,
        uint? targetSteamId3 = null,
        string? targetSteamPath = null,
        string? targetUsername = null,
        string? targetUserProfile = null,
        bool overwriteExisting = true,
        bool dryRun = false,
        IProgress<double>? downloadProgress = null,
        IProgress<RestoreProgressReport>? restoreProgress = null,
        CancellationToken cancellationToken = default)
    {
        var localZip = await GoogleDrive.DownloadBackupAsync(fileId, null, downloadProgress, cancellationToken);
        return await AutoRestoreAsync(
            localZip,
            targetSteamId3,
            targetSteamPath,
            targetUsername,
            targetUserProfile,
            overwriteExisting,
            dryRun,
            restoreProgress,
            cancellationToken);
    }

    /// <summary>
    /// Lista todos os backups locais (.zip) em uma pasta, lendo seus manifestos e conquistas.
    /// </summary>
    public async Task<List<LocalBackupInfo>> GetLocalBackupsAsync(string directory, CancellationToken cancellationToken = default)
    {
        var list = new List<LocalBackupInfo>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return list;

        var zipFiles = Directory.GetFiles(directory, "*.zip", SearchOption.TopDirectoryOnly);
        foreach (var zipPath in zipFiles)
        {
            try
            {
                var fi = new FileInfo(zipPath);
                var manifest = await ReadBackupManifestAsync(zipPath, cancellationToken);
                if (manifest != null)
                {
                    int achCount = manifest.Achievements?.Count ?? 0;
                    int achUnlocked = manifest.Achievements?.Count(a => a.IsUnlocked) ?? 0;

                    list.Add(new LocalBackupInfo
                    {
                        FilePath = zipPath,
                        FileName = fi.Name,
                        GameName = !string.IsNullOrWhiteSpace(manifest.GameName) ? manifest.GameName : Path.GetFileNameWithoutExtension(fi.Name),
                        AppId = manifest.AppId,
                        FileSizeBytes = fi.Length,
                        CreatedTime = fi.LastWriteTime,
                        SourceUsername = manifest.SourceUsername,
                        SourceSteamPersona = manifest.SourceSteamPersonaName ?? string.Empty,
                        SourceSteamId3 = manifest.SourceSteamId3,
                        SourceSteamId64 = manifest.SourceSteamId64,
                        SourceMachineName = manifest.SourceMachineName,
                        AchievementsCount = achCount,
                        AchievementsUnlockedCount = achUnlocked,
                        Manifest = manifest
                    });
                }
                else
                {
                    list.Add(new LocalBackupInfo
                    {
                        FilePath = zipPath,
                        FileName = fi.Name,
                        GameName = Path.GetFileNameWithoutExtension(fi.Name),
                        AppId = 0,
                        FileSizeBytes = fi.Length,
                        CreatedTime = fi.LastWriteTime,
                        SourceUsername = "Desconhecido"
                    });
                }
            }
            catch
            {
                // Ignora arquivos corrompidos ou em uso
            }
        }

        return list.OrderByDescending(b => b.CreatedTime ?? DateTime.MinValue).ToList();
    }
}

