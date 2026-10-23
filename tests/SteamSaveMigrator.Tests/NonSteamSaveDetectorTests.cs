using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SteamSaveMigrator.Core;
using SteamSaveMigrator.Core.Detectors;
using SteamSaveMigrator.Core.Models;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class NonSteamSaveDetectorTests : IDisposable
{
    private readonly string _testRoot;

    public NonSteamSaveDetectorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_NonSteamTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public void GetKnownSearchLocations_ContainsMajorCracksAndEmulators()
    {
        var locations = NonSteamSaveDetector.GetKnownSearchLocations();

        Assert.NotEmpty(locations);

        var sources = locations.Select(l => l.CrackSource).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Verifica que todos os grupos solicitados estão cobertos
        Assert.Contains("CODEX", sources);
        Assert.Contains("Rune", sources);
        Assert.Contains("PLAZA", sources);
        Assert.Contains("CPY", sources);
        Assert.Contains("HOODLUM", sources);
        Assert.Contains("Voices38", sources);
        Assert.Contains("Goldberg", sources);
    }

    [Fact]
    public async Task DetectAllAsync_DetectsCodexPlazaRuneCpyHoodlumVoices38()
    {
        // 1. Simula CODEX (Elden Ring - AppId 1245620)
        var codexDir = Path.Combine(_testRoot, "CODEX", "1245620", "remote");
        Directory.CreateDirectory(codexDir);
        File.WriteAllText(Path.Combine(codexDir, "ER0000.sl2"), "dummy save data elden ring");

        // 2. Simula Rune (Cyberpunk 2077 - AppId 1091500)
        var runeDir = Path.Combine(_testRoot, "RUNE", "1091500", "remote");
        Directory.CreateDirectory(runeDir);
        File.WriteAllText(Path.Combine(runeDir, "sav.dat"), "dummy save cyberpunk");

        // 3. Simula PLAZA (The Witcher 3 - AppId 292030)
        var plazaDir = Path.Combine(_testRoot, "PLAZA", "292030");
        Directory.CreateDirectory(plazaDir);
        File.WriteAllText(Path.Combine(plazaDir, "ManualSave_0.sav"), "dummy save witcher");

        // 4. Simula CPY (Sekiro - AppId 814380 dentro de CPY_SAVES\Player)
        var cpyDir = Path.Combine(_testRoot, "CPY_SAVES", "Player", "814380");
        Directory.CreateDirectory(cpyDir);
        File.WriteAllText(Path.Combine(cpyDir, "S0000.sl2"), "dummy save sekiro");

        // 5. Simula HOODLUM (Dark Souls PTDE - AppId 211420)
        var hoodlumDir = Path.Combine(_testRoot, "HOODLUM", "211420");
        Directory.CreateDirectory(hoodlumDir);
        File.WriteAllText(Path.Combine(hoodlumDir, "DRAKS0005.sl2"), "dummy save ds1");

        // 6. Simula Voices38 (Baldur's Gate 3 - AppId 1086940)
        var voicesDir = Path.Combine(_testRoot, "Voices38", "1086940");
        Directory.CreateDirectory(voicesDir);
        File.WriteAllText(Path.Combine(voicesDir, "Save01.lsv"), "dummy save bg3");

        // Executa a detecção passando as pastas personalizadas simuladas
        var customFolders = new[]
        {
            Path.Combine(_testRoot, "CODEX"),
            Path.Combine(_testRoot, "RUNE"),
            Path.Combine(_testRoot, "PLAZA"),
            Path.Combine(_testRoot, "CPY_SAVES"),
            Path.Combine(_testRoot, "HOODLUM"),
            Path.Combine(_testRoot, "Voices38")
        };

        var detected = await NonSteamSaveDetector.DetectAllAsync(customFolders);

        Assert.NotNull(detected);
        Assert.True(detected.Count >= 6, $"Esperava ao menos 6 jogos detectados, encontrou {detected.Count}");

        // Verifica identificação correta de nomes e AppIDs
        var eldenRing = detected.FirstOrDefault(d => d.AppId == 1245620);
        Assert.NotNull(eldenRing);
        Assert.Equal("ELDEN RING", eldenRing.GameName);
        Assert.True(eldenRing.FileCount > 0);

        var cyberpunk = detected.FirstOrDefault(d => d.AppId == 1091500);
        Assert.NotNull(cyberpunk);
        Assert.Equal("Cyberpunk 2077", cyberpunk.GameName);

        var witcher = detected.FirstOrDefault(d => d.AppId == 292030);
        Assert.NotNull(witcher);
        Assert.Equal("The Witcher 3: Wild Hunt", witcher.GameName);

        var sekiro = detected.FirstOrDefault(d => d.AppId == 814380);
        Assert.NotNull(sekiro);
        Assert.Equal("Sekiro: Shadows Die Twice", sekiro.GameName);

        var ds1 = detected.FirstOrDefault(d => d.AppId == 211420);
        Assert.NotNull(ds1);
        Assert.Equal("DARK SOULS: Prepare to Die Edition", ds1.GameName);

        var bg3 = detected.FirstOrDefault(d => d.AppId == 1086940);
        Assert.NotNull(bg3);
        Assert.Equal("Baldur's Gate 3", bg3.GameName);
    }

    [Fact]
    public async Task DetectAllAsync_ResolvesNameFromIniConfiguration()
    {
        // Jogo com AppId desconhecido mas que possui steam_emu.ini com AppName
        var customGameDir = Path.Combine(_testRoot, "CustomEmulator", "9999999");
        Directory.CreateDirectory(customGameDir);
        File.WriteAllText(Path.Combine(customGameDir, "save.bin"), "save content");
        File.WriteAllText(Path.Combine(customGameDir, "steam_emu.ini"),
            "[Settings]\nAppName=Meu Jogo Independente Incrivel\nAppId=9999999\nLanguage=brazilian\n");

        var detected = await NonSteamSaveDetector.DetectAllAsync(new[] { Path.Combine(_testRoot, "CustomEmulator") });

        var customGame = detected.FirstOrDefault(d => d.AppId == 9999999);
        Assert.NotNull(customGame);
        Assert.Equal("Meu Jogo Independente Incrivel", customGame.GameName);
    }

    [Fact]
    public async Task BackupNonSteamGameAsync_CreatesCompatibleZipAndManifest()
    {
        var gameSaveDir = Path.Combine(_testRoot, "GameSaveDir");
        Directory.CreateDirectory(gameSaveDir);
        File.WriteAllText(Path.Combine(gameSaveDir, "slot1.sav"), "Slot 1 Save Content");
        File.WriteAllText(Path.Combine(gameSaveDir, "settings.cfg"), "Settings Content");

        var nonSteamSave = new NonSteamGameSave
        {
            AppId = 1245620,
            GameName = "ELDEN RING",
            CrackSource = "Rune",
            SavePath = gameSaveDir,
            FileCount = 2,
            TotalSizeBytes = 500,
            Files = SaveLocationDetector.ScanDirectoryFiles(gameSaveDir)
        };

        var engine = new SteamSaveMigratorEngine();
        var outputZip = Path.Combine(_testRoot, "BackupOutput", "EldenRing_Rune_Backup.zip");

        var resultZipPath = await engine.BackupNonSteamGameAsync(nonSteamSave, outputZip);

        Assert.True(File.Exists(resultZipPath));
        Assert.True(new FileInfo(resultZipPath).Length > 0);

        // Verifica que o manifesto gerado é perfeitamente válido para o sistema de restauração
        var manifest = await engine.ReadBackupManifestAsync(resultZipPath);
        Assert.NotNull(manifest);
        Assert.Equal(1245620u, manifest.AppId);
        Assert.Equal("ELDEN RING", manifest.GameName);
        Assert.Equal("Rune", manifest.SourceSteamPersonaName);
        Assert.Equal(2, manifest.Entries.Count);

        // Confirma que o zip contém o manifest.json e os arquivos do save
        using var archive = ZipFile.OpenRead(resultZipPath);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("slot1.sav")));
    }

    [Fact]
    public async Task BackupMultipleNonSteamGamesAsync_SavesAllGamesInBatch()
    {
        var game1Dir = Path.Combine(_testRoot, "BatchGame1");
        var game2Dir = Path.Combine(_testRoot, "BatchGame2");
        Directory.CreateDirectory(game1Dir);
        Directory.CreateDirectory(game2Dir);

        File.WriteAllText(Path.Combine(game1Dir, "game1.dat"), "Game 1 Save");
        File.WriteAllText(Path.Combine(game2Dir, "game2.dat"), "Game 2 Save");

        var saves = new List<NonSteamGameSave>
        {
            new()
            {
                AppId = 1091500,
                GameName = "Cyberpunk 2077",
                CrackSource = "CODEX",
                SavePath = game1Dir,
                FileCount = 1,
                TotalSizeBytes = 100,
                Files = SaveLocationDetector.ScanDirectoryFiles(game1Dir),
                IsSelected = true
            },
            new()
            {
                AppId = 292030,
                GameName = "The Witcher 3: Wild Hunt",
                CrackSource = "PLAZA",
                SavePath = game2Dir,
                FileCount = 1,
                TotalSizeBytes = 200,
                Files = SaveLocationDetector.ScanDirectoryFiles(game2Dir),
                IsSelected = true
            }
        };

        var engine = new SteamSaveMigratorEngine();
        var batchOutputDir = Path.Combine(_testRoot, "BatchOutput");

        var batchResult = await engine.BackupMultipleNonSteamGamesAsync(saves, batchOutputDir);

        Assert.NotNull(batchResult);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(2, batchResult.GeneratedZipFiles.Count);

        foreach (var zip in batchResult.GeneratedZipFiles)
        {
            Assert.True(File.Exists(zip));
            var manifest = await engine.ReadBackupManifestAsync(zip);
            Assert.NotNull(manifest);
            Assert.True(manifest.AppId == 1091500u || manifest.AppId == 292030u);
        }
    }
}
