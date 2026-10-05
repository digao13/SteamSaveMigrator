using System;
using System.IO;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class BackupAndRestoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _sourceUserDir;
    private readonly string _targetUserDir;
    private readonly string _backupZipPath;

    public BackupAndRestoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SSM_Tests_" + Guid.NewGuid().ToString("N"));
        _sourceUserDir = Path.Combine(_tempRoot, "Users", "USUARIO_ORIGEM");
        _targetUserDir = Path.Combine(_tempRoot, "Users", "NOVO_USUARIO");
        _backupZipPath = Path.Combine(_tempRoot, "backups", "test_backup.zip");

        Directory.CreateDirectory(_sourceUserDir);
        Directory.CreateDirectory(_targetUserDir);
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
    public async Task Should_Backup_And_Restore_Game_Saves_To_New_User()
    {
        // 1. Cria saves fictícios no perfil de USUARIO_ORIGEM
        var sourceLocalApp = Path.Combine(_sourceUserDir, "AppData", "Local", "EldenGame");
        var sourceDocs = Path.Combine(_sourceUserDir, "Documents", "EldenGame");
        Directory.CreateDirectory(sourceLocalApp);
        Directory.CreateDirectory(sourceDocs);

        var file1 = Path.Combine(sourceLocalApp, "profile.sav");
        var file2 = Path.Combine(sourceDocs, "progress.dat");

        await File.WriteAllTextAsync(file1, "SAVE_DATA_LEVEL_99");
        await File.WriteAllTextAsync(file2, "QUEST_PROGRESS_COMPLETED");

        var game = new SteamGame
        {
            AppId = 999999,
            Name = "Elden Game",
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
                            FullPath = file1,
                            RelativePath = "profile.sav",
                            SizeBytes = new FileInfo(file1).Length,
                            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file1)
                        }
                    }
                },
                new()
                {
                    LocationType = SaveLocationType.Documents,
                    SourcePath = sourceDocs,
                    Exists = true,
                    Files = new()
                    {
                        new()
                        {
                            FullPath = file2,
                            RelativePath = "progress.dat",
                            SizeBytes = new FileInfo(file2).Length,
                            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file2)
                        }
                    }
                }
            }
        };

        // 2. Realiza o backup
        var backupService = new BackupService();
        var zipResult = await backupService.CreateBackupAsync(
            game,
            _backupZipPath,
            sourceUserProfile: _sourceUserDir
        );

        Assert.True(File.Exists(zipResult));

        // 3. Lê e inspeciona o manifesto
        var restoreService = new RestoreService();
        var manifest = await restoreService.ReadManifestAsync(zipResult);

        Assert.NotNull(manifest);
        Assert.Equal("USUARIO_ORIGEM", manifest.SourceUsername);
        Assert.Equal(999999u, manifest.AppId);
        Assert.Equal(2, manifest.TotalFiles);

        // 4. Restaura no novo usuário NOVO_USUARIO
        var conversionOptions = new PathConversionOptions
        {
            SourceUsername = "USUARIO_ORIGEM",
            SourceUserProfile = _sourceUserDir,
            TargetUsername = "NOVO_USUARIO",
            TargetUserProfile = _targetUserDir,
            OverwriteExisting = true
        };

        var restoreResult = await restoreService.RestoreBackupAsync(zipResult, conversionOptions);

        Assert.True(restoreResult.IsSuccess);
        Assert.Equal(2, restoreResult.SuccessCount);

        // 5. Verifica se os arquivos foram criados no diretório do NOVO_USUARIO
        var expectedDest1 = Path.Combine(_targetUserDir, "AppData", "Local", "EldenGame", "profile.sav");
        var expectedDest2 = Path.Combine(_targetUserDir, "Documents", "EldenGame", "progress.dat");

        Assert.True(File.Exists(expectedDest1), $"Arquivo não encontrado em: {expectedDest1}");
        Assert.True(File.Exists(expectedDest2), $"Arquivo não encontrado em: {expectedDest2}");

        var content1 = await File.ReadAllTextAsync(expectedDest1);
        var content2 = await File.ReadAllTextAsync(expectedDest2);

        Assert.Equal("SAVE_DATA_LEVEL_99", content1);
        Assert.Equal("QUEST_PROGRESS_COMPLETED", content2);
    }
}
