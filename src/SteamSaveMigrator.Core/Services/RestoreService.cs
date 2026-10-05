using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Services;

public record RestoreProgressReport(
    int CurrentFileIndex,
    int TotalFiles,
    string FileName,
    string DestinationPath
);

/// <summary>
/// Serviço responsável por inspecionar, converter caminhos e restaurar backups de saves.
/// </summary>
public class RestoreService
{
    public async Task<BackupManifest?> ReadManifestAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Arquivo de backup não encontrado: {zipPath}", zipPath);

        using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null) return null;

            using (var stream = manifestEntry.Open())
            {
                return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(
        string zipPath,
        PathConversionOptions? options = null,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            return new RestoreResult
            {
                IsSuccess = false,
                GlobalErrors = new List<string> { $"Arquivo de backup não encontrado: {zipPath}" }
            };
        }

        using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    GlobalErrors = new List<string> { "O arquivo de backup não contém o arquivo manifest.json." }
                };
            }

            BackupManifest? manifest;
            using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);
            }

            if (manifest == null)
            {
                return new RestoreResult
                {
                    IsSuccess = false,
                    GlobalErrors = new List<string> { "Não foi possível desserializar o manifesto do backup." }
                };
            }

            // Se as opções não especificaram usuário de origem, herda do manifesto ou detecta dos caminhos
            var detectedSourceSteamId3 = options?.SourceSteamId3 ?? manifest.SourceSteamId3 ?? DetectSourceSteamId(manifest);

            var effectiveOptions = (options ?? new PathConversionOptions()) with
            {
                SourceUsername = !string.IsNullOrWhiteSpace(options?.SourceUsername)
                    ? options.SourceUsername
                    : manifest.SourceUsername,
                SourceUserProfile = !string.IsNullOrWhiteSpace(options?.SourceUserProfile)
                    ? options.SourceUserProfile
                    : manifest.SourceUserProfile,
                SourceSteamId3 = detectedSourceSteamId3,
                SourceSteamPersonaName = options?.SourceSteamPersonaName ?? manifest.SourceSteamPersonaName
            };

            var targetFolders = !string.IsNullOrWhiteSpace(effectiveOptions.TargetUserProfile)
                ? WindowsKnownFolders.GetFoldersForUserProfile(effectiveOptions.TargetUserProfile)
                : WindowsKnownFolders.GetFoldersForUsername(effectiveOptions.TargetUsername);

            var steamDir = !string.IsNullOrWhiteSpace(effectiveOptions.TargetSteamPath)
                ? effectiveOptions.TargetSteamPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

            var items = new List<RestoredItemReport>();
            int successCount = 0;
            int skippedCount = 0;
            int errorCount = 0;
            int fileIndex = 0;
            int total = manifest.Entries.Count;

            // Se o usuário especificou explicitamente uma conta de origem nas opções para desambiguação,
            // filtra entradas de userdata de outras contas secundárias. Caso contrário, mantém todas as entradas do backup.
            var validEntries = manifest.Entries;
            if (options?.SourceSteamId3.HasValue == true)
            {
                var explicitSourceId = options.SourceSteamId3.Value;
                var userdataAccountIds = manifest.Entries
                    .Where(e => e.TokenizedPath.StartsWith(PathVariableConverter.TokenSteamUserdata, StringComparison.OrdinalIgnoreCase))
                    .Select(e =>
                    {
                        var segs = e.TokenizedPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        return (segs.Length >= 2 && uint.TryParse(segs[1], out var id)) ? (uint?)id : null;
                    })
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList();

                if (userdataAccountIds.Count > 1 && userdataAccountIds.Contains(explicitSourceId))
                {
                    validEntries = manifest.Entries.Where(e =>
                    {
                        if (e.TokenizedPath.StartsWith(PathVariableConverter.TokenSteamUserdata, StringComparison.OrdinalIgnoreCase))
                        {
                            var segs = e.TokenizedPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                            if (segs.Length >= 2 && uint.TryParse(segs[1], out var idInPath))
                            {
                                return idInPath == explicitSourceId;
                            }
                        }
                        return true;
                    }).ToList();
                }
            }

            // Ordena as entradas para que arquivos com maior tamanho e data mais recente sejam restaurados primeiro,
            // garantindo que saves com progresso real prevaleçam sobre saves vazios ou intermediários
            var sortedEntries = validEntries
                .OrderByDescending(e => e.FileSizeBytes)
                .ThenByDescending(e => e.LastWriteTimeUtc)
                .ToList();

            // Mapeia destinos já restaurados e seus respectivos tamanhos para blindar contra sobrescrita por saves menores
            var restoredDestinations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sortedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileIndex++;

                // Resolve o caminho de destino no novo usuário e novo perfil Steam
                var destPath = PathVariableConverter.Resolve(entry.TokenizedPath, effectiveOptions);

                progress?.Report(new RestoreProgressReport(
                    CurrentFileIndex: fileIndex,
                    TotalFiles: total,
                    FileName: Path.GetFileName(destPath),
                    DestinationPath: destPath
                ));

                var zipEntry = archive.GetEntry(entry.ZipEntryName);
                if (zipEntry == null)
                {
                    errorCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = false,
                        ErrorMessage = $"Arquivo '{entry.ZipEntryName}' não encontrado no arquivo ZIP."
                    });
                    continue;
                }

                // 1. Blindagem de sessão: se este destino já foi restaurado nesta sessão por uma entrada maior ou idêntica,
                // não permite sobrescrever com uma versão duplicada/inferior do pacote
                if (restoredDestinations.TryGetValue(destPath, out var rSize) && rSize >= entry.FileSizeBytes)
                {
                    skippedCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = true,
                        WasOverwritten = false,
                        ErrorMessage = $"Preservada versão com maior progresso já restaurada ({rSize:N0} bytes mantidos, versão de {entry.FileSizeBytes:N0} bytes ignorada)."
                    });
                    continue;
                }

                bool alreadyExists = File.Exists(destPath);
                if (alreadyExists && !effectiveOptions.OverwriteExisting)
                {
                    skippedCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = true,
                        WasOverwritten = false,
                        ErrorMessage = "Ignorado (arquivo já existente no destino)"
                    });
                    continue;
                }

                // 2. Blindagem de integridade local: se o save no disco local já tem progresso superior ao do pacote
                if (alreadyExists)
                {
                    long existingSize = 0;
                    try { existingSize = new FileInfo(destPath).Length; } catch { }

                    var ext = Path.GetExtension(destPath).ToLowerInvariant();
                    bool isSaveFile = ext is ".sav" or ".dat" or ".bin" or ".sl2" or ".save" or ".json";
                    if (existingSize > entry.FileSizeBytes && (isSaveFile || existingSize > 10240))
                    {
                        skippedCount++;
                        items.Add(new RestoredItemReport
                        {
                            OriginalPath = entry.OriginalFullPath,
                            TokenizedPath = entry.TokenizedPath,
                            DestinationPath = destPath,
                            FileSizeBytes = entry.FileSizeBytes,
                            Success = true,
                            WasOverwritten = false,
                            ErrorMessage = $"Preservado progresso avançado local: save local com {existingSize:N0} bytes mantido, backup com {entry.FileSizeBytes:N0} bytes ignorado."
                        });
                        continue;
                    }
                }

                if (effectiveOptions.DryRun)
                {
                    successCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = true,
                        WasOverwritten = alreadyExists
                    });
                    continue;
                }

                try
                {
                    var parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrWhiteSpace(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    using (var entryStream = zipEntry.Open())
                    using (var fileDestStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(fileDestStream, cancellationToken);
                    }

                    // Se for steam_autocloud.vdf, atualiza o accountid para o TargetSteamId3
                    if (Path.GetFileName(destPath).Equals("steam_autocloud.vdf", StringComparison.OrdinalIgnoreCase) && effectiveOptions.TargetSteamId3.HasValue)
                    {
                        try
                        {
                            var vdfContent = await File.ReadAllTextAsync(destPath, cancellationToken);
                            var updatedVdf = System.Text.RegularExpressions.Regex.Replace(
                                vdfContent,
                                @"""accountid""\s*""\d+""",
                                $"\"accountid\"\t\t\"{effectiveOptions.TargetSteamId3.Value}\"");
                            await File.WriteAllTextAsync(destPath, updatedVdf, cancellationToken);
                        }
                        catch { }
                    }

                    // Define timestamp UTC atual para a Steam Cloud saber que este arquivo é o mais recente
                    try
                    {
                        File.SetLastWriteTimeUtc(destPath, DateTime.UtcNow);
                    }
                    catch { }

                    restoredDestinations[destPath] = entry.FileSizeBytes;

                    // Espelhamento automático de Auto-Cloud (Unreal Engine / Moss / etc.)
                    if (effectiveOptions.TargetSteamId3.HasValue)
                    {
                        var targetIdStr = effectiveOptions.TargetSteamId3.Value.ToString();
                        var appIdStr = manifest.AppId.ToString();

                        // Caso A: Se o arquivo restaurado está em AppData\Local
                        if (destPath.StartsWith(targetFolders.AppDataLocal, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = destPath.Substring(targetFolders.AppDataLocal.Length).TrimStart('\\', '/');
                            var acLocalPath = Path.Combine(steamDir, "userdata", targetIdStr, appIdStr, "ac", "WinAppDataLocal", rel);
                            TryMirrorFile(destPath, acLocalPath, restoredDestinations);
                        }
                        // Caso B: Se o arquivo restaurado está em Documents
                        else if (destPath.StartsWith(targetFolders.Documents, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = destPath.Substring(targetFolders.Documents.Length).TrimStart('\\', '/');
                            var acDocPath = Path.Combine(steamDir, "userdata", targetIdStr, appIdStr, "ac", "WinMyDocuments", rel);
                            TryMirrorFile(destPath, acDocPath, restoredDestinations);
                        }
                        // Caso C: Se o arquivo restaurado veio de userdata com ac\WinAppDataLocal
                        else if (destPath.Contains($"\\userdata\\{targetIdStr}\\{appIdStr}\\ac\\WinAppDataLocal\\", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = $"\\userdata\\{targetIdStr}\\{appIdStr}\\ac\\WinAppDataLocal\\";
                            var idx = destPath.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0)
                            {
                                var rel = destPath.Substring(idx + token.Length).TrimStart('\\', '/');
                                var localAppPath = Path.Combine(targetFolders.AppDataLocal, rel);
                                TryMirrorFile(destPath, localAppPath, restoredDestinations);
                            }
                        }
                        // Caso D: Se o arquivo restaurado veio de userdata com ac\WinMyDocuments
                        else if (destPath.Contains($"\\userdata\\{targetIdStr}\\{appIdStr}\\ac\\WinMyDocuments\\", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = $"\\userdata\\{targetIdStr}\\{appIdStr}\\ac\\WinMyDocuments\\";
                            var idx = destPath.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0)
                            {
                                var rel = destPath.Substring(idx + token.Length).TrimStart('\\', '/');
                                var docPath = Path.Combine(targetFolders.Documents, rel);
                                TryMirrorFile(destPath, docPath, restoredDestinations);
                            }
                        }
                    }

                    successCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = true,
                        WasOverwritten = alreadyExists
                    });
                }
                catch (Exception ex)
                {
                    errorCount++;
                    items.Add(new RestoredItemReport
                    {
                        OriginalPath = entry.OriginalFullPath,
                        TokenizedPath = entry.TokenizedPath,
                        DestinationPath = destPath,
                        FileSizeBytes = entry.FileSizeBytes,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Pós-processamento avançado: Sincronização e reparação do remotecache.vdf da Steam
            if (effectiveOptions.TargetSteamId3.HasValue && !effectiveOptions.DryRun)
            {
                var targetUserdataDir = Path.Combine(steamDir, "userdata", effectiveOptions.TargetSteamId3.Value.ToString(), manifest.AppId.ToString());
                var remoteCacheVdf = Path.Combine(targetUserdataDir, "remotecache.vdf");
                await SyncRemoteCacheVdfAsync(remoteCacheVdf, manifest.AppId, items, targetFolders, cancellationToken);
            }

            // Sincronização automática de conquistas (Achievements)
            int achievementsSynced = 0;
            AchievementSyncResult? achievementSyncDetails = null;
            if (effectiveOptions.TargetSteamId3.HasValue && !effectiveOptions.DryRun && manifest.Achievements != null && manifest.Achievements.Count > 0)
            {
                try
                {
                    var achService = new SteamAchievementService();
                    achievementSyncDetails = achService.SyncAchievements(
                        steamDir,
                        manifest.AppId,
                        effectiveOptions.TargetSteamId3.Value,
                        manifest.Achievements,
                        detectedSourceSteamId3
                    );
                    achievementsSynced = achievementSyncDetails.SyncedCount;
                }
                catch { }
            }

            return new RestoreResult
            {
                IsSuccess = errorCount == 0,
                GameName = manifest.GameName,
                AppId = manifest.AppId,
                TotalFilesProcessed = total,
                SuccessCount = successCount,
                SkippedCount = skippedCount,
                ErrorCount = errorCount,
                SourceSteamId3 = effectiveOptions.SourceSteamId3,
                TargetSteamId3 = effectiveOptions.TargetSteamId3,
                TargetSteamPersonaName = effectiveOptions.TargetSteamPersonaName,
                AchievementsSynced = achievementsSynced,
                AchievementSyncDetails = achievementSyncDetails,
                Achievements = manifest.Achievements ?? new(),
                Items = items
            };
        }
    }

    /// <summary>
    /// Restauração 100% automática e transparente de um backup (.zip).
    /// Detecta automaticamente o perfil de origem gravado no manifesto e converte todos
    /// os caminhos para o usuário atualmente conectado no Windows e para o perfil Steam conectado no momento.
    /// </summary>
    public async Task<RestoreResult> AutoRestoreAsync(
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
        var manifest = await ReadManifestAsync(zipPath, cancellationToken);
        if (manifest == null)
        {
            return new RestoreResult
            {
                IsSuccess = false,
                GlobalErrors = new List<string> { "Não foi possível ler o manifesto do arquivo de backup." }
            };
        }

        var effectiveTargetUsername = !string.IsNullOrWhiteSpace(targetUsername)
            ? targetUsername
            : Environment.UserName;

        var effectiveTargetUserProfile = !string.IsNullOrWhiteSpace(targetUserProfile)
            ? targetUserProfile
            : (!string.IsNullOrWhiteSpace(targetUsername)
                ? WindowsKnownFolders.GetFoldersForUsername(targetUsername).UserProfile
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        // Se o targetSteamId3 não foi explicitamente passado, detecta o usuário Steam conectado atualmente
        string? targetPersonaName = null;
        if (!targetSteamId3.HasValue)
        {
            try
            {
                var steamInfo = Detectors.SteamDetector.Detect(targetSteamPath);
                if (steamInfo.IsFound && steamInfo.ActiveAccount != null)
                {
                    targetSteamId3 = steamInfo.ActiveAccount.SteamId3;
                    targetPersonaName = steamInfo.ActiveAccount.PersonaName;
                    targetSteamPath ??= steamInfo.SteamPath;
                }
            }
            catch { }
        }

        var autoOptions = new PathConversionOptions
        {
            SourceUsername = manifest.SourceUsername,
            SourceUserProfile = manifest.SourceUserProfile,
            SourceSteamId3 = manifest.SourceSteamId3 ?? DetectSourceSteamId(manifest),
            SourceSteamPersonaName = manifest.SourceSteamPersonaName,
            TargetUsername = effectiveTargetUsername,
            TargetUserProfile = effectiveTargetUserProfile,
            TargetSteamId3 = targetSteamId3,
            TargetSteamPersonaName = targetPersonaName,
            TargetSteamPath = targetSteamPath,
            OverwriteExisting = overwriteExisting,
            DryRun = dryRun
        };

        return await RestoreBackupAsync(zipPath, autoOptions, progress, cancellationToken);
    }

    /// <summary>
    /// Restaura múltiplos pacotes de backup automaticamente em lote.
    /// </summary>
    public async Task<BatchRestoreResult> AutoRestoreBatchAsync(
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
        var paths = new List<string>(zipPaths);
        var results = new List<RestoreResult>();
        int packageIndex = 0;
        int total = paths.Count;
        int successCount = 0;
        int failureCount = 0;
        int totalFiles = 0;

        foreach (var zip in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            packageIndex++;

            var res = await AutoRestoreAsync(
                zip,
                targetSteamId3: targetSteamId3,
                targetSteamPath: targetSteamPath,
                targetUsername: targetUsername,
                targetUserProfile: targetUserProfile,
                overwriteExisting: overwriteExisting,
                dryRun: dryRun,
                progress: null,
                cancellationToken: cancellationToken);
            results.Add(res);

            if (res.IsSuccess)
            {
                successCount++;
                totalFiles += res.SuccessCount;
            }
            else
            {
                failureCount++;
            }

            packageProgress?.Report((packageIndex, total, res.GameName));
        }

        return new BatchRestoreResult
        {
            TotalPackages = total,
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalFilesRestored = totalFiles,
            Results = results
        };
    }

    /// <summary>
    /// Detecta o SteamID3 de origem a partir das entradas do manifesto caso não tenha sido gravado explicitamente.
    /// </summary>
    public static uint? DetectSourceSteamId(BackupManifest manifest)
    {
        if (manifest.SourceSteamId3.HasValue && manifest.SourceSteamId3.Value > 0)
        {
            return manifest.SourceSteamId3.Value;
        }

        foreach (var entry in manifest.Entries)
        {
            if (entry.TokenizedPath.StartsWith(PathVariableConverter.TokenSteamUserdata, StringComparison.OrdinalIgnoreCase))
            {
                var relative = entry.TokenizedPath.Substring(PathVariableConverter.TokenSteamUserdata.Length).TrimStart('\\', '/');
                var slashIdx = relative.IndexOfAny(new[] { '\\', '/' });
                var firstSeg = slashIdx >= 0 ? relative.Substring(0, slashIdx) : relative;
                if (uint.TryParse(firstSeg, out var id3) && id3 > 0)
                {
                    return id3;
                }
            }

            var segments = entry.TokenizedPath.Split(new[] { '\\', '/' });
            foreach (var seg in segments)
            {
                if (seg.Length == 17 && seg.StartsWith("7656119", StringComparison.Ordinal) && ulong.TryParse(seg, out var id64))
                {
                    return (uint)(id64 & 0xFFFFFFFF);
                }
            }
        }

        return null;
    }

    private static void TryMirrorFile(string sourcePath, string destPath, Dictionary<string, long>? restoredDestinations = null)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                var srcLen = new FileInfo(sourcePath).Length;
                if (File.Exists(destPath))
                {
                    var destLen = new FileInfo(destPath).Length;
                    // Se o destino já possui um save maior ou idêntico, NUNCA rebaixa!
                    if (destLen >= srcLen)
                    {
                        if (restoredDestinations != null)
                        {
                            restoredDestinations[destPath] = destLen;
                        }
                        return;
                    }
                }

                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Copy(sourcePath, destPath, true);
                File.SetLastWriteTimeUtc(destPath, DateTime.UtcNow);

                if (restoredDestinations != null)
                {
                    restoredDestinations[destPath] = srcLen;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Sincroniza e regenera o arquivo remotecache.vdf da Steam para a conta de destino,
    /// calculando o hash SHA-1 real e marcando os arquivos com status sincronizado (syncstate 1).
    /// Isso impede que a Steam Cloud rebaixe ou delete o save restaurado ao abrir a Steam.
    /// </summary>
    private static async Task SyncRemoteCacheVdfAsync(
        string remoteCacheVdfPath,
        uint appId,
        List<RestoredItemReport> restoredItems,
        UserProfileFolders targetFolders,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDir = Path.GetDirectoryName(remoteCacheVdfPath);
            if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string vdfContent = File.Exists(remoteCacheVdfPath)
                ? await File.ReadAllTextAsync(remoteCacheVdfPath, cancellationToken)
                : $"\"{appId}\"\n{{\n\t\"ChangeNumber\"\t\t\"0\"\n\t\"OSType\"\t\t\"0\"\n}}\n";

            // Se o VDF estiver com syncstate 2 (marcado para exclusão),
            // a Steam deletaria o arquivo restaurado. Forçamos para 1 (sincronizado)
            vdfContent = System.Text.RegularExpressions.Regex.Replace(
                vdfContent,
                @"""syncstate""\s*""2""",
                "\"syncstate\"\t\t\"1\"");

            // Atualiza timestamps globais
            vdfContent = System.Text.RegularExpressions.Regex.Replace(
                vdfContent,
                @"""localtime""\s*""\d+""",
                $"\"localtime\"\t\t\"{nowUnix}\"");

            vdfContent = System.Text.RegularExpressions.Regex.Replace(
                vdfContent,
                @"""time""\s*""\d+""",
                $"\"time\"\t\t\"{nowUnix}\"");

            vdfContent = System.Text.RegularExpressions.Regex.Replace(
                vdfContent,
                @"""remotetime""\s*""\d+""",
                $"\"remotetime\"\t\t\"{nowUnix}\"");

            // Mapeia todos os arquivos que devem constar no remotecache.vdf:
            // 1. Arquivos físicos presentes na pasta ac/WinAppDataLocal e ac/WinMyDocuments
            var filesToRegister = new Dictionary<string, (string FullPath, string RootCode)>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
            {
                var acLocal = Path.Combine(targetDir, "ac", "WinAppDataLocal");
                if (Directory.Exists(acLocal))
                {
                    foreach (var f in Directory.GetFiles(acLocal, "*", SearchOption.AllDirectories))
                    {
                        if (f.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = Path.GetRelativePath(acLocal, f).Replace('\\', '/');
                        filesToRegister[rel] = (f, "3");
                    }
                }

                var acDocs = Path.Combine(targetDir, "ac", "WinMyDocuments");
                if (Directory.Exists(acDocs))
                {
                    foreach (var f in Directory.GetFiles(acDocs, "*", SearchOption.AllDirectories))
                    {
                        if (f.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = Path.GetRelativePath(acDocs, f).Replace('\\', '/');
                        filesToRegister[rel] = (f, "0");
                    }
                }
            }

            // 2. Arquivos restaurados com sucesso a partir de restoredItems
            foreach (var item in restoredItems.Where(i => i.Success && File.Exists(i.DestinationPath)))
            {
                var normalizedDest = item.DestinationPath.Replace('\\', '/');
                var acLocalIdx = normalizedDest.IndexOf("/ac/WinAppDataLocal/", StringComparison.OrdinalIgnoreCase);
                if (acLocalIdx >= 0)
                {
                    var relKey = normalizedDest.Substring(acLocalIdx + "/ac/WinAppDataLocal/".Length);
                    if (!string.IsNullOrWhiteSpace(relKey) && !relKey.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase))
                    {
                        filesToRegister[relKey] = (item.DestinationPath, "3");
                    }
                }
                else if (item.DestinationPath.StartsWith(targetFolders.AppDataLocal, StringComparison.OrdinalIgnoreCase))
                {
                    var relKey = item.DestinationPath.Substring(targetFolders.AppDataLocal.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(relKey) && !relKey.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase))
                    {
                        filesToRegister.TryAdd(relKey, (item.DestinationPath, "3"));
                    }
                }
            }

            // Atualiza ou insere cada arquivo no VDF
            foreach (var kvp in filesToRegister)
            {
                var relKey = kvp.Key;
                var (filePath, rootCode) = kvp.Value;
                if (!File.Exists(filePath)) continue;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0) continue;

                var sha1 = CalculateFileSha1(filePath);
                var keyPattern = $@"""{System.Text.RegularExpressions.Regex.Escape(relKey)}""\s*\{{[^}}]*\}}";
                var updatedBlock = $"\"{relKey}\"\n\t{{\n\t\t\"root\"\t\t\"{rootCode}\"\n\t\t\"size\"\t\t\"{fileInfo.Length}\"\n\t\t\"localtime\"\t\t\"{nowUnix}\"\n\t\t\"time\"\t\t\"{nowUnix}\"\n\t\t\"remotetime\"\t\t\"{nowUnix}\"\n\t\t\"sha\"\t\t\"{sha1}\"\n\t\t\"syncstate\"\t\t\"1\"\n\t\t\"persiststate\"\t\t\"0\"\n\t\t\"platformstosync2\"\t\t\"-1\"\n\t}}";

                if (System.Text.RegularExpressions.Regex.IsMatch(vdfContent, keyPattern, System.Text.RegularExpressions.RegexOptions.Singleline))
                {
                    vdfContent = System.Text.RegularExpressions.Regex.Replace(vdfContent, keyPattern, updatedBlock, System.Text.RegularExpressions.RegexOptions.Singleline);
                }
                else
                {
                    var lastClosingBrace = vdfContent.LastIndexOf('}');
                    if (lastClosingBrace >= 0)
                    {
                        vdfContent = vdfContent.Substring(0, lastClosingBrace) + "\t" + updatedBlock + "\n}\n";
                    }
                }
            }

            await File.WriteAllTextAsync(remoteCacheVdfPath, vdfContent, cancellationToken);
            File.SetLastWriteTimeUtc(remoteCacheVdfPath, DateTime.UtcNow);
        }
        catch { }
    }

    private static string CalculateFileSha1(string filePath)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return new string('0', 40);
        }
    }
}
