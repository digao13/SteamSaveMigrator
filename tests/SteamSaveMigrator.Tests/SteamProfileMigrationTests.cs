using System;
using System.IO;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Detectors;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class SteamProfileMigrationTests
{
    [Fact]
    public void Should_Migrate_Steam_Userdata_From_One_Steam_Account_To_Another()
    {
        // Dado um save no formato Steam userdata do usuário A (81405752)
        var sourceToken = @"%STEAM_USERDATA%\81405752\241100\remote\save.bin";
        var options = new PathConversionOptions
        {
            TargetSteamPath = @"C:\Steam",
            SourceSteamId3 = 81405752,
            TargetSteamId3 = 1879694565
        };

        var resolved = PathVariableConverter.Resolve(sourceToken, options);
        var expected = Path.GetFullPath(@"C:\Steam\userdata\1879694565\241100\remote\save.bin");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Should_Migrate_Steam_Userdata_When_SourceSteamId3_Is_Omitted()
    {
        // Se o manifesto não tiver SourceSteamId3 gravado, o conversor deve detectar o primeiro segmento numérico
        var sourceToken = @"%STEAM_USERDATA%\99998888\646570\remote\preferences.save";
        var options = new PathConversionOptions
        {
            TargetSteamPath = @"C:\Steam",
            SourceSteamId3 = null, // omitido
            TargetSteamId3 = 1879694565
        };

        var resolved = PathVariableConverter.Resolve(sourceToken, options);
        var expected = Path.GetFullPath(@"C:\Steam\userdata\1879694565\646570\remote\preferences.save");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Should_Migrate_AppData_Folder_Containing_SteamId64()
    {
        // Exemplo: Elden Ring ou Palworld que salvam em %APPDATA% com a pasta nomeada com o SteamID64
        // SteamID3 = 81405752  -> SteamID64 = 76561198041671480
        // SteamID3 = 1879694565 -> SteamID64 = 76561199839960293
        var sourceProfile = @"C:\Users\USUARIO_ORIGEM";
        var targetProfile = @"C:\Users\NOVO_USUARIO";

        var fullPath = @"C:\Users\USUARIO_ORIGEM\AppData\Roaming\EldenRing\76561198041671480\ER0000.sl2";
        var token = PathVariableConverter.Tokenize(fullPath, sourceProfile);

        var options = new PathConversionOptions
        {
            SourceUsername = "USUARIO_ORIGEM",
            SourceUserProfile = sourceProfile,
            TargetUsername = "NOVO_USUARIO",
            TargetUserProfile = targetProfile,
            SourceSteamId3 = 81405752,
            TargetSteamId3 = 1879694565
        };

        var resolved = PathVariableConverter.Resolve(token, options);
        var expected = Path.GetFullPath(@"C:\Users\NOVO_USUARIO\AppData\Roaming\EldenRing\76561199839960293\ER0000.sl2");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Should_AutoDetect_SourceSteamId_From_Manifest_Entries()
    {
        var manifest = new BackupManifest
        {
            GameName = "Monster Hunter: World",
            AppId = 582010,
            SourceUsername = "USUARIO_ORIGEM",
            SourceSteamId3 = null, // Não gravado explicitamente
            Entries = new System.Collections.Generic.List<BackupManifestEntry>
            {
                new()
                {
                    TokenizedPath = @"%STEAM_USERDATA%\81405752\582010\remote\SAVEDATA1000",
                    OriginalFullPath = @"C:\Steam\userdata\81405752\582010\remote\SAVEDATA1000",
                    ZipEntryName = "data/SAVEDATA1000"
                }
            }
        };

        var detected = RestoreService.DetectSourceSteamId(manifest);
        Assert.NotNull(detected);
        Assert.Equal((uint)81405752, detected.Value);
    }

    [Fact]
    public async Task Should_Full_Cycle_Backup_And_Restore_To_Another_Steam_Profile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_SteamProfileTest_" + Guid.NewGuid().ToString("N"));
        var steamDir = Path.Combine(tempRoot, "Steam");
        var sourceUserdata = Path.Combine(steamDir, "userdata", "81405752", "12345", "remote");
        Directory.CreateDirectory(sourceUserdata);

        var saveFile = Path.Combine(sourceUserdata, "game_save.dat");
        await File.WriteAllTextAsync(saveFile, "STEAM_SAVE_CONTENT_12345");

        var backupZip = Path.Combine(tempRoot, "backup.zip");

        var game = new SteamGame
        {
            AppId = 12345,
            Name = "Steam Cloud Test Game",
            InstallDir = "TestGame",
            DetectedSaveLocations = new System.Collections.Generic.List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.SteamUserdata,
                    DisplayName = "Steam Userdata (81405752)",
                    SourcePath = Path.Combine(steamDir, "userdata", "81405752", "12345"),
                    TokenizedPath = @"%STEAM_USERDATA%\81405752\12345",
                    Exists = true,
                    Files = new System.Collections.Generic.List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = saveFile,
                            RelativePath = @"remote\game_save.dat",
                            SizeBytes = 24,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                }
            }
        };

        var backupService = new BackupService();
        await backupService.CreateBackupAsync(
            game,
            backupZip,
            steamPath: steamDir,
            sourceSteamId3: 81405752,
            sourceSteamPersonaName: "Desconnect");

        Assert.True(File.Exists(backupZip));

        // Inspeciona manifesto gravado
        var restoreService = new RestoreService();
        var manifest = await restoreService.ReadManifestAsync(backupZip);
        Assert.NotNull(manifest);
        Assert.Equal((uint)81405752, manifest.SourceSteamId3);
        Assert.Equal((ulong)76561198041671480, manifest.SourceSteamId64);
        Assert.Equal("Desconnect", manifest.SourceSteamPersonaName);

        // Restaura apontando para outro perfil Steam (1879694565)
        var restoreResult = await restoreService.AutoRestoreAsync(
            backupZip,
            targetSteamId3: 1879694565,
            targetSteamPath: steamDir);

        Assert.True(restoreResult.IsSuccess);
        Assert.Equal((uint)81405752, restoreResult.SourceSteamId3);
        Assert.Equal((uint)1879694565, restoreResult.TargetSteamId3);

        // O arquivo DEVE ter sido salvo no perfil do novo usuário Steam (1879694565)
        var targetExpectedFile = Path.Combine(steamDir, "userdata", "1879694565", "12345", "remote", "game_save.dat");
        Assert.True(File.Exists(targetExpectedFile), $"Arquivo esperado não existe em: {targetExpectedFile}");
        var content = await File.ReadAllTextAsync(targetExpectedFile);
        Assert.Equal("STEAM_SAVE_CONTENT_12345", content);

        // Limpeza
        try { Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public async Task Should_Full_Cycle_Backup_And_Restore_To_Another_Windows_User()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_WinUserTest_" + Guid.NewGuid().ToString("N"));
        var sourceProfile = Path.Combine(tempRoot, "Users", "Desconnect");
        var targetProfile = Path.Combine(tempRoot, "Users", "novo_usuario");

        var sourceSaveDir = Path.Combine(sourceProfile, "AppData", "Local", "TestGame", "Saves");
        Directory.CreateDirectory(sourceSaveDir);
        var sourceSaveFile = Path.Combine(sourceSaveDir, "player.sav");
        await File.WriteAllTextAsync(sourceSaveFile, "SAVE_FROM_DESCONNECT_WIN_USER");

        var backupZip = Path.Combine(tempRoot, "backup_win.zip");

        var game = new SteamGame
        {
            AppId = 99999,
            Name = "Windows User Test Game",
            InstallDir = "TestGame",
            DetectedSaveLocations = new System.Collections.Generic.List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataLocal,
                    DisplayName = "AppData Local Saves",
                    SourcePath = sourceSaveDir,
                    TokenizedPath = @"%LOCALAPPDATA%\TestGame\Saves",
                    Exists = true,
                    Files = new System.Collections.Generic.List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = sourceSaveFile,
                            RelativePath = "player.sav",
                            SizeBytes = 30,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                }
            }
        };

        var backupService = new BackupService();
        await backupService.CreateBackupAsync(
            game,
            backupZip,
            sourceUserProfile: sourceProfile,
            sourceSteamPersonaName: "Desconnect");

        Assert.True(File.Exists(backupZip));

        // Restaura especificando o usuário Windows de destino "novo_usuario"
        var restoreService = new RestoreService();
        var restoreResult = await restoreService.AutoRestoreAsync(
            backupZip,
            targetUsername: "novo_usuario",
            targetUserProfile: targetProfile);

        Assert.True(restoreResult.IsSuccess);

        // O arquivo DEVE ter sido salvo na pasta AppData\Local do usuário "novo_usuario"
        var expectedDestFile = Path.Combine(targetProfile, "AppData", "Local", "TestGame", "Saves", "player.sav");
        Assert.True(File.Exists(expectedDestFile), $"Arquivo não encontrado no perfil do novo usuário Windows: {expectedDestFile}");
        var content = await File.ReadAllTextAsync(expectedDestFile);
        Assert.Equal("SAVE_FROM_DESCONNECT_WIN_USER", content);

        // Limpeza
        try { Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public async Task Should_Simultaneously_Migrate_Both_Windows_User_And_Steam_Profile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_BothTest_" + Guid.NewGuid().ToString("N"));
        var steamDir = Path.Combine(tempRoot, "Steam");
        var sourceWinProfile = Path.Combine(tempRoot, "Users", "Desconnect");
        var targetWinProfile = Path.Combine(tempRoot, "Users", "novo_usuario");

        var sourceSteamUserdata = Path.Combine(steamDir, "userdata", "81405752", "582010", "remote");
        Directory.CreateDirectory(sourceSteamUserdata);
        var sourceSteamSave = Path.Combine(sourceSteamUserdata, "SAVEDATA1000");
        await File.WriteAllTextAsync(sourceSteamSave, "STEAM_USERDATA_SAVE_CONTENT");

        var sourceWinDocs = Path.Combine(sourceWinProfile, "Documents", "My Games", "TestGame", "Saves");
        Directory.CreateDirectory(sourceWinDocs);
        var sourceDocsSave = Path.Combine(sourceWinDocs, "save.dat");
        await File.WriteAllTextAsync(sourceDocsSave, "WIN_DOCS_SAVE_CONTENT");

        var backupZip = Path.Combine(tempRoot, "dual_migration_backup.zip");

        var game = new SteamGame
        {
            AppId = 582010,
            Name = "Dual Migration Test Game",
            InstallDir = "TestGame",
            DetectedSaveLocations = new System.Collections.Generic.List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.SteamUserdata,
                    DisplayName = "Steam Userdata",
                    SourcePath = Path.Combine(steamDir, "userdata", "81405752", "582010"),
                    TokenizedPath = @"%STEAM_USERDATA%\81405752\582010",
                    Exists = true,
                    Files = new System.Collections.Generic.List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = sourceSteamSave,
                            RelativePath = @"remote\SAVEDATA1000",
                            SizeBytes = 27,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                },
                new()
                {
                    LocationType = SaveLocationType.Documents,
                    DisplayName = "Documents Save",
                    SourcePath = sourceWinDocs,
                    TokenizedPath = @"%DOCUMENTS%\My Games\TestGame\Saves",
                    Exists = true,
                    Files = new System.Collections.Generic.List<SaveFileInfo>
                    {
                        new()
                        {
                            FullPath = sourceDocsSave,
                            RelativePath = "save.dat",
                            SizeBytes = 22,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                }
            }
        };

        var backupService = new BackupService();
        await backupService.CreateBackupAsync(
            game,
            backupZip,
            sourceUserProfile: sourceWinProfile,
            steamPath: steamDir,
            sourceSteamId3: 81405752,
            sourceSteamPersonaName: "Desconnect");

        Assert.True(File.Exists(backupZip));

        // Executa a restauração informando AMBOS os novos alvos:
        // Novo Usuário Windows: "novo_usuario"
        // Novo Perfil Steam: 1879694565 (novo_usuario)
        var restoreService = new RestoreService();
        var restoreResult = await restoreService.AutoRestoreAsync(
            backupZip,
            targetSteamId3: 1879694565,
            targetSteamPath: steamDir,
            targetUsername: "novo_usuario",
            targetUserProfile: targetWinProfile);

        Assert.True(restoreResult.IsSuccess);
        Assert.Equal(2, restoreResult.SuccessCount);

        // 1. O save da Steam DEVE estar na nova pasta do SteamID3 (1879694565)
        var expectedSteamSave = Path.Combine(steamDir, "userdata", "1879694565", "582010", "remote", "SAVEDATA1000");
        Assert.True(File.Exists(expectedSteamSave), $"Save Steam não encontrado no novo perfil Steam: {expectedSteamSave}");
        var steamContent = await File.ReadAllTextAsync(expectedSteamSave);
        Assert.Equal("STEAM_USERDATA_SAVE_CONTENT", steamContent);

        // 2. O save dos Documentos DEVE estar na nova pasta de Documentos do usuário "novo_usuario"
        var expectedWinSave = Path.Combine(targetWinProfile, "Documents", "My Games", "TestGame", "Saves", "save.dat");
        Assert.True(File.Exists(expectedWinSave), $"Save Windows não encontrado no novo perfil Windows: {expectedWinSave}");
        var winContent = await File.ReadAllTextAsync(expectedWinSave);
        Assert.Equal("WIN_DOCS_SAVE_CONTENT", winContent);

        // Limpeza
        try { Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public void Should_Detect_Available_Windows_Profiles()
    {
        var profiles = WindowsKnownFolders.GetAvailableUserProfiles();
        Assert.NotEmpty(profiles);
        var current = System.Linq.Enumerable.FirstOrDefault(profiles, p => p.IsCurrentAccount);
        Assert.NotNull(current);
        var currentProfileDirName = Path.GetFileName(WindowsKnownFolders.GetCurrentProfile());
        Assert.True(
            string.Equals(current.Username, currentProfileDirName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current.Username, Environment.UserName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_Update_Steam_AutoCloud_Vdf_AccountId_And_Mirror_To_Steam_Userdata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_AutoCloudTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var sourceWinProfile = Path.Combine(tempRoot, "Users", "User");
        var targetWinProfile = Path.Combine(tempRoot, "Users", "Arianne");
        var steamDir = Path.Combine(tempRoot, "Steam");
        var backupZip = Path.Combine(tempRoot, "Moss_Backup.zip");

        // Cria estrutura de origem
        var sourceLocalApp = Path.Combine(sourceWinProfile, "AppData", "Local", "MossCollection", "Saved", "SaveGames");
        Directory.CreateDirectory(sourceLocalApp);

        var sourceSaveFile = Path.Combine(sourceLocalApp, "Moss1SaveGame_0.sav");
        await File.WriteAllTextAsync(sourceSaveFile, "ADVANCED_SAVE_DATA_CONTENT_1934264");

        var sourceAutoCloudVdf = Path.Combine(sourceLocalApp, "steam_autocloud.vdf");
        await File.WriteAllTextAsync(sourceAutoCloudVdf, "\"steam_autocloud.vdf\"\n{\n\t\"accountid\"\t\t\"81405752\"\n}\n");

        // Gera o backup
        var game = new SteamGame
        {
            AppId = 3914860,
            Name = "Moss: The Forgotten Relic",
            InstallDir = "Moss",
            InstallPath = Path.Combine(steamDir, "steamapps", "common", "Moss"),
            DetectedSaveLocations = new System.Collections.Generic.List<GameSaveLocation>
            {
                new()
                {
                    LocationType = SaveLocationType.AppDataLocal,
                    SourcePath = sourceLocalApp,
                    TokenizedPath = @"%LOCALAPPDATA%\MossCollection\Saved\SaveGames",
                    Exists = true,
                    Files = new System.Collections.Generic.List<SaveFileInfo>
                    {
                        new() { FullPath = sourceSaveFile, RelativePath = "Moss1SaveGame_0.sav", SizeBytes = 32 },
                        new() { FullPath = sourceAutoCloudVdf, RelativePath = "steam_autocloud.vdf", SizeBytes = 53 }
                    }
                }
            }
        };

        var backupService = new BackupService();
        await backupService.CreateBackupAsync(
            game,
            backupZip,
            sourceUserProfile: sourceWinProfile,
            steamPath: steamDir,
            sourceSteamId3: 81405752,
            sourceSteamPersonaName: "Desconnect"
        );

        // Executa a restauração para Arianne (usuário Windows) e 1879694565 (conta Steam)
        var restoreService = new RestoreService();
        var options = new PathConversionOptions
        {
            SourceUsername = "User",
            SourceUserProfile = sourceWinProfile,
            SourceSteamId3 = 81405752,
            TargetUsername = "Arianne",
            TargetUserProfile = targetWinProfile,
            TargetSteamId3 = 1879694565,
            TargetSteamPath = steamDir,
            OverwriteExisting = true
        };

        var result = await restoreService.RestoreBackupAsync(backupZip, options);
        Assert.True(result.IsSuccess);

        // 1. O save e o steam_autocloud.vdf DEVEM estar na pasta da Arianne
        var targetLocalApp = Path.Combine(targetWinProfile, "AppData", "Local", "MossCollection", "Saved", "SaveGames");
        var targetSaveFile = Path.Combine(targetLocalApp, "Moss1SaveGame_0.sav");
        var targetAutoCloudVdf = Path.Combine(targetLocalApp, "steam_autocloud.vdf");

        Assert.True(File.Exists(targetSaveFile), $"Save não encontrado em LocalAppData de Arianne: {targetSaveFile}");
        Assert.True(File.Exists(targetAutoCloudVdf), $"steam_autocloud.vdf não encontrado: {targetAutoCloudVdf}");

        // 2. O steam_autocloud.vdf DEVE ter o accountid atualizado para 1879694565
        var vdfContent = await File.ReadAllTextAsync(targetAutoCloudVdf);
        Assert.Contains("1879694565", vdfContent);
        Assert.DoesNotContain("81405752", vdfContent);

        // 3. O save DEVE estar espelhado na pasta userdata da Steam do novo SteamID3 (1879694565)
        var targetSteamAcSave = Path.Combine(steamDir, "userdata", "1879694565", "3914860", "ac", "WinAppDataLocal", "MossCollection", "Saved", "SaveGames", "Moss1SaveGame_0.sav");
        Assert.True(File.Exists(targetSteamAcSave), $"Save não espelhado no Steam userdata AutoCloud: {targetSteamAcSave}");

        var mirroredContent = await File.ReadAllTextAsync(targetSteamAcSave);
        Assert.Equal("ADVANCED_SAVE_DATA_CONTENT_1934264", mirroredContent);

        // Limpeza
        try { Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public async Task Should_Prevent_Savegame_Downgrade_When_Smaller_Save_Attempts_To_Overwrite_Larger()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SSM_DowngradeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "backup.zip");
            var targetDest = Path.Combine(tempDir, "TargetSave", "game_save.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(targetDest)!);

            // Cria um save existente local de 5000 bytes
            var largeContent = new byte[5000];
            Array.Fill(largeContent, (byte)0xAA);
            await File.WriteAllBytesAsync(targetDest, largeContent);

            // Cria o ZIP de backup com uma versão menor de 2000 bytes
            var smallContent = new byte[2000];
            Array.Fill(smallContent, (byte)0xBB);

            using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var manifest = new BackupManifest
                {
                    GameName = "Downgrade Protection Game",
                    AppId = 12345,
                    SourceUsername = "TestUser",
                    Entries = new System.Collections.Generic.List<BackupManifestEntry>
                    {
                        new()
                        {
                            TokenizedPath = @"%USERPROFILE%\TargetSave\game_save.sav",
                            OriginalFullPath = @"C:\Users\TestUser\TargetSave\game_save.sav",
                            ZipEntryName = "data/game_save.sav",
                            FileSizeBytes = 2000,
                            LastWriteTimeUtc = DateTime.UtcNow.AddHours(-1)
                        }
                    }
                };

                var mEntry = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(mEntry.Open()))
                {
                    await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(manifest));
                }

                var dEntry = archive.CreateEntry("data/game_save.sav");
                using (var dStream = dEntry.Open())
                {
                    await dStream.WriteAsync(smallContent);
                }
            }

            var restoreService = new RestoreService();
            var options = new PathConversionOptions
            {
                TargetUserProfile = tempDir,
                TargetUsername = "TestTarget",
                OverwriteExisting = true
            };

            var result = await restoreService.RestoreBackupAsync(zipPath, options);
            Assert.True(result.IsSuccess);

            // Verifica que o arquivo NÃO foi rebaixado e manteve os 5000 bytes originais
            var finalLen = new FileInfo(targetDest).Length;
            Assert.Equal(5000, finalLen);

            var finalBytes = await File.ReadAllBytesAsync(targetDest);
            Assert.Equal((byte)0xAA, finalBytes[0]);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Should_Not_Discard_Userdata_Entries_When_SourceSteamId3_Differs_From_Path_Segment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SSM_UserdataTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "backup_diff_id.zip");
            var steamPath = Path.Combine(tempDir, "Steam");
            Directory.CreateDirectory(steamPath);

            var saveBytes = System.Text.Encoding.UTF8.GetBytes("SAVE_FROM_ACCOUNT_1879694565");

            using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var manifest = new BackupManifest
                {
                    GameName = "Multi Account Game",
                    AppId = 99999,
                    SourceUsername = "User",
                    SourceSteamId3 = 81405752, // Manifesto gravou o ID ativo da máquina de origem
                    Entries = new System.Collections.Generic.List<BackupManifestEntry>
                    {
                        new()
                        {
                            // Mas o save real estava na pasta da conta secundária 1879694565
                            TokenizedPath = @"%STEAM_USERDATA%\1879694565\99999\remote\save.bin",
                            OriginalFullPath = @"C:\Steam\userdata\1879694565\99999\remote\save.bin",
                            ZipEntryName = "data/save.bin",
                            FileSizeBytes = saveBytes.Length,
                            LastWriteTimeUtc = DateTime.UtcNow
                        }
                    }
                };

                var mEntry = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(mEntry.Open()))
                {
                    await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(manifest));
                }

                var dEntry = archive.CreateEntry("data/save.bin");
                using (var dStream = dEntry.Open())
                {
                    await dStream.WriteAsync(saveBytes);
                }
            }

            var restoreService = new RestoreService();
            var options = new PathConversionOptions
            {
                TargetSteamPath = steamPath,
                TargetSteamId3 = 81405752, // Quer restaurar para a conta 81405752
                TargetUsername = "User",
                OverwriteExisting = true
            };

            var result = await restoreService.RestoreBackupAsync(zipPath, options);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.SuccessCount);

            // O arquivo deve ter sido restaurado com sucesso na pasta da conta 81405752!
            var expectedPath = Path.Combine(steamPath, "userdata", "81405752", "99999", "remote", "save.bin");
            Assert.True(File.Exists(expectedPath), $"Arquivo deveria existir em: {expectedPath}");

            var content = await File.ReadAllTextAsync(expectedPath);
            Assert.Equal("SAVE_FROM_ACCOUNT_1879694565", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
