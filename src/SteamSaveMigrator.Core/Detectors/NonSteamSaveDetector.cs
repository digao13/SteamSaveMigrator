using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Detectors;

public record NonSteamSearchLocation(
    string Path,
    string CrackSource,
    string Description
);

/// <summary>
/// Detector especializado em identificar saves de jogos non-Steam e correções conhecidas
/// (CODEX, PLAZA, CPY, HOODLUM, Rune, Voices38, Goldberg, EMPRESS, Skidrow, FLT, etc.).
/// </summary>
public static class NonSteamSaveDetector
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private static readonly ConcurrentDictionary<uint, string> AppNameCache = new();
    private static string? _cacheFilePath;

    static NonSteamSaveDetector()
    {
        InitializePopularAppIds();
        LoadLocalAppCache();
    }

    /// <summary>
    /// Retorna as pastas comumente utilizadas por correções de jogos Steam no Windows.
    /// </summary>
    public static List<NonSteamSearchLocation> GetKnownSearchLocations(string? userProfilePath = null)
    {
        var userProfile = !string.IsNullOrWhiteSpace(userProfilePath)
            ? userProfilePath
            : WindowsKnownFolders.GetCurrentProfile();

        var folders = WindowsKnownFolders.GetFoldersForUserProfile(userProfile);
        var commonDocs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        if (string.IsNullOrWhiteSpace(commonDocs))
        {
            commonDocs = @"C:\Users\Public\Documents";
        }

        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonAppData))
        {
            commonAppData = @"C:\ProgramData";
        }

        var list = new List<NonSteamSearchLocation>();

        void AddIfPossible(string path, string source, string desc)
        {
            if (!string.IsNullOrWhiteSpace(path) && !list.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new NonSteamSearchLocation(Path.GetFullPath(path), source, desc));
            }
        }

        // =========================================================================
        // 1. CODEX
        // =========================================================================
        AddIfPossible(Path.Combine(commonDocs, "Steam", "CODEX"), "CODEX", "Saves CODEX (Público)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Steam", "CODEX"), "CODEX", "Saves CODEX (Roaming)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "CODEX"), "CODEX", "Saves CODEX (Local)");

        // =========================================================================
        // 2. PLAZA
        // =========================================================================
        AddIfPossible(Path.Combine(commonDocs, "Steam", "PLAZA"), "PLAZA", "Saves PLAZA (Público)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Steam", "PLAZA"), "PLAZA", "Saves PLAZA (Roaming)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "PLAZA"), "PLAZA", "Saves PLAZA (Local)");

        // =========================================================================
        // 3. RUNE
        // =========================================================================
        AddIfPossible(Path.Combine(commonDocs, "Steam", "RUNE"), "Rune", "Saves Rune (Público)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Steam", "RUNE"), "Rune", "Saves Rune (Roaming)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "Rune"), "Rune", "Saves Rune (Local)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "RUNE"), "Rune", "Saves Rune (Local)");

        // =========================================================================
        // 4. CPY (CONSPIR4CY)
        // =========================================================================
        AddIfPossible(Path.Combine(folders.Documents, "CPY_SAVES"), "CPY", "Saves CPY (Documentos do Usuário)");
        AddIfPossible(Path.Combine(commonDocs, "CPY_SAVES"), "CPY", "Saves CPY (Documentos Públicos)");

        // =========================================================================
        // 5. HOODLUM
        // =========================================================================
        AddIfPossible(Path.Combine(folders.AppDataLocal, "HOODLUM"), "HOODLUM", "Saves HOODLUM (Local)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "HOODLUM"), "HOODLUM", "Saves HOODLUM (Roaming)");
        AddIfPossible(Path.Combine(folders.Documents, "HOODLUM"), "HOODLUM", "Saves HOODLUM (Documentos)");

        // =========================================================================
        // 6. Voices38 (e variações de emuladores Voksi / Revolt / Voices38)
        // =========================================================================
        AddIfPossible(Path.Combine(commonDocs, "Steam", "Voices38"), "Voices38", "Saves Voices38 (Público)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Steam", "Voices38"), "Voices38", "Saves Voices38 (Roaming)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "Steam", "Voices38"), "Voices38", "Saves Voices38 (Local Steam)");
        AddIfPossible(Path.Combine(folders.AppDataLocal, "Voices38"), "Voices38", "Saves Voices38 (Local)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Voices38"), "Voices38", "Saves Voices38 (Roaming direto)");
        AddIfPossible(Path.Combine(commonAppData, "Steam", "Voices38"), "Voices38", "Saves Voices38 (ProgramData)");

        // =========================================================================
        // 7. Goldberg SteamEmu
        // =========================================================================
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Goldberg SteamEmu Saves"), "Goldberg", "Saves Goldberg Emulator");

        // =========================================================================
        // 8. EMPRESS
        // =========================================================================
        AddIfPossible(Path.Combine(commonDocs, "EMPRESS"), "EMPRESS", "Saves EMPRESS (Público)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "EMPRESS"), "EMPRESS", "Saves EMPRESS (Roaming)");

        // =========================================================================
        // 9. SKIDROW
        // =========================================================================
        AddIfPossible(Path.Combine(folders.AppDataLocal, "SKIDROW"), "SKIDROW", "Saves SKIDROW (Local)");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "SKIDROW"), "SKIDROW", "Saves SKIDROW (Roaming)");

        // =========================================================================
        // 10. Fairlight (FLT)
        // =========================================================================
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "Fairlight"), "Fairlight", "Saves Fairlight FLT");
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "FLT"), "Fairlight", "Saves FLT");

        // =========================================================================
        // 11. SmartSteamEmu
        // =========================================================================
        AddIfPossible(Path.Combine(folders.AppDataRoaming, "SmartSteamEmu"), "SmartSteamEmu", "Saves SmartSteamEmu");

        // =========================================================================
        // 12. Varreduras dinâmicas em pastas genéricas Steam (ex: C:\Users\Public\Documents\Steam\*)
        // =========================================================================
        TryAddDynamicSteamSubfolders(Path.Combine(commonDocs, "Steam"), "Público", list);
        TryAddDynamicSteamSubfolders(Path.Combine(folders.AppDataRoaming, "Steam"), "Roaming", list);
        TryAddDynamicSteamSubfolders(Path.Combine(folders.AppDataLocal, "Steam"), "Local", list);
        TryAddDynamicSteamSubfolders(Path.Combine(commonAppData, "Steam"), "ProgramData", list);

        return list;
    }

    private static void TryAddDynamicSteamSubfolders(string steamParentDir, string tag, List<NonSteamSearchLocation> list)
    {
        if (!Directory.Exists(steamParentDir)) return;

        try
        {
            var subDirs = Directory.GetDirectories(steamParentDir);
            foreach (var sub in subDirs)
            {
                var folderName = Path.GetFileName(sub);
                if (string.IsNullOrWhiteSpace(folderName)) continue;

                // Ignora pastas internas óbvias da própria Valve/Steam
                if (folderName.Equals("games", StringComparison.OrdinalIgnoreCase) ||
                    folderName.Equals("music", StringComparison.OrdinalIgnoreCase) ||
                    folderName.Equals("htmlcache", StringComparison.OrdinalIgnoreCase) ||
                    folderName.Equals("widevine", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!list.Any(x => x.Path.Equals(sub, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new NonSteamSearchLocation(
                        Path.GetFullPath(sub),
                        folderName,
                        $"Saves em {folderName} ({tag})"
                    ));
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Realiza a varredura em todas as pastas conhecidas e personalizadas para identificar jogos com saves.
    /// </summary>
    public static async Task<List<NonSteamGameSave>> DetectAllAsync(
        IEnumerable<string>? customFolders = null,
        IReadOnlyDictionary<uint, string>? localGamesDictionary = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<NonSteamGameSave>();
        var seenSavePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var targetLocations = GetKnownSearchLocations();

        // Adiciona pastas personalizadas solicitadas pelo usuário com detecção inteligente de correção
        if (customFolders != null)
        {
            foreach (var custom in customFolders)
            {
                if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                {
                    var fullCustom = Path.GetFullPath(custom);
                    if (!targetLocations.Any(x => x.Path.Equals(fullCustom, StringComparison.OrdinalIgnoreCase)))
                    {
                        var dirName = Path.GetFileName(fullCustom.TrimEnd('\\', '/'));
                        var detectedCrack = "Personalizado";

                        if (dirName.Contains("CODEX", StringComparison.OrdinalIgnoreCase)) detectedCrack = "CODEX";
                        else if (dirName.Contains("RUNE", StringComparison.OrdinalIgnoreCase)) detectedCrack = "Rune";
                        else if (dirName.Contains("PLAZA", StringComparison.OrdinalIgnoreCase)) detectedCrack = "PLAZA";
                        else if (dirName.Contains("CPY", StringComparison.OrdinalIgnoreCase)) detectedCrack = "CPY";
                        else if (dirName.Contains("HOODLUM", StringComparison.OrdinalIgnoreCase)) detectedCrack = "HOODLUM";
                        else if (dirName.Contains("Voices38", StringComparison.OrdinalIgnoreCase)) detectedCrack = "Voices38";
                        else if (dirName.Contains("Goldberg", StringComparison.OrdinalIgnoreCase)) detectedCrack = "Goldberg";
                        else if (dirName.Contains("EMPRESS", StringComparison.OrdinalIgnoreCase)) detectedCrack = "EMPRESS";
                        else if (dirName.Contains("Fairlight", StringComparison.OrdinalIgnoreCase) || dirName.Contains("FLT", StringComparison.OrdinalIgnoreCase)) detectedCrack = "Fairlight";
                        else if (dirName.Contains("SKIDROW", StringComparison.OrdinalIgnoreCase)) detectedCrack = "SKIDROW";

                        targetLocations.Add(new NonSteamSearchLocation(fullCustom, detectedCrack, $"Pasta Personalizada ({dirName})"));
                    }
                }
            }
        }

        foreach (var loc in targetLocations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!Directory.Exists(loc.Path)) continue;

            progress?.Report($"Verificando {loc.CrackSource} em {Path.GetFileName(loc.Path)}...");

            try
            {
                var subDirs = Directory.GetDirectories(loc.Path);

                // Se a pasta em si já contiver arquivos de save diretamente
                var selfFiles = SaveLocationDetector.ScanDirectoryFiles(loc.Path, 10);
                if (selfFiles.Count > 0 && subDirs.Length == 0)
                {
                    await ProcessGameFolderAsync(loc.Path, loc.CrackSource, seenSavePaths, results, localGamesDictionary, cancellationToken);
                }

                foreach (var gameOrAppDir in subDirs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var folderName = Path.GetFileName(gameOrAppDir);
                    if (string.IsNullOrWhiteSpace(folderName)) continue;

                    // Caso especial: subpastas intermediárias como "Player", "Profile", "Default" que contêm os AppIDs/jogos
                    if (folderName.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("Profile", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var playerDirs = Directory.GetDirectories(gameOrAppDir);
                            foreach (var pDir in playerDirs)
                            {
                                await ProcessGameFolderAsync(pDir, loc.CrackSource, seenSavePaths, results, localGamesDictionary, cancellationToken);
                            }
                        }
                        catch { }
                        continue;
                    }

                    await ProcessGameFolderAsync(gameOrAppDir, loc.CrackSource, seenSavePaths, results, localGamesDictionary, cancellationToken);
                }
            }
            catch { }
        }

        // Ordena por nome do jogo para apresentação visual limpa
        return results.OrderBy(r => r.GameName).ThenBy(r => r.CrackSource).ToList();
    }

    private static async Task ProcessGameFolderAsync(
        string gameDirPath,
        string crackSource,
        HashSet<string> seenSavePaths,
        List<NonSteamGameSave> results,
        IReadOnlyDictionary<uint, string>? localGamesDictionary,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(gameDirPath);
        if (seenSavePaths.Contains(fullPath)) return;

        // Escaneia os arquivos do diretório
        var files = SaveLocationDetector.ScanDirectoryFiles(fullPath);
        if (files.Count == 0) return;

        seenSavePaths.Add(fullPath);

        var folderName = Path.GetFileName(fullPath);
        uint detectedAppId = 0;
        string? resolvedName = null;

        // 1. Tenta identificar se o nome da pasta é um AppId numérico (ex: 1245620)
        if (uint.TryParse(folderName, out var parsedId))
        {
            detectedAppId = parsedId;
        }
        else
        {
            // Tenta extrair AppId de arquivos internos (.ini, txt, etc.)
            detectedAppId = TryExtractAppIdFromFiles(fullPath, files);
        }

        // 2. Tenta extrair nome do jogo a partir de configurações locais (.ini, .nfo)
        resolvedName = TryExtractGameNameFromFiles(fullPath, files);

        // 3. Tenta resolver via dicionário local de jogos instalados na Steam do usuário
        if (string.IsNullOrWhiteSpace(resolvedName) && detectedAppId > 0 && localGamesDictionary != null)
        {
            if (localGamesDictionary.TryGetValue(detectedAppId, out var localName))
            {
                resolvedName = localName;
            }
        }

        // 4. Tenta resolver via catálogo estático de AppIds populares
        if (string.IsNullOrWhiteSpace(resolvedName) && detectedAppId > 0)
        {
            if (AppNameCache.TryGetValue(detectedAppId, out var cachedName))
            {
                resolvedName = cachedName;
            }
        }

        // 5. Se ainda não achou nome mas temos um AppId válido, tenta consulta rápida na API online da Steam
        if (string.IsNullOrWhiteSpace(resolvedName) && detectedAppId > 0)
        {
            resolvedName = await TryFetchGameNameOnlineAsync(detectedAppId, cancellationToken);
        }

        // 6. Fallback final para nome
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            if (detectedAppId > 0)
            {
                resolvedName = $"Jogo Steam (AppID {detectedAppId})";
            }
            else
            {
                resolvedName = folderName;
            }
        }

        DateTime? lastModified = null;
        if (files.Count > 0)
        {
            lastModified = files.Max(f => f.LastWriteTimeUtc);
        }

        results.Add(new NonSteamGameSave
        {
            AppId = detectedAppId,
            GameName = resolvedName,
            CrackSource = crackSource,
            SavePath = fullPath,
            FileCount = files.Count,
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            LastModified = lastModified,
            Files = files,
            Notes = $"Localizado em {fullPath}",
            IsSelected = true
        });
    }

    private static uint TryExtractAppIdFromFiles(string dirPath, List<SaveFileInfo> files)
    {
        // Procura por arquivos comuns que contêm AppID
        var candidateFiles = files.Where(f =>
            f.RelativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith("appid.txt", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith("steam_appid.txt", StringComparison.OrdinalIgnoreCase)
        ).Take(5);

        foreach (var candidate in candidateFiles)
        {
            try
            {
                if (File.Exists(candidate.FullPath))
                {
                    var content = File.ReadAllText(candidate.FullPath);

                    // Verifica padrão AppId = 123456 ou AppId=123456
                    var match = Regex.Match(content, @"(?:AppId|App_Id|AppID|GameID)\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && uint.TryParse(match.Groups[1].Value, out var id))
                    {
                        return id;
                    }

                    // Se for um arquivo txt com apenas dígitos
                    var trimmed = content.Trim();
                    if (uint.TryParse(trimmed, out var txtId))
                    {
                        return txtId;
                    }
                }
            }
            catch { }
        }

        return 0;
    }

    private static string? TryExtractGameNameFromFiles(string dirPath, List<SaveFileInfo> files)
    {
        var iniFiles = files.Where(f =>
            f.RelativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith("game_name.txt", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith("app_name.txt", StringComparison.OrdinalIgnoreCase)
        ).Take(5);

        foreach (var ini in iniFiles)
        {
            try
            {
                if (File.Exists(ini.FullPath))
                {
                    var lines = File.ReadAllLines(ini.FullPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        var match = Regex.Match(trimmed, @"^(?:GameName|AppName|Title|GameTitle)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value.Trim('"', '\'', ' ');
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                return name;
                            }
                        }
                    }

                    // Se for app_name.txt
                    if (ini.RelativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var singleLine = File.ReadAllText(ini.FullPath).Trim();
                        if (singleLine.Length > 2 && singleLine.Length < 100 && !singleLine.Contains('\n'))
                        {
                            return singleLine;
                        }
                    }
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Consulta assíncrona leve à API pública da Steam para resolver nome de AppIds desconhecidos.
    /// </summary>
    private static async Task<string?> TryFetchGameNameOnlineAsync(uint appId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
            using var response = await HttpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(appId.ToString(), out var appProp) &&
                appProp.TryGetProperty("success", out var successProp) &&
                successProp.GetBoolean() &&
                appProp.TryGetProperty("data", out var dataProp) &&
                dataProp.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AppNameCache[appId] = name;
                    SaveLocalAppCache();
                    return name;
                }
            }
        }
        catch { }

        return null;
    }

    private static void LoadLocalAppCache()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cacheFilePath = Path.Combine(localAppData, "SteamSaveMigrator", "nonsteam_apps_cache.json");

            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var (key, value) in dict)
                    {
                        if (uint.TryParse(key, out var id) && !string.IsNullOrWhiteSpace(value))
                        {
                            AppNameCache[id] = value;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void SaveLocalAppCache()
    {
        if (string.IsNullOrWhiteSpace(_cacheFilePath)) return;

        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dict = AppNameCache.ToDictionary(k => k.Key.ToString(), v => v.Value);
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch { }
    }

    /// <summary>
    /// Catálogo offline estático com centenas dos jogos Steam mais populares e seus respectivos AppIds.
    /// Garante reconhecimento instantâneo mesmo sem conexão à internet.
    /// </summary>
    private static void InitializePopularAppIds()
    {
        var popular = new (uint Id, string Name)[]
        {
            (1245620, "ELDEN RING"),
            (1091500, "Cyberpunk 2077"),
            (1086940, "Baldur's Gate 3"),
            (2358720, "Black Myth: Wukong"),
            (990080, "Hogwarts Legacy"),
            (1174180, "Red Dead Redemption 2"),
            (271590, "Grand Theft Auto V"),
            (292030, "The Witcher 3: Wild Hunt"),
            (1593500, "God of War"),
            (1817070, "Marvel’s Spider-Man Remastered"),
            (1817190, "Marvel’s Spider-Man: Miles Morales"),
            (1151640, "Horizon Zero Dawn Complete Edition"),
            (2420110, "Horizon Forbidden West Complete Edition"),
            (2215430, "Ghost of Tsushima DIRECTOR'S CUT"),
            (814380, "Sekiro: Shadows Die Twice"),
            (374320, "DARK SOULS III"),
            (335300, "DARK SOULS II: Scholar of the First Sin"),
            (211420, "DARK SOULS: Prepare to Die Edition"),
            (570940, "DARK SOULS: REMASTERED"),
            (1627720, "Lies of P"),
            (1446780, "MONSTER HUNTER RISE"),
            (582010, "Monster Hunter: World"),
            (2050650, "Resident Evil 4"),
            (1196590, "Resident Evil Village"),
            (883710, "Resident Evil 2"),
            (952060, "Resident Evil 3"),
            (418370, "Resident Evil 7 Biohazard"),
            (1623730, "Palworld"),
            (553850, "HELLDIVERS 2"),
            (1716740, "Starfield"),
            (489830, "The Elder Scrolls V: Skyrim Special Edition"),
            (72850, "The Elder Scrolls V: Skyrim"),
            (377160, "Fallout 4"),
            (782330, "DOOM Eternal"),
            (379720, "DOOM (2016)"),
            (1462040, "FINAL FANTASY VII REMAKE INTERGRADE"),
            (2054970, "Dragon's Dogma 2"),
            (1551360, "Forza Horizon 5"),
            (1293830, "Forza Horizon 4"),
            (1145360, "Hades"),
            (1145350, "Hades II"),
            (367520, "Hollow Knight"),
            (892970, "Valheim"),
            (1966720, "Lethal Company"),
            (413150, "Stardew Valley"),
            (105600, "Terraria"),
            (646570, "Slay the Spire"),
            (588650, "Dead Cells"),
            (1868140, "DAVE THE DIVER"),
            (2161700, "Persona 3 Reload"),
            (1687950, "Persona 5 Royal"),
            (1113000, "Persona 4 Golden"),
            (2124490, "Silent Hill 2"),
            (1364780, "Street Fighter 6"),
            (1778820, "TEKKEN 8"),
            (2379780, "Balatro"),
            (1794680, "Vampire Survivors"),
            (2054790, "Like a Dragon: Infinite Wealth"),
            (1637320, "Like a Dragon: Ishin!"),
            (1172470, "Apex Legends"),
            (570, "Dota 2"),
            (730, "Counter-Strike 2"),
            (440, "Team Fortress 2"),
            (108600, "Project Zomboid"),
            (252490, "Rust"),
            (381210, "Dead by Daylight"),
            (230410, "Warframe"),
            (107410, "Arma 3"),
            (281990, "Stellaris"),
            (289070, "Sid Meier's Civilization VI"),
            (236850, "Europa Universalis IV"),
            (8500, "EVE Online"),
            (255710, "Cities: Skylines"),
            (949230, "Cities: Skylines II"),
            (1237970, "Titanfall 2"),
            (1222670, "The Sims 4"),
            (22370, "Fallout: New Vegas"),
            (22380, "Fallout 3"),
            (207610, "The Walking Dead"),
            (391540, "Undertale"),
            (268910, "Cuphead"),
            (632360, "Risk of Rain 2"),
            (250900, "The Binding of Isaac: Rebirth"),
            (322330, "Don't Starve Together"),
            (242760, "The Forest"),
            (1326470, "Sons Of The Forest"),
            (534380, "Dying Light 2 Stay Human"),
            (239140, "Dying Light"),
            (686810, "Hellblade: Senua's Sacrifice"),
            (1354470, "Senua's Saga: Hellblade II"),
            (2195250, "EA SPORTS FC 24"),
            (2456740, "EA SPORTS FC 25"),
            (1517290, "Battlefield 2042"),
            (1238810, "Battlefield V"),
            (1238840, "Battlefield 1"),
            (1938090, "Call of Duty"),
            (2073850, "THE FINALS"),
            (1172620, "Sea of Thieves"),
            (847370, "Sunset Overdrive"),
            (1240440, "Halo Infinite"),
            (976730, "Halo: The Master Chief Collection")
        };

        foreach (var item in popular)
        {
            AppNameCache[item.Id] = item.Name;
        }
    }
}
