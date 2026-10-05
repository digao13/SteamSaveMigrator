using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Detectors;

/// <summary>
/// Detector inteligente de locais de save para jogos Steam no Windows.
/// </summary>
public static class SaveLocationDetector
{
    public static List<GameSaveLocation> DetectSaveLocations(
        SteamGame game,
        SteamInstallationInfo steamInfo,
        string? userProfilePath = null)
    {
        var locations = new List<GameSaveLocation>();
        var userProfile = !string.IsNullOrWhiteSpace(userProfilePath)
            ? userProfilePath
            : WindowsKnownFolders.GetCurrentProfile();

        var folders = WindowsKnownFolders.GetFoldersForUserProfile(userProfile);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Steam Userdata (Saves sincronizados via Steam Cloud local)
        if (steamInfo.IsFound && !string.IsNullOrWhiteSpace(steamInfo.SteamPath))
        {
            var userdataDir = Path.Combine(steamInfo.SteamPath, "userdata");
            if (Directory.Exists(userdataDir))
            {
                var steamAccounts = new List<string>();
                if (steamInfo.ActiveAccount != null)
                {
                    steamAccounts.Add(steamInfo.ActiveAccount.SteamId3.ToString());
                }
                else if (steamInfo.Accounts.Count > 0)
                {
                    steamAccounts.AddRange(steamInfo.Accounts.Select(a => a.SteamId3.ToString()));
                }
                else
                {
                    steamAccounts.AddRange(Directory.GetDirectories(userdataDir).Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))!);
                }

                foreach (var accDirName in steamAccounts)
                {
                    if (string.IsNullOrWhiteSpace(accDirName)) continue;
                    var gameUserdataPath = Path.Combine(userdataDir, accDirName, game.AppId.ToString());

                    if (Directory.Exists(gameUserdataPath))
                    {
                        var files = ScanDirectoryFiles(gameUserdataPath);
                        if (files.Count > 0 && seenPaths.Add(gameUserdataPath))
                        {
                            var tokenized = PathVariableConverter.Tokenize(gameUserdataPath, userProfile, steamInfo.SteamPath);
                            locations.Add(new GameSaveLocation
                            {
                                LocationType = SaveLocationType.SteamUserdata,
                                DisplayName = $"Steam Userdata ({accDirName})",
                                SourcePath = gameUserdataPath,
                                TokenizedPath = tokenized,
                                Exists = true,
                                FileCount = files.Count,
                                TotalSizeBytes = files.Sum(f => f.SizeBytes),
                                Files = files
                            });
                        }
                    }
                }
            }
        }

        // 2. Base de Dados Conhecida (KnownGameDatabase)
        var knownRules = KnownGameDatabase.GetRulesForApp(game.AppId);
        foreach (var rule in knownRules)
        {
            var targetBasePath = rule.LocationType switch
            {
                SaveLocationType.AppDataLocal => folders.AppDataLocal,
                SaveLocationType.AppDataLocalLow => folders.AppDataLocalLow,
                SaveLocationType.AppDataRoaming => folders.AppDataRoaming,
                SaveLocationType.Documents => folders.Documents,
                SaveLocationType.SavedGames => folders.SavedGames,
                _ => folders.UserProfile
            };

            var fullPath = Path.Combine(targetBasePath, rule.RelativePathFormat.Replace("{AppId}", game.AppId.ToString()));
            if (Directory.Exists(fullPath))
            {
                var files = ScanDirectoryFiles(fullPath);
                if (files.Count > 0 && seenPaths.Add(fullPath))
                {
                    var tokenized = PathVariableConverter.Tokenize(fullPath, userProfile, steamInfo.SteamPath);
                    locations.Add(new GameSaveLocation
                    {
                        LocationType = rule.LocationType,
                        DisplayName = rule.Description,
                        SourcePath = fullPath,
                        TokenizedPath = tokenized,
                        Exists = true,
                        FileCount = files.Count,
                        TotalSizeBytes = files.Sum(f => f.SizeBytes),
                        Files = files
                    });
                }
            }
            else if (File.Exists(fullPath))
            {
                // É um arquivo individual
                var fileInfo = new FileInfo(fullPath);
                if (seenPaths.Add(fullPath))
                {
                    var tokenized = PathVariableConverter.Tokenize(fullPath, userProfile, steamInfo.SteamPath);
                    locations.Add(new GameSaveLocation
                    {
                        LocationType = rule.LocationType,
                        DisplayName = rule.Description,
                        SourcePath = fullPath,
                        TokenizedPath = tokenized,
                        Exists = true,
                        FileCount = 1,
                        TotalSizeBytes = fileInfo.Length,
                        Files = new List<SaveFileInfo>
                        {
                            new()
                            {
                                FullPath = fullPath,
                                RelativePath = Path.GetFileName(fullPath),
                                SizeBytes = fileInfo.Length,
                                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                            }
                        }
                    });
                }
            }
        }

        // 3. Scanner Heurístico Dinâmico
        // Busca variações do nome do jogo e installdir nas pastas padrão do usuário
        var searchKeywords = GenerateSearchKeywords(game);

        var scanTargets = new (string Directory, SaveLocationType Type, string Name)[]
        {
            (folders.SavedGames, SaveLocationType.SavedGames, "Saved Games"),
            (Path.Combine(folders.Documents, "My Games"), SaveLocationType.Documents, "Documents\\My Games"),
            (folders.Documents, SaveLocationType.Documents, "Documents"),
            (folders.AppDataLocalLow, SaveLocationType.AppDataLocalLow, "AppData\\LocalLow"),
            (folders.AppDataRoaming, SaveLocationType.AppDataRoaming, "AppData\\Roaming"),
            (folders.AppDataLocal, SaveLocationType.AppDataLocal, "AppData\\Local")
        };

        foreach (var (dir, type, dirDisplayName) in scanTargets)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var keyword in searchKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 3) continue;

                try
                {
                    // Busca correspondência exata ou prefixada nas subpastas imediatas
                    var subDirs = Directory.GetDirectories(dir);
                    foreach (var subDir in subDirs)
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (IsMatch(dirName, keyword))
                        {
                            if (seenPaths.Contains(subDir)) continue;

                            // Se for pasta do desenvolvedor (ex: CD Projekt Red, FromSoftware, Rockstar Games),
                            // podemos checar subdiretórios internos
                            var matchingSubdir = CheckNestedDeveloperFolder(subDir, searchKeywords);
                            var effectivePath = matchingSubdir ?? subDir;

                            if (seenPaths.Add(effectivePath))
                            {
                                var files = ScanDirectoryFiles(effectivePath);
                                if (files.Count > 0)
                                {
                                    var tokenized = PathVariableConverter.Tokenize(effectivePath, userProfile, steamInfo.SteamPath);
                                    locations.Add(new GameSaveLocation
                                    {
                                        LocationType = type,
                                        DisplayName = $"{dirDisplayName} ({Path.GetFileName(effectivePath)})",
                                        SourcePath = effectivePath,
                                        TokenizedPath = tokenized,
                                        Exists = true,
                                        FileCount = files.Count,
                                        TotalSizeBytes = files.Sum(f => f.SizeBytes),
                                        Files = files
                                    });
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Erros de permissão
                }
            }
        }

        return locations;
    }

    private static string? CheckNestedDeveloperFolder(string parentDir, List<string> keywords)
    {
        try
        {
            var nested = Directory.GetDirectories(parentDir);
            foreach (var child in nested)
            {
                var childName = Path.GetFileName(child);
                foreach (var kw in keywords)
                {
                    if (IsMatch(childName, kw))
                    {
                        return child;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsMatch(string dirName, string keyword)
    {
        var cleanDir = CleanName(dirName);
        var cleanKw = CleanName(keyword);

        return cleanDir.Equals(cleanKw, StringComparison.OrdinalIgnoreCase) ||
               cleanDir.StartsWith(cleanKw, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanName(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static List<string> GenerateSearchKeywords(SteamGame game)
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(game.Name))
            list.Add(game.Name);

        if (!string.IsNullOrWhiteSpace(game.InstallDir))
            list.Add(game.InstallDir);

        // Remove subtítulos após ":" ou "-"
        if (game.Name.Contains(':'))
            list.Add(game.Name.Split(':')[0].Trim());

        if (game.Name.Contains('-'))
            list.Add(game.Name.Split('-')[0].Trim());

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<SaveFileInfo> ScanDirectoryFiles(string directoryPath, int maxFiles = 2000)
    {
        var result = new List<SaveFileInfo>();
        if (!Directory.Exists(directoryPath)) return result;

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            var files = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories);

            foreach (var f in files)
            {
                // Pular arquivos maiores que 500MB (provavelmente não são saves)
                if (f.Length > 500 * 1024 * 1024) continue;

                var relative = Path.GetRelativePath(directoryPath, f.FullName);

                result.Add(new SaveFileInfo
                {
                    FullPath = f.FullName,
                    RelativePath = relative,
                    SizeBytes = f.Length,
                    LastWriteTimeUtc = f.LastWriteTimeUtc
                });

                if (result.Count >= maxFiles) break;
            }
        }
        catch
        {
            // Erro ao enumerar arquivos
        }

        return result;
    }
}
