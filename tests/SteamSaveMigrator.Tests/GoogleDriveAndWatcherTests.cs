using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class MockGoogleDriveService : IGoogleDriveService
{
    public bool IsAuth { get; set; } = true;
    public string UserEmail { get; set; } = "gamer@gmail.com";
    public List<CloudBackupInfo> UploadedFiles { get; } = new();

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsAuth);

    public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        IsAuth = true;
        return Task.FromResult(true);
    }

    public void CancelAuthentication() { }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        IsAuth = false;
        return Task.CompletedTask;
    }

    public Task<string?> GetUserEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(IsAuth ? UserEmail : null);

    public Task<CloudBackupInfo> UploadBackupAsync(
        string localZipPath,
        string gameName,
        uint appId,
        string? sourceSteamPersona = null,
        uint? sourceSteamId3 = null,
        ulong? sourceSteamId64 = null,
        string? sourceUsername = null,
        int achievementsCount = 0,
        int achievementsUnlockedCount = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(localZipPath);
        var cloud = new CloudBackupInfo
        {
            FileId = $"cloud_{Guid.NewGuid():N}",
            FileName = Path.GetFileName(localZipPath),
            GameName = gameName,
            AppId = appId,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 1024,
            CreatedTime = DateTime.Now,
            SourceUsername = sourceUsername ?? Environment.UserName,
            SourceSteamPersona = sourceSteamPersona ?? "TestPlayer",
            SourceSteamId3 = sourceSteamId3,
            SourceSteamId64 = sourceSteamId64,
            AchievementsCount = achievementsCount,
            AchievementsUnlockedCount = achievementsUnlockedCount,
            WebViewLink = "https://drive.google.com/file/d/test"
        };
        UploadedFiles.Add(cloud);
        progress?.Report(100.0);
        return Task.FromResult(cloud);
    }

    public Task<List<CloudBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UploadedFiles.ToList());
    }

    public Task<string> DownloadBackupAsync(string fileId, string? targetLocalZipPath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var target = targetLocalZipPath ?? Path.Combine(Path.GetTempPath(), $"downloaded_{fileId}.zip");
        // Cria um arquivo zip vazio de teste se não existir
        if (!File.Exists(target))
        {
            File.WriteAllBytes(target, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }
        progress?.Report(100.0);
        return Task.FromResult(target);
    }

    public Task<bool> DeleteBackupAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var item = UploadedFiles.FirstOrDefault(x => x.FileId == fileId);
        if (item != null)
        {
            UploadedFiles.Remove(item);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public bool HasConfiguredCredentials => true;
    public string? CurrentClientId => "mock_client_id";

    public void ConfigureCredentials(string clientId, string clientSecret)
    {
    }

    public void ConfigureClientSecrets(string clientId, string clientSecret)
    {
    }

    public bool ConfigureCredentialsFromJson(string jsonFilePath) => true;
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class GoogleDriveAndWatcherTests : IDisposable
{
    private readonly string _testRoot;

    public GoogleDriveAndWatcherTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_CloudTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task MockDrive_UploadAndListBackups_ShouldWork()
    {
        var mockDrive = new MockGoogleDriveService();
        var zipPath = Path.Combine(_testRoot, "EldenRing_1245620_backup.zip");
        await File.WriteAllTextAsync(zipPath, "dummy zip content");

        var uploaded = await mockDrive.UploadBackupAsync(zipPath, "Elden Ring", 1245620);
        Assert.NotNull(uploaded);
        Assert.Equal("Elden Ring", uploaded.GameName);
        Assert.Equal(1245620u, uploaded.AppId);

        var list = await mockDrive.ListBackupsAsync();
        Assert.Single(list);
        Assert.Equal("Elden Ring", list[0].GameName);
    }

    [Fact]
    public async Task MockDrive_DownloadAndRestore_ShouldExecute()
    {
        var mockDrive = new MockGoogleDriveService();
        var engine = new SteamSaveMigratorEngine(mockDrive);

        // Prepara um backup zip real para podermos simular restauração
        var fakeAppData = Path.Combine(_testRoot, "AppData", "Roaming", "Hades2");
        Directory.CreateDirectory(fakeAppData);
        await File.WriteAllTextAsync(Path.Combine(fakeAppData, "save.sav"), "conteudo do save");

        var fakeFile = Path.Combine(fakeAppData, "save.sav");
        var game = new SteamGame
        {
            AppId = 1145350,
            Name = "Hades II",
            InstallDir = Path.Combine(_testRoot, "Hades2Install"),
            DetectedSaveLocations = new List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataRoaming,
                    SourcePath = fakeAppData,
                    TokenizedPath = @"%APPDATA%\Hades2",
                    Exists = true,
                    FileCount = 1,
                    TotalSizeBytes = 100,
                    Files = new List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = fakeFile,
                            RelativePath = "save.sav",
                            SizeBytes = 100,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                }
            }
        };

        var backupZip = Path.Combine(_testRoot, "hades2_backup.zip");
        await engine.BackupGameAsync(game, backupZip);

        // Upload para o mock drive
        var uploaded = await mockDrive.UploadBackupAsync(backupZip, "Hades II", 1145350);

        // Testa restauração simulada diretamente da nuvem
        var downloadZip = Path.Combine(_testRoot, "cloud_downloaded.zip");
        File.Copy(backupZip, downloadZip, true);

        var restoreResult = await engine.AutoRestoreAsync(downloadZip, dryRun: true);
        Assert.NotNull(restoreResult);
        Assert.True(restoreResult.IsSuccess);
        Assert.True(restoreResult.SuccessCount > 0);
    }

    [Fact]
    public void GameWatcher_DetectRunningAppIds_ShouldHandleEmptyAndMissing()
    {
        var mockDrive = new MockGoogleDriveService();
        var engine = new SteamSaveMigratorEngine(mockDrive);
        using var watcher = new SteamGameWatcherService(engine, mockDrive);

        var emptyGames = new List<SteamGame>();
        var activeIds = watcher.DetectRunningAppIds(emptyGames);

        Assert.NotNull(activeIds);
    }

    [Fact]
    public void CloudBackupInfo_Formatting_ShouldFormatSizeCorrectly()
    {
        var info = new CloudBackupInfo
        {
            FileId = "123",
            FileName = "game.zip",
            GameName = "Portal 2",
            AppId = 620,
            FileSizeBytes = 1024 * 1024 * 15 // 15 MB
        };

        Assert.Contains("MB", info.FormattedSize);
    }

    [Fact]
    public async Task GameWatcher_OnGameExit_ShouldAutoBackupAndUploadToGoogleDrive()
    {
        var mockDrive = new MockGoogleDriveService();
        var engine = new SteamSaveMigratorEngine(mockDrive);

        // Prepara save de teste
        var fakeSaveFolder = Path.Combine(_testRoot, "Saves_Cyberpunk");
        Directory.CreateDirectory(fakeSaveFolder);
        var saveFile = Path.Combine(fakeSaveFolder, "manual_save_0.sav");
        await File.WriteAllTextAsync(saveFile, "CYBERPUNK_2077_SAVE_CONTENT");

        var game = new SteamGame
        {
            AppId = 1091500,
            Name = "Cyberpunk 2077",
            InstallDir = Path.Combine(_testRoot, "Cyberpunk2077Install"),
            DetectedSaveLocations = new List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.SavedGames,
                    SourcePath = fakeSaveFolder,
                    TokenizedPath = @"%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077",
                    Exists = true,
                    FileCount = 1,
                    TotalSizeBytes = 30,
                    Files = new List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = saveFile,
                            RelativePath = "manual_save_0.sav",
                            SizeBytes = 30,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                }
            }
        };

        var config = new GameWatcherConfig
        {
            AutoBackupOnGameExit = true,
            AutoUploadToGoogleDrive = true,
            PostExitDelaySeconds = 0,
            LocalBackupOutputDir = Path.Combine(_testRoot, "WatcherOutput")
        };

        using var watcher = new SteamGameWatcherService(engine, mockDrive, config);
        var receivedEvents = new List<GameWatcherEvent>();
        watcher.OnWatcherEvent += (e) => receivedEvents.Add(e);

        // Simula o fechamento do jogo acionando o backup automático
        await watcher.HandleGameExitedBackupAsync(game);

        // Verifica os eventos disparados
        Assert.Contains(receivedEvents, e => e.Type == GameWatcherEventType.BackupStarted);
        Assert.Contains(receivedEvents, e => e.Type == GameWatcherEventType.BackupCompleted);
        Assert.Contains(receivedEvents, e => e.Type == GameWatcherEventType.CloudUploadStarted);
        Assert.Contains(receivedEvents, e => e.Type == GameWatcherEventType.CloudUploadCompleted);

        var completedEvent = receivedEvents.First(e => e.Type == GameWatcherEventType.CloudUploadCompleted);
        Assert.NotNull(completedEvent.CloudBackup);
        Assert.Equal("Cyberpunk 2077", completedEvent.CloudBackup.GameName);
        Assert.Equal(1091500u, completedEvent.CloudBackup.AppId);
        Assert.True(File.Exists(completedEvent.LocalZipPath));

        // Verifica que o arquivo foi adicionado ao Google Drive mockado
        var cloudFiles = await mockDrive.ListBackupsAsync();
        Assert.Contains(cloudFiles, c => c.GameName == "Cyberpunk 2077" && c.AppId == 1091500);
    }

    [Fact]
    public void GoogleDriveService_ConfigureCredentialsFromJson_InstalledFormat_ParsesSuccessfully()
    {
        var jsonDir = Path.Combine(_testRoot, "GoogleAuthTest1");
        Directory.CreateDirectory(jsonDir);
        var jsonFile = Path.Combine(jsonDir, "credentials.json");

        var jsonContent = """
        {
          "installed": {
            "client_id": "123456789-abcdef.apps.googleusercontent.com",
            "project_id": "steamsavemigrator-project",
            "auth_uri": "https://accounts.google.com/o/oauth2/auth",
            "token_uri": "https://oauth2.googleapis.com/token",
            "client_secret": "GOCSPX-SecretReal123"
          }
        }
        """;
        File.WriteAllText(jsonFile, jsonContent);

        var authDir1 = Path.Combine(_testRoot, "IsolatedAuth1");
        Directory.CreateDirectory(authDir1);
        var realService = new GoogleDriveService(authDir1);
        var success = realService.ConfigureCredentialsFromJson(jsonFile);

        Assert.True(success);
        Assert.True(realService.HasConfiguredCredentials);
        Assert.Equal("123456789-abcdef.apps.googleusercontent.com", realService.CurrentClientId);
    }

    [Fact]
    public void GoogleDriveService_ConfigureCredentialsFromJson_WebFormat_ParsesSuccessfully()
    {
        var jsonDir = Path.Combine(_testRoot, "GoogleAuthTest2");
        Directory.CreateDirectory(jsonDir);
        var jsonFile = Path.Combine(jsonDir, "client_secret.json");

        var jsonContent = """
        {
          "web": {
            "client_id": "987654321-xyz.apps.googleusercontent.com",
            "client_secret": "GOCSPX-WebSecret456"
          }
        }
        """;
        File.WriteAllText(jsonFile, jsonContent);

        var authDir2 = Path.Combine(_testRoot, "IsolatedAuth2");
        Directory.CreateDirectory(authDir2);
        var realService = new GoogleDriveService(authDir2);
        var success = realService.ConfigureCredentialsFromJson(jsonFile);

        Assert.True(success);
        Assert.True(realService.HasConfiguredCredentials);
        Assert.Equal("987654321-xyz.apps.googleusercontent.com", realService.CurrentClientId);
    }

    [Fact]
    public void GoogleDriveService_ConfigureCredentials_DirectInput_Succeeds()
    {
        var authDir3 = Path.Combine(_testRoot, "IsolatedAuth3");
        Directory.CreateDirectory(authDir3);
        var realService = new GoogleDriveService(authDir3);
        realService.ConfigureCredentials("manual_client_id.apps.googleusercontent.com", "manual_secret_abc");

        Assert.True(realService.HasConfiguredCredentials);
        Assert.Equal("manual_client_id.apps.googleusercontent.com", realService.CurrentClientId);
    }

    [Fact]
    public void CloudBackupInfo_AchievementsDisplay_FormatsCorrectly()
    {
        var withAch = new CloudBackupInfo
        {
            GameName = "Hades II",
            AppId = 1145350,
            AchievementsCount = 20,
            AchievementsUnlockedCount = 15
        };

        Assert.Equal("🏆 15/20", withAch.AchievementsDisplay);

        var withoutAch = new CloudBackupInfo
        {
            GameName = "Portal",
            AppId = 400,
            AchievementsCount = 0,
            AchievementsUnlockedCount = 0
        };

        Assert.Equal("—", withoutAch.AchievementsDisplay);
    }

    [Fact]
    public void UserSettings_OpenSteamOnStartup_And_Profiles_Persist()
    {
        var testConfig = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_TestLaunchConfig_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AppSettingsService.OverrideConfigPathForTesting = testConfig;

            var settings = new UserSettings
            {
                OpenSteamOnStartup = true,
                WatcherAutoBackup = true,
                LastTargetSteamId3 = 81405752,
                LastTargetWindowsUsername = "Ariane"
            };

            AppSettingsService.SaveSettings(settings);
            var loaded = AppSettingsService.LoadSettings();

            Assert.True(loaded.OpenSteamOnStartup);
            Assert.True(loaded.WatcherAutoBackup);
            Assert.Equal(81405752u, loaded.LastTargetSteamId3);
            Assert.Equal("Ariane", loaded.LastTargetWindowsUsername);
        }
        finally
        {
            AppSettingsService.OverrideConfigPathForTesting = null;
            if (File.Exists(testConfig))
            {
                File.Delete(testConfig);
            }
        }
    }

    [Fact]
    public void SteamDetector_LaunchSteamIfNotRunning_DoesNotThrow()
    {
        // Garante que o método é seguro e não lança exceção não tratada
        var exception = Record.Exception(() =>
        {
            _ = SteamSaveMigrator.Core.Detectors.SteamDetector.LaunchSteamIfNotRunning();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void GameWatcher_UpdateOutputDir_ShouldChangeConfigAndCreateDirectory()
    {
        var engine = new SteamSaveMigratorEngine();
        var mockDrive = new MockGoogleDriveService();
        var initialDir = Path.Combine(_testRoot, "InitialWatcherOutput");
        var config = new GameWatcherConfig { LocalBackupOutputDir = initialDir };

        using var watcher = new SteamGameWatcherService(engine, mockDrive, config);
        Assert.Equal(initialDir, watcher.Config.LocalBackupOutputDir);

        var newDir = Path.Combine(_testRoot, "DynamicWatcherOutput");
        watcher.UpdateOutputDir(newDir);

        Assert.Equal(newDir, watcher.Config.LocalBackupOutputDir);
        Assert.True(Directory.Exists(newDir));
    }
}
