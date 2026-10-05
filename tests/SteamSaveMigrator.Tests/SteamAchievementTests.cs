using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class SteamAchievementTests : IDisposable
{
    private readonly string _tempDir;

    public SteamAchievementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_AchTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Limpeza de testes
        }
    }

    [Fact]
    public void BackupManifest_SerializesAndDeserializesAchievementsProperly()
    {
        var manifest = new BackupManifest
        {
            GameName = "Test Game",
            AppId = 12345,
            SourceSteamId3 = 111222333,
            SourceUsername = "TestUser",
            CreatedUtc = DateTime.UtcNow,
            Achievements = new List<GameAchievementInfo>
            {
                new GameAchievementInfo
                {
                    Id = "ACH_WIN_GAME",
                    DisplayName = "Vencedor",
                    Description = "Vença o jogo na dificuldade máxima",
                    IsUnlocked = true,
                    UnlockTimeUtc = DateTime.UtcNow
                },
                new GameAchievementInfo
                {
                    Id = "ACH_FIRST_STEP",
                    DisplayName = "Primeiros Passos",
                    Description = "Inicie sua jornada",
                    IsUnlocked = true,
                    UnlockTimeUtc = DateTime.UtcNow.AddHours(-5)
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var restored = JsonSerializer.Deserialize<BackupManifest>(json);

        Assert.NotNull(restored);
        Assert.Equal("Test Game", restored.GameName);
        Assert.Equal(12345u, restored.AppId);
        Assert.NotNull(restored.Achievements);
        Assert.Equal(2, restored.Achievements.Count);
        Assert.Equal("ACH_WIN_GAME", restored.Achievements[0].Id);
        Assert.True(restored.Achievements[0].IsUnlocked);
    }

    [Fact]
    public void SteamAchievementService_ReturnsEmptyWhenFilesNotExist()
    {
        var service = new SteamAchievementService();
        var fakeSteamPath = Path.Combine(_tempDir, "FakeSteam");
        Directory.CreateDirectory(fakeSteamPath);

        var achievements = service.GetGameAchievements(fakeSteamPath, 99999, 123456);

        Assert.NotNull(achievements);
        Assert.Empty(achievements);
    }

    [Fact]
    public void SteamAchievementService_SyncAchievements_CreatesTargetStatsFileWithPendingChanges()
    {
        var service = new SteamAchievementService();
        var steamPath = Path.Combine(_tempDir, "SteamRoot");
        var appCacheStats = Path.Combine(steamPath, "appcache", "stats");
        Directory.CreateDirectory(appCacheStats);

        uint appId = 730;
        uint targetSteamId = 987654;

        var achievements = new List<GameAchievementInfo>
        {
            new GameAchievementInfo
            {
                Id = "ACH_SAMPLE_1",
                DisplayName = "Conquista 1",
                IsUnlocked = true,
                UnlockTimeUtc = DateTime.UtcNow
            },
            new GameAchievementInfo
            {
                Id = "ACH_SAMPLE_2",
                DisplayName = "Conquista 2",
                IsUnlocked = true,
                UnlockTimeUtc = DateTime.UtcNow
            }
        };

        int synced = service.SyncAchievementsToTargetAccount(steamPath, appId, targetSteamId, achievements, null);

        Assert.Equal(2, synced);

        var targetStatsFile = Path.Combine(appCacheStats, $"UserGameStats_{targetSteamId}_{appId}.bin");
        Assert.True(File.Exists(targetStatsFile));

        var bytes = File.ReadAllBytes(targetStatsFile);
        Assert.True(bytes.Length > 0);

        // Verifica se a string da conquista foi gravada no binário
        var binaryText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("ACH_SAMPLE_1", binaryText);
        Assert.Contains("ACH_SAMPLE_2", binaryText);
    }

    [Fact]
    public void SteamAchievementService_CopiesSourceStatsIfAvailable()
    {
        var service = new SteamAchievementService();
        var steamPath = Path.Combine(_tempDir, "SteamRootWithSource");
        var appCacheStats = Path.Combine(steamPath, "appcache", "stats");
        Directory.CreateDirectory(appCacheStats);

        uint appId = 400;
        uint sourceSteamId = 11111;
        uint targetSteamId = 22222;

        var sourceStatsFile = Path.Combine(appCacheStats, $"UserGameStats_{sourceSteamId}_{appId}.bin");
        File.WriteAllBytes(sourceStatsFile, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var achievements = new List<GameAchievementInfo>
        {
            new GameAchievementInfo { Id = "ACH_TEST", IsUnlocked = true }
        };

        int synced = service.SyncAchievementsToTargetAccount(steamPath, appId, targetSteamId, achievements, sourceSteamId);

        Assert.Equal(1, synced);

        var targetStatsFile = Path.Combine(appCacheStats, $"UserGameStats_{targetSteamId}_{appId}.bin");
        Assert.True(File.Exists(targetStatsFile));
        var targetBytes = File.ReadAllBytes(targetStatsFile);
        // Deve ter copiado os bytes base da origem
        Assert.True(targetBytes.Length >= 4);
    }

    [Fact]
    public void SteamAchievementService_MultiGroupAchievements_MatchesAccuratelyWithoutDuplicates()
    {
        var service = new SteamAchievementService();
        var steamPath = Path.Combine(_tempDir, "SteamMultiGroup");
        var appCacheStats = Path.Combine(steamPath, "appcache", "stats");
        Directory.CreateDirectory(appCacheStats);

        uint appId = 3914860;
        uint steamId3 = 81405752;

        // Cria schema binário simulado com 3 conquistas
        var schemaFile = Path.Combine(appCacheStats, $"UserGameStatsSchema_{appId}.bin");
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            // Ach 1: Grupo 2, Bit 7
            bw.Write((byte)1); // String
            WriteNullTerminated(bw, "name");
            WriteNullTerminated(bw, "ACH_MOSS2_WINDOW_ONE");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "token");
            WriteNullTerminated(bw, "NEW_ACHIEVEMENT_2_7_NAME");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "english");
            WriteNullTerminated(bw, "Restoration");

            // Ach 2: Grupo 2, Bit 8 (não desbloqueada)
            bw.Write((byte)1);
            WriteNullTerminated(bw, "name");
            WriteNullTerminated(bw, "ACH_MOSS2_WINDOW_TWO");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "token");
            WriteNullTerminated(bw, "NEW_ACHIEVEMENT_2_8_NAME");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "english");
            WriteNullTerminated(bw, "Next Step");

            // Ach 3: Grupo 3, Bit 13
            bw.Write((byte)1);
            WriteNullTerminated(bw, "name");
            WriteNullTerminated(bw, "ACH_MOSS2_BOND_REUNITED");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "token");
            WriteNullTerminated(bw, "NEW_ACHIEVEMENT_3_13_NAME");

            bw.Write((byte)1);
            WriteNullTerminated(bw, "english");
            WriteNullTerminated(bw, "Unbreakable Bond");

            File.WriteAllBytes(schemaFile, ms.ToArray());
        }

        // Cria UserGameStats com subblocos
        var userStatsFile = Path.Combine(appCacheStats, $"UserGameStats_{steamId3}_{appId}.bin");
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            // SubBlock "cache"
            bw.Write((byte)0);
            WriteNullTerminated(bw, "cache");

            // SubBlock "2"
            bw.Write((byte)0);
            WriteNullTerminated(bw, "2");

            bw.Write((byte)0);
            WriteNullTerminated(bw, "AchievementTimes");

            // Bit 7 desbloqueado
            bw.Write((byte)2);
            WriteNullTerminated(bw, "7");
            bw.Write(1791129287);

            bw.Write((byte)8); // End AchievementTimes
            bw.Write((byte)8); // End SubBlock 2

            // SubBlock "3"
            bw.Write((byte)0);
            WriteNullTerminated(bw, "3");

            bw.Write((byte)0);
            WriteNullTerminated(bw, "AchievementTimes");

            // Bit 13 desbloqueado
            bw.Write((byte)2);
            WriteNullTerminated(bw, "13");
            bw.Write(1791130251);

            bw.Write((byte)8); // End AchievementTimes
            bw.Write((byte)8); // End SubBlock 3

            bw.Write((byte)8); // End cache

            File.WriteAllBytes(userStatsFile, ms.ToArray());
        }

        var results = service.GetGameAchievements(steamPath, appId, steamId3);

        Assert.Equal(3, results.Count);

        var ach1 = results.First(a => a.Id == "ACH_MOSS2_WINDOW_ONE");
        Assert.True(ach1.IsUnlocked);
        Assert.Equal(2, ach1.GroupId);
        Assert.Equal(7, ach1.BitId);

        var ach2 = results.First(a => a.Id == "ACH_MOSS2_WINDOW_TWO");
        Assert.False(ach2.IsUnlocked); // Não deve estar desbloqueada!

        var ach3 = results.First(a => a.Id == "ACH_MOSS2_BOND_REUNITED");
        Assert.True(ach3.IsUnlocked);
        Assert.Equal(3, ach3.GroupId);
        Assert.Equal(13, ach3.BitId);

        Assert.Equal(2, results.Count(a => a.IsUnlocked));
    }

    [Fact]
    public void SteamAchievementService_MossGame_MatchesExactlyFourAchievementsIfInstalled()
    {
        var steamDir = @"C:\Program Files (x86)\Steam";
        var schemaFile = Path.Combine(steamDir, "appcache", "stats", "UserGameStatsSchema_3914860.bin");
        var userStatsFile = Path.Combine(steamDir, "appcache", "stats", "UserGameStats_81405752_3914860.bin");

        if (!File.Exists(schemaFile) || !File.Exists(userStatsFile))
        {
            return;
        }

        var service = new SteamAchievementService();
        var results = service.GetGameAchievements(steamDir, 3914860, 81405752);

        Assert.Equal(46, results.Count);
        var unlocked = results.Where(a => a.IsUnlocked).ToList();
        Assert.Equal(4, unlocked.Count);

        Assert.Contains(unlocked, a => a.Id == "ACH_MOSS2_WINDOW_ONE");
        Assert.Contains(unlocked, a => a.Id == "ACH_MOSS2_WEAPON_CHAKRAM");
        Assert.Contains(unlocked, a => a.Id == "ACH_MOSS2_ENEMY_SCREECHER");
        Assert.Contains(unlocked, a => a.Id == "ACH_MOSS2_BOND_REUNITED");
    }

    [Fact]
    public void UpdateExistingMossBackupManifest_ToMatchAccurateAchievements()
    {
        var backupPath = @"C:\Users\User\SteamSavesBackup\Moss__The_Forgotten_Relic_3914860_20261005_105159.zip";
        if (!File.Exists(backupPath)) return;

        var steamDir = @"C:\Program Files (x86)\Steam";
        var service = new SteamAchievementService();
        var accurateAchs = service.GetGameAchievements(steamDir, 3914860, 81405752);

        using (var zip = System.IO.Compression.ZipFile.Open(backupPath, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json");
            if (entry != null)
            {
                BackupManifest? manifest = null;
                using (var s = entry.Open())
                {
                    manifest = JsonSerializer.Deserialize<BackupManifest>(s);
                }

                if (manifest != null)
                {
                    var updated = manifest with { Achievements = accurateAchs };
                    entry.Delete();
                    var newEntry = zip.CreateEntry("manifest.json");
                    using (var ns = newEntry.Open())
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        JsonSerializer.Serialize(ns, updated, options);
                    }
                }
            }
        }
    }

    private static void WriteNullTerminated(BinaryWriter bw, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        bw.Write(bytes);
        bw.Write((byte)0);
    }
}


