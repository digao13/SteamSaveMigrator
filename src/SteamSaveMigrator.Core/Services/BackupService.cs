using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Services;

public record BackupProgressReport(
    int CurrentFileIndex,
    int TotalFiles,
    string CurrentFileName,
    long ProcessedBytes,
    long TotalBytes
);

/// <summary>
/// Serviço responsável por empacotar e compactar os saves de um jogo em um arquivo de backup com manifesto.
/// </summary>
public class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> CreateBackupAsync(
        SteamGame game,
        string outputZipPath,
        string? sourceUserProfile = null,
        string? steamPath = null,
        uint? sourceSteamId3 = null,
        string? sourceSteamPersonaName = null,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var userProfile = !string.IsNullOrWhiteSpace(sourceUserProfile)
            ? sourceUserProfile
            : WindowsKnownFolders.GetCurrentProfile();

        var username = Path.GetFileName(userProfile.TrimEnd('\\', '/'));

        var outputDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (File.Exists(outputZipPath))
        {
            File.Delete(outputZipPath);
        }

        // Identifica a conta Steam de origem associada a estes saves
        var (detectedId3, detectedId64, detectedPersona) = DetectSteamAccountFromGameSaves(game, steamPath);
        var finalSteamId3 = sourceSteamId3 ?? detectedId3;
        var finalSteamId64 = finalSteamId3.HasValue ? (0x0110000100000000UL | finalSteamId3.Value) : detectedId64;
        var finalPersona = sourceSteamPersonaName ?? detectedPersona;

        // Se encontrou ID mas não o nome de persona, tenta buscar nos metadados da Steam
        if (finalSteamId3.HasValue && string.IsNullOrWhiteSpace(finalPersona))
        {
            try
            {
                var steamInfo = Detectors.SteamDetector.Detect(steamPath);
                var matched = steamInfo.Accounts.FirstOrDefault(a => a.SteamId3 == finalSteamId3.Value);
                if (matched != null) finalPersona = matched.PersonaName;
            }
            catch { }
        }

        // Detecta conquistas já obtidas na conta de origem para permitir migração de conquistas
        var achievements = new System.Collections.Generic.List<GameAchievementInfo>();
        try
        {
            var achService = new SteamAchievementService();
            achievements = achService.GetGameAchievements(steamPath, game.AppId, finalSteamId3);
        }
        catch { }

        var manifest = new BackupManifest
        {
            CreatedUtc = DateTime.UtcNow,
            SourceMachineName = Environment.MachineName,
            SourceUsername = username,
            SourceUserProfile = userProfile,
            SourceSteamId3 = finalSteamId3,
            SourceSteamId64 = finalSteamId64,
            SourceSteamPersonaName = finalPersona,
            AppId = game.AppId,
            GameName = game.Name,
            Achievements = achievements
        };

        var allSaveFiles = new System.Collections.Generic.List<(SaveFileInfo File, GameSaveLocation Location)>();
        foreach (var loc in game.DetectedSaveLocations)
        {
            if (!loc.Exists) continue;
            foreach (var f in loc.Files)
            {
                allSaveFiles.Add((f, loc));
            }
        }

        int totalFiles = allSaveFiles.Count;
        long totalBytes = 0;
        foreach (var (f, _) in allSaveFiles) totalBytes += f.SizeBytes;

        using (var zipStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            int fileIndex = 0;
            long processedBytes = 0;

            foreach (var (saveFile, location) in allSaveFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileIndex++;

                progress?.Report(new BackupProgressReport(
                    CurrentFileIndex: fileIndex,
                    TotalFiles: totalFiles,
                    CurrentFileName: Path.GetFileName(saveFile.FullPath),
                    ProcessedBytes: processedBytes,
                    TotalBytes: totalBytes
                ));

                if (!File.Exists(saveFile.FullPath))
                {
                    continue;
                }

                // Tokeniza o caminho do arquivo
                var tokenizedPath = PathVariableConverter.Tokenize(saveFile.FullPath, userProfile, steamPath);

                // Caminho dentro do Zip (normalizado com barras para padrão zip)
                // Ex: saves/%LOCALAPPDATA%/EldenRing/save.sl2 -> saves/_LOCALAPPDATA_/EldenRing/save.sl2
                var safeZipPath = "data/" + tokenizedPath
                    .Replace(":", "")
                    .Replace("%", "_")
                    .Replace('\\', '/');

                var entry = archive.CreateEntry(safeZipPath, CompressionLevel.Optimal);
                string sha256;

                using (var sourceStream = new FileStream(saveFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var entryStream = entry.Open())
                using (var sha = SHA256.Create())
                {
                    // Copiar e calcular hash simultaneamente
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await entryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        processedBytes += read;

                        progress?.Report(new BackupProgressReport(
                            CurrentFileIndex: fileIndex,
                            TotalFiles: totalFiles,
                            CurrentFileName: Path.GetFileName(saveFile.FullPath),
                            ProcessedBytes: processedBytes,
                            TotalBytes: totalBytes
                        ));
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    sha256 = BitConverter.ToString(sha.Hash ?? Array.Empty<byte>()).Replace("-", "").ToLowerInvariant();
                }

                manifest.Entries.Add(new BackupManifestEntry
                {
                    OriginalFullPath = saveFile.FullPath,
                    TokenizedPath = tokenizedPath,
                    LocationType = location.LocationType,
                    ZipEntryName = safeZipPath,
                    FileSizeBytes = saveFile.SizeBytes,
                    LastWriteTimeUtc = saveFile.LastWriteTimeUtc,
                    Sha256Checksum = sha256
                });
            }

            // Grava o manifest.json na raiz do zip
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var manifestStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
            }
        }

        return outputZipPath;
    }

    /// <summary>
    /// Realiza o backup de múltiplos jogos de uma só vez para o diretório de destino.
    /// </summary>
    public async Task<BatchBackupResult> CreateBatchBackupAsync(
        System.Collections.Generic.IEnumerable<SteamGame> games,
        string outputDirectory,
        string? sourceUserProfile = null,
        string? steamPath = null,
        uint? sourceSteamId3 = null,
        string? sourceSteamPersonaName = null,
        IProgress<BatchBackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var gameList = new System.Collections.Generic.List<SteamGame>(games);
        int totalGames = gameList.Count;
        int currentGameIndex = 0;
        var generatedZips = new System.Collections.Generic.List<string>();
        var errors = new System.Collections.Generic.Dictionary<string, string>();
        int totalFilesArchived = 0;
        long totalBytesArchived = 0;

        foreach (var game in gameList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentGameIndex++;

            var sanitizedName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var outZip = Path.Combine(outputDirectory, $"{sanitizedName}_{game.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            var singleProgress = new Progress<BackupProgressReport>(report =>
            {
                double gamePerc = report.TotalFiles > 0 ? (double)report.CurrentFileIndex / report.TotalFiles * 100 : 0;
                double overallPerc = ((currentGameIndex - 1) + (gamePerc / 100.0)) / totalGames * 100;

                progress?.Report(new BatchBackupProgressReport
                {
                    TotalGames = totalGames,
                    CurrentGameIndex = currentGameIndex,
                    CurrentGameName = game.Name,
                    CurrentGameAppId = game.AppId,
                    CurrentFileIndex = report.CurrentFileIndex,
                    CurrentGameTotalFiles = report.TotalFiles,
                    CurrentFileName = report.CurrentFileName,
                    OverallPercent = overallPerc,
                    CurrentGamePercent = gamePerc
                });
            });

            try
            {
                var zipResult = await CreateBackupAsync(game, outZip, sourceUserProfile, steamPath, sourceSteamId3, sourceSteamPersonaName, singleProgress, cancellationToken);
                generatedZips.Add(zipResult);
                totalFilesArchived += game.TotalSaveFiles;
                totalBytesArchived += game.TotalSaveSizeBytes;
            }
            catch (Exception ex)
            {
                errors[game.Name] = ex.Message;
            }
        }

        return new BatchBackupResult
        {
            TotalGamesProcessed = totalGames,
            SuccessCount = generatedZips.Count,
            FailureCount = errors.Count,
            GeneratedZipFiles = generatedZips,
            Errors = errors,
            TotalFilesArchived = totalFilesArchived,
            TotalSizeBytesArchived = totalBytesArchived
        };
    }

    private static (uint? SteamId3, ulong? SteamId64, string? PersonaName) DetectSteamAccountFromGameSaves(
        SteamGame game,
        string? steamPath)
    {
        // 1. A partir de localizações do tipo SteamUserdata
        var candidateAccounts = new List<(uint SteamId3, ulong SteamId64, long TotalBytes, DateTime LastModified)>();

        foreach (var loc in game.DetectedSaveLocations)
        {
            if (loc.LocationType == SaveLocationType.SteamUserdata && !string.IsNullOrWhiteSpace(loc.SourcePath))
            {
                var segments = loc.SourcePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (segments[i].Equals("userdata", StringComparison.OrdinalIgnoreCase) &&
                        uint.TryParse(segments[i + 1], out var id3) && id3 > 0)
                    {
                        var id64 = 0x0110000100000000UL | id3;
                        long totalBytes = loc.Files.Sum(f => f.SizeBytes);
                        var lastMod = loc.Files.Any() ? loc.Files.Max(f => f.LastWriteTimeUtc) : DateTime.MinValue;
                        candidateAccounts.Add((id3, id64, totalBytes, lastMod));
                    }
                }
            }
        }

        if (candidateAccounts.Count > 0)
        {
            var best = candidateAccounts
                .OrderByDescending(c => c.TotalBytes)
                .ThenByDescending(c => c.LastModified)
                .First();
            return (best.SteamId3, best.SteamId64, null);
        }

        // 2. A partir de caminhos com SteamID64 (17 dígitos começando com 7656119, ex: Elden Ring, Palworld)
        foreach (var loc in game.DetectedSaveLocations)
        {
            foreach (var f in loc.Files)
            {
                var segments = f.FullPath.Split(new[] { '\\', '/' });
                foreach (var seg in segments)
                {
                    if (seg.Length == 17 && seg.StartsWith("7656119", StringComparison.Ordinal) && ulong.TryParse(seg, out var id64))
                    {
                        var id3 = (uint)(id64 & 0xFFFFFFFF);
                        return (id3, id64, null);
                    }
                }
            }
        }

        // 3. Fallback: conta ativa/conectada da Steam na máquina de origem
        try
        {
            var info = Detectors.SteamDetector.Detect(steamPath);
            if (info.ActiveAccount != null)
            {
                return (info.ActiveAccount.SteamId3, info.ActiveAccount.SteamId64, info.ActiveAccount.PersonaName);
            }
        }
        catch { }

        return (null, null, null);
    }
}

