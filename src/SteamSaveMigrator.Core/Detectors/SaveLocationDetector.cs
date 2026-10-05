using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Parsers;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Detectors;

/// <summary>
/// Detector inteligente de locais de save para jogos Steam no Windows.
/// Combina Steam AutoCloud (remotecache.vdf), heurística por engine (Unity, Unreal, Godot),
/// banco de dados de jogos conhecidos e varredura recursiva de diretórios de desenvolvedor.
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

        void TryAddDirectoryLocation(string path, SaveLocationType type, string displayName)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            var fullPath = Path.GetFullPath(path);

            // Se o caminho já foi adicionado ou se já adicionamos uma pasta pai que o engloba, ignora
            if (seenPaths.Any(p => fullPath.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                                  fullPath.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var files = ScanDirectoryFiles(fullPath);
            if (files.Count > 0 && seenPaths.Add(fullPath))
            {
                var tokenized = PathVariableConverter.Tokenize(fullPath, userProfile, steamInfo.SteamPath);
                locations.Add(new GameSaveLocation
                {
                    LocationType = type,
                    DisplayName = displayName,
                    SourcePath = fullPath,
                    TokenizedPath = tokenized,
                    Exists = true,
                    FileCount = files.Count,
                    TotalSizeBytes = files.Sum(f => f.SizeBytes),
                    Files = files
                });
            }
        }

        void TryAddFileLocation(string filePath, SaveLocationType type, string displayName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            var fullPath = Path.GetFullPath(filePath);

            var parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parentDir) &&
                seenPaths.Any(p => fullPath.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                  fullPath.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (seenPaths.Add(fullPath))
            {
                var tokenized = PathVariableConverter.Tokenize(fullPath, userProfile, steamInfo.SteamPath);
                locations.Add(new GameSaveLocation
                {
                    LocationType = type,
                    DisplayName = displayName,
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

        // 1. Steam AutoCloud (remotecache.vdf)
        // Detecta o mapeamento oficial da Steam Cloud para caminhos locais (AppData, LocalLow, Documents, etc.)
        DetectSteamAutoCloud(game, steamInfo, folders, TryAddDirectoryLocation, TryAddFileLocation);

        // 2. Engine Detectors: Unity, Unreal Engine e Godot
        DetectGameEngines(game, folders, TryAddDirectoryLocation);

        // 3. Steam Userdata (Saves sincronizados na pasta de userdata da Steam)
        DetectSteamUserdata(game, steamInfo, userProfile, seenPaths, locations);

        // 4. Base de Dados Conhecida (KnownGameDatabase)
        DetectKnownGames(game, steamInfo, userProfile, folders, seenPaths, locations);

        // 5. Scanner Heurístico Abrangente de 2 Níveis (Direto e Subpastas de Desenvolvedores)
        DetectHeuristic(game, folders, TryAddDirectoryLocation);

        // 6. Pastas de Saves dentro da Instalação do Jogo
        DetectInstallDirSaves(game, TryAddDirectoryLocation);

        return locations;
    }

    private static void DetectSteamAutoCloud(
        SteamGame game,
        SteamInstallationInfo steamInfo,
        UserProfileFolders folders,
        Action<string, SaveLocationType, string> addDir,
        Action<string, SaveLocationType, string> addFile)
    {
        if (!steamInfo.IsFound || string.IsNullOrWhiteSpace(steamInfo.SteamPath)) return;

        var userdataDir = Path.Combine(steamInfo.SteamPath, "userdata");
        if (!Directory.Exists(userdataDir)) return;

        try
        {
            var accountDirs = Directory.GetDirectories(userdataDir);
            foreach (var accDir in accountDirs)
            {
                var remoteCacheFile = Path.Combine(accDir, game.AppId.ToString(), "remotecache.vdf");
                if (!File.Exists(remoteCacheFile)) continue;

                try
                {
                    var vdf = VdfParser.ParseFile(remoteCacheFile);
                    var appNode = vdf[game.AppId.ToString()] ?? vdf;

                    foreach (var child in appNode.Children)
                    {
                        var relPath = child.Name;
                        if (string.IsNullOrWhiteSpace(relPath) || relPath.Equals("ChangeNumber", StringComparison.OrdinalIgnoreCase) || relPath.Equals("OSType", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var rootVal = child.GetString("root") ?? child.GetInt32("root").ToString();

                        var (targetBase, locType) = rootVal switch
                        {
                            "0" => (game.InstallPath, SaveLocationType.Custom),
                            "1" => (folders.Documents, SaveLocationType.Documents),
                            "2" => (folders.AppDataRoaming, SaveLocationType.AppDataRoaming),
                            "3" => (Path.Combine(accDir, game.AppId.ToString()), SaveLocationType.SteamUserdata),
                            "4" => (folders.SavedGames, SaveLocationType.SavedGames),
                            "11" => (folders.AppDataLocal, SaveLocationType.AppDataLocal),
                            "12" => (folders.AppDataLocalLow, SaveLocationType.AppDataLocalLow),
                            _ => (folders.UserProfile, SaveLocationType.Custom)
                        };

                        if (string.IsNullOrWhiteSpace(targetBase) || !Directory.Exists(targetBase)) continue;

                        var fullFilePath = Path.Combine(targetBase, relPath.Replace('/', '\\'));
                        if (File.Exists(fullFilePath))
                        {
                            // Encontra a melhor pasta raiz do save para incluir arquivos adjacentes (outros slots, logs, backups)
                            var parentDir = Path.GetDirectoryName(fullFilePath);
                            if (!string.IsNullOrWhiteSpace(parentDir))
                            {
                                var parentName = Path.GetFileName(parentDir);
                                // Se a pasta pai imediata for um ID Steam (ex: 76561198041671480 ou 81405752), pega a pasta do jogo acima
                                if (parentName.Length >= 7 && (parentName.StartsWith("7656119") || uint.TryParse(parentName, out _)))
                                {
                                    var grandParent = Path.GetDirectoryName(parentDir);
                                    if (!string.IsNullOrWhiteSpace(grandParent) && Directory.Exists(grandParent))
                                    {
                                        parentDir = grandParent;
                                    }
                                }

                                addDir(parentDir, locType, $"Steam Cloud ({Path.GetFileName(parentDir)})");
                            }
                            else
                            {
                                addFile(fullFilePath, locType, $"Steam Cloud ({Path.GetFileName(fullFilePath)})");
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DetectGameEngines(
        SteamGame game,
        UserProfileFolders folders,
        Action<string, SaveLocationType, string> addDir)
    {
        // 1. Unity Engine: procura app.info (Company / Product)
        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                var appInfoFiles = Directory.GetFiles(game.InstallPath, "app.info", SearchOption.AllDirectories);
                foreach (var appInfo in appInfoFiles)
                {
                    var lines = File.ReadAllLines(appInfo).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                    if (lines.Length >= 2)
                    {
                        var company = lines[0].Trim();
                        var product = lines[1].Trim();

                        addDir(Path.Combine(folders.AppDataLocalLow, company, product), SaveLocationType.AppDataLocalLow, $"Unity ({product})");
                        addDir(Path.Combine(folders.AppDataLocal, company, product), SaveLocationType.AppDataLocal, $"Unity ({product})");
                        addDir(Path.Combine(folders.AppDataRoaming, company, product), SaveLocationType.AppDataRoaming, $"Unity ({product})");
                    }
                }
            }
            catch { }
        }

        // 2. Unreal Engine: Saved\SaveGames
        var keywords = GenerateSearchKeywords(game);
        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw) || kw.Length < 3) continue;

            var uePath1 = Path.Combine(folders.AppDataLocal, kw, "Saved", "SaveGames");
            if (Directory.Exists(uePath1))
            {
                addDir(uePath1, SaveLocationType.AppDataLocal, $"Unreal Engine ({kw})");
            }

            var uePathRoot = Path.Combine(folders.AppDataLocal, kw, "Saved");
            if (Directory.Exists(uePathRoot))
            {
                addDir(uePathRoot, SaveLocationType.AppDataLocal, $"Unreal Engine ({kw})");
            }

            if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
            {
                var ueInternal = Path.Combine(game.InstallPath, kw, "Saved", "SaveGames");
                if (Directory.Exists(ueInternal))
                {
                    addDir(ueInternal, SaveLocationType.Custom, $"Unreal Engine ({kw})");
                }
            }
        }

        // 3. Godot Engine: AppData\Roaming\Godot\app_userdata\<GameName>
        var godotBase = Path.Combine(folders.AppDataRoaming, "Godot", "app_userdata");
        if (Directory.Exists(godotBase))
        {
            foreach (var kw in keywords)
            {
                var godotPath = Path.Combine(godotBase, kw);
                if (Directory.Exists(godotPath))
                {
                    addDir(godotPath, SaveLocationType.AppDataRoaming, $"Godot ({kw})");
                }
            }
        }
    }

    private static void DetectSteamUserdata(
        SteamGame game,
        SteamInstallationInfo steamInfo,
        string userProfile,
        HashSet<string> seenPaths,
        List<GameSaveLocation> locations)
    {
        if (!steamInfo.IsFound || string.IsNullOrWhiteSpace(steamInfo.SteamPath)) return;

        var userdataDir = Path.Combine(steamInfo.SteamPath, "userdata");
        if (!Directory.Exists(userdataDir)) return;

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

    private static void DetectKnownGames(
        SteamGame game,
        SteamInstallationInfo steamInfo,
        string userProfile,
        UserProfileFolders folders,
        HashSet<string> seenPaths,
        List<GameSaveLocation> locations)
    {
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
    }

    private static void DetectHeuristic(
        SteamGame game,
        UserProfileFolders folders,
        Action<string, SaveLocationType, string> addDir)
    {
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

            try
            {
                var subDirs = Directory.GetDirectories(dir);
                foreach (var subDir in subDirs)
                {
                    var dirName = Path.GetFileName(subDir);

                    // Nível 1: Pasta com o nome do jogo
                    foreach (var kw in searchKeywords)
                    {
                        if (string.IsNullOrWhiteSpace(kw) || kw.Length < 3) continue;

                        if (IsMatch(dirName, kw))
                        {
                            addDir(subDir, type, $"{dirDisplayName} ({dirName})");
                        }
                    }

                    // Nível 2: Subpasta dentro de pasta de desenvolvedor/publisher (ex: Studio Doodal\Solateria, FromSoftware\EldenRing)
                    try
                    {
                        var childDirs = Directory.GetDirectories(subDir);
                        foreach (var childDir in childDirs)
                        {
                            var childName = Path.GetFileName(childDir);
                            foreach (var kw in searchKeywords)
                            {
                                if (string.IsNullOrWhiteSpace(kw) || kw.Length < 3) continue;

                                if (IsMatch(childName, kw))
                                {
                                    addDir(childDir, type, $"{dirDisplayName} ({dirName}\\{childName})");
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void DetectInstallDirSaves(
        SteamGame game,
        Action<string, SaveLocationType, string> addDir)
    {
        if (string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath)) return;

        var candidates = new[] { "saves", "save", "SaveData", "savedata", "savegames", "UserData", "userdata", "www\\save" };
        foreach (var cand in candidates)
        {
            var p = Path.Combine(game.InstallPath, cand);
            if (Directory.Exists(p))
            {
                addDir(p, SaveLocationType.Custom, $"Pasta do Jogo ({cand})");
            }
        }
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
        catch { }

        return result;
    }
}
