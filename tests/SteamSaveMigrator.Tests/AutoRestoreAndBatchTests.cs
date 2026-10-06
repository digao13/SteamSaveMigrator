using System;
using System.IO;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class AutoRestoreAndBatchTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _sourceUserDir;
    private readonly string _outputBackupDir;

    public AutoRestoreAndBatchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SSM_AutoTests_" + Guid.NewGuid().ToString("N"));
        _sourceUserDir = Path.Combine(_tempRoot, "Users", "USUARIO_ORIGEM");
        _outputBackupDir = Path.Combine(_tempRoot, "Backups");

        Directory.CreateDirectory(_sourceUserDir);
        Directory.CreateDirectory(_outputBackupDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Should_Auto_Restore_Without_Manual_Configuration_In_DryRun()
    {
        // 1. Cria save fictício no perfil de USUARIO_ORIGEM
        var sourceLocalApp = Path.Combine(_sourceUserDir, "AppData", "Local", "AutoGame");
        Directory.CreateDirectory(sourceLocalApp);
        var saveFile = Path.Combine(sourceLocalApp, "autosave.dat");
        await File.WriteAllTextAsync(saveFile, "ORIGINAL_SAVE_CONTENT");

        var game = new SteamGame
        {
            AppId = 777777,
            Name = "Auto Game",
            DetectedSaveLocations = new()
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataLocal,
                    SourcePath = sourceLocalApp,
                    Exists = true,
                    Files = new()
                    {
                        new()
                        {
                            FullPath = saveFile,
                            RelativePath = "autosave.dat",
                            SizeBytes = new FileInfo(saveFile).Length,
                            LastWriteTimeUtc = File.GetLastWriteTimeUtc(saveFile)
                        }
                    }
                }
            }
        };

        var backupZip = Path.Combine(_outputBackupDir, "AutoGame_backup.zip");
        var backupService = new BackupService();
        await backupService.CreateBackupAsync(game, backupZip, sourceUserProfile: _sourceUserDir);

        // 2. Chama AutoRestoreAsync sem precisar fornecer nenhum caminho nem usuário manualmente!
        var restoreService = new RestoreService();
        var result = await restoreService.AutoRestoreAsync(backupZip, dryRun: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.SuccessCount);

        var restoredItem = result.Items[0];
        Assert.True(restoredItem.Success);

        // O destino deve apontar para o usuário atual do Windows (%USERPROFILE%\AppData\Local\AutoGame\autosave.dat)
        var expectedCurrentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expectedDestination = Path.Combine(expectedCurrentUserProfile, "AppData", "Local", "AutoGame", "autosave.dat");

        Assert.Equal(Path.GetFullPath(expectedDestination), Path.GetFullPath(restoredItem.DestinationPath));
    }

    [Fact]
    public async Task Should_Batch_Backup_Multiple_Games_Simultaneously()
    {
        var localDir1 = Path.Combine(_sourceUserDir, "AppData", "Local", "GameA");
        var localDir2 = Path.Combine(_sourceUserDir, "AppData", "Local", "GameB");
        Directory.CreateDirectory(localDir1);
        Directory.CreateDirectory(localDir2);

        var fileA = Path.Combine(localDir1, "saveA.dat");
        var fileB = Path.Combine(localDir2, "saveB.dat");
        await File.WriteAllTextAsync(fileA, "CONTENT_A");
        await File.WriteAllTextAsync(fileB, "CONTENT_B");

        var gameA = new SteamGame
        {
            AppId = 111,
            Name = "Game Alpha",
            DetectedSaveLocations = new()
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataLocal,
                    SourcePath = localDir1,
                    Exists = true,
                    Files = new() { new() { FullPath = fileA, RelativePath = "saveA.dat", SizeBytes = 9 } }
                }
            }
        };

        var gameB = new SteamGame
        {
            AppId = 222,
            Name = "Game Beta",
            DetectedSaveLocations = new()
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataLocal,
                    SourcePath = localDir2,
                    Exists = true,
                    Files = new() { new() { FullPath = fileB, RelativePath = "saveB.dat", SizeBytes = 9 } }
                }
            }
        };

        var backupService = new BackupService();
        var batchResult = await backupService.CreateBatchBackupAsync(
            new[] { gameA, gameB },
            _outputBackupDir,
            sourceUserProfile: _sourceUserDir
        );

        Assert.True(batchResult.IsSuccess);
        Assert.Equal(2, batchResult.TotalGamesProcessed);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(2, batchResult.GeneratedZipFiles.Count);

        foreach (var zip in batchResult.GeneratedZipFiles)
        {
            Assert.True(File.Exists(zip), $"Arquivo não encontrado: {zip}");
        }
    }

    [Fact]
    public async Task Should_Prevent_Small_Save_From_Overwriting_Larger_Save_And_Update_RemoteCache()
    {
        var targetSteamDir = Path.Combine(_tempRoot, "SteamTarget");
        var targetUserDir = Path.Combine(_tempRoot, "Users", "NovoUsuario");
        Directory.CreateDirectory(targetSteamDir);
        Directory.CreateDirectory(targetUserDir);

        // Prepara pacote zip com 2 entradas para o mesmo arquivo:
        // 1 grande (2MB) em userdata e 1 pequena (1.6KB) em AppDataLocal
        var backupZip = Path.Combine(_outputBackupDir, "ProgressProtectionTest.zip");

        var largeData = new byte[15000]; // 15KB > 10KB threshold
        Array.Fill(largeData, (byte)0xAB);

        var smallData = new byte[1000]; // 1KB < 5KB threshold
        Array.Fill(smallData, (byte)0x12);

        var manifest = new BackupManifest
        {
            AppId = 3914860,
            GameName = "Moss Test",
            SourceSteamId3 = 1879694565,
            SourceUsername = "Arianne",
            SourceUserProfile = "C:\\Users\\Arianne",
            Entries = new()
            {
                new()
                {
                    OriginalFullPath = "C:\\Program Files (x86)\\Steam\\userdata\\1879694565\\3914860\\ac\\WinAppDataLocal\\MossCollection\\Saved\\SaveGames\\Moss1SaveGame_0.sav",
                    TokenizedPath = "%STEAM_USERDATA%\\1879694565\\3914860\\ac\\WinAppDataLocal\\MossCollection\\Saved\\SaveGames\\Moss1SaveGame_0.sav",
                    ZipEntryName = "data/_STEAM_USERDATA_/1879694565/3914860/ac/WinAppDataLocal/MossCollection/Saved/SaveGames/Moss1SaveGame_0.sav",
                    FileSizeBytes = largeData.Length,
                    LastWriteTimeUtc = DateTime.UtcNow
                },
                new()
                {
                    OriginalFullPath = "C:\\Users\\Arianne\\AppData\\Local\\MossCollection\\Saved\\SaveGames\\Moss1SaveGame_0.sav",
                    TokenizedPath = "%LOCALAPPDATA%\\MossCollection\\Saved\\SaveGames\\Moss1SaveGame_0.sav",
                    ZipEntryName = "data/_LOCALAPPDATA_/MossCollection/Saved/SaveGames/Moss1SaveGame_0.sav",
                    FileSizeBytes = smallData.Length,
                    LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-5)
                }
            }
        };

        using (var archive = System.IO.Compression.ZipFile.Open(backupZip, System.IO.Compression.ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var mw = new StreamWriter(manifestEntry.Open()))
            {
                mw.Write(System.Text.Json.JsonSerializer.Serialize(manifest));
            }

            var entryLarge = archive.CreateEntry(manifest.Entries[0].ZipEntryName);
            using (var stream = entryLarge.Open())
            {
                stream.Write(largeData);
            }

            var entrySmall = archive.CreateEntry(manifest.Entries[1].ZipEntryName);
            using (var stream = entrySmall.Open())
            {
                stream.Write(smallData);
            }
        }

        // Restaura apontando para a conta targetSteamId3 = 81405752
        var restoreService = new RestoreService();
        var result = await restoreService.AutoRestoreAsync(
            backupZip,
            targetSteamId3: 81405752,
            targetSteamPath: targetSteamDir,
            targetUsername: "NovoUsuario",
            targetUserProfile: targetUserDir,
            overwriteExisting: true
        );

        Assert.True(result.IsSuccess);

        // O arquivo no userdata de destino deve ter o tamanho grande (15.000 bytes)
        var restoredUserdataFile = Path.Combine(targetSteamDir, "userdata", "81405752", "3914860", "ac", "WinAppDataLocal", "MossCollection", "Saved", "SaveGames", "Moss1SaveGame_0.sav");
        Assert.True(File.Exists(restoredUserdataFile), $"Arquivo não encontrado em: {restoredUserdataFile}");
        Assert.Equal(largeData.Length, new FileInfo(restoredUserdataFile).Length);

        // O arquivo espelhado em LocalAppData de destino também deve ter o tamanho grande
        var restoredLocalFile = Path.Combine(targetUserDir, "AppData", "Local", "MossCollection", "Saved", "SaveGames", "Moss1SaveGame_0.sav");
        Assert.True(File.Exists(restoredLocalFile), $"Arquivo não encontrado em: {restoredLocalFile}");
        Assert.Equal(largeData.Length, new FileInfo(restoredLocalFile).Length);

        // O remotecache.vdf deve ter sido criado com syncstate 1
        var remoteCacheVdf = Path.Combine(targetSteamDir, "userdata", "81405752", "3914860", "remotecache.vdf");
        Assert.True(File.Exists(remoteCacheVdf));
        var vdfText = await File.ReadAllTextAsync(remoteCacheVdf);
        Assert.Contains("\"syncstate\"\t\t\"1\"", vdfText);
        Assert.Contains(largeData.Length.ToString(), vdfText);
    }

    [Fact]
    public void Should_Save_And_Load_UserSettings_Correctly()
    {
        var testConfig = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_TestBatchConfig_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AppSettingsService.OverrideConfigPathForTesting = testConfig;

            var settings = new UserSettings
            {
                CustomBackupDirectory = @"C:\BackupsPersonalizados",
                AutoStartWatcherOnLaunch = true,
                WatcherAutoUpload = true,
                WatcherPollInterval = 5,
                WatcherPostExitDelay = 7,
                StartWithWindows = true,
                MinimizeToTray = true
            };

            AppSettingsService.SaveSettings(settings);
            var loaded = AppSettingsService.LoadSettings();

            Assert.NotNull(loaded);
            Assert.Equal(@"C:\BackupsPersonalizados", loaded.CustomBackupDirectory);
            Assert.True(loaded.AutoStartWatcherOnLaunch);
            Assert.True(loaded.WatcherAutoUpload);
            Assert.Equal(5, loaded.WatcherPollInterval);
            Assert.Equal(7, loaded.WatcherPostExitDelay);
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
    public void Should_Detect_Saves_Via_AutoCloud_And_Engine_Scanners()
    {
        var testSteamDir = Path.Combine(_tempRoot, "SteamMock");
        var userdataPath = Path.Combine(testSteamDir, "userdata", "123456", "999888");
        Directory.CreateDirectory(userdataPath);

        // Cria estrutura de save em AppDataLocalLow simulada (ex: Studio Doodal/Solateria)
        var localLowDir = Path.Combine(_sourceUserDir, "AppData", "LocalLow", "MockStudio", "MockGame");
        Directory.CreateDirectory(localLowDir);
        File.WriteAllText(Path.Combine(localLowDir, "SaveData.txt"), "MOCK_SAVE_DATA_CONTENT");

        // Cria remotecache.vdf apontando para root 12 (AppDataLocalLow)
        var remoteCacheVdf = Path.Combine(userdataPath, "remotecache.vdf");
        var vdfContent = "\"999888\"\n{\n\t\"MockStudio/MockGame/SaveData.txt\"\n\t{\n\t\t\"root\"\t\t\"12\"\n\t\t\"size\"\t\t\"100\"\n\t}\n}";
        File.WriteAllText(remoteCacheVdf, vdfContent);

        var steamInfo = new SteamInstallationInfo
        {
            IsFound = true,
            SteamPath = testSteamDir,
            Accounts = new()
            {
                new() { SteamId3 = 123456, SteamId64 = 76561198041671480, PersonaName = "MockPlayer" }
            }
        };

        var game = new SteamGame
        {
            AppId = 999888,
            Name = "Mock Game",
            InstallDir = "MockGame"
        };

        var locations = SteamSaveMigrator.Core.Detectors.SaveLocationDetector.DetectSaveLocations(game, steamInfo, _sourceUserDir);

        Assert.NotEmpty(locations);
        Assert.Contains(locations, l => l.SourcePath.Equals(localLowDir, StringComparison.OrdinalIgnoreCase));
        var match = locations.First(l => l.SourcePath.Equals(localLowDir, StringComparison.OrdinalIgnoreCase));
        Assert.True(match.FileCount >= 1);
        Assert.Contains(match.Files, f => f.RelativePath.Contains("SaveData.txt"));
    }
}
