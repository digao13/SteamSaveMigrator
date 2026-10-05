using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;

namespace SteamSaveMigrator.Core.Detectors;

/// <summary>
/// Detector universal de locais de saves baseado nas principais engines de jogos do mercado:
/// Unity, Unreal Engine (UE3/UE4/UE5), Godot (3/4), GameMaker Studio, RPG Maker (MV/MZ/XP/VX),
/// Ren'Py, Source/Source 2 (Valve), CryEngine, id Tech, Bethesda Creation Engine,
/// FNA/MonoGame/XNA, Electron/NW.js, BioWare e KiriKiri.
/// </summary>
public static class EngineSaveDetector
{
    public static void Detect(
        SteamGame game,
        UserProfileFolders folders,
        Action<string, SaveLocationType, string> addDir)
    {
        var keywords = GenerateKeywords(game);

        // 1. Unity Engine
        DetectUnity(game, folders, keywords, addDir);

        // 2. Unreal Engine (UE3, UE4, UE5)
        DetectUnrealEngine(game, folders, keywords, addDir);

        // 3. Godot Engine (Godot 3 e Godot 4)
        DetectGodot(game, folders, keywords, addDir);

        // 4. GameMaker (GameMaker: Studio, GMS 2, GM Legacy)
        DetectGameMaker(game, folders, keywords, addDir);

        // 5. RPG Maker (MV, MZ, XP, VX, VX Ace, 2000, 2003, Wolf RPG)
        DetectRpgMaker(game, folders, keywords, addDir);

        // 6. Ren'Py (Visual Novels & Jogos Narrativos)
        DetectRenPy(game, folders, keywords, addDir);

        // 7. Source & Source 2 Engine (Valve)
        DetectSourceEngine(game, addDir);

        // 8. CryEngine / Lumberyard / O3DE
        DetectCryEngine(game, folders, keywords, addDir);

        // 9. id Tech (id Software: Doom, Wolfenstein, Quake, Tango Gameworks)
        DetectIdTech(game, folders, keywords, addDir);

        // 10. Bethesda Creation Engine / Gamebryo (Skyrim, Fallout, Starfield)
        DetectCreationEngine(game, folders, keywords, addDir);

        // 11. FNA / MonoGame / XNA (Celeste, Stardew Valley, Terraria, etc.)
        DetectMonoGameAndFna(game, folders, keywords, addDir);

        // 12. Electron / NW.js / Desktop Web Games
        DetectElectronAndWeb(game, folders, keywords, addDir);

        // 13. BioWare Engine (Mass Effect, Dragon Age)
        DetectBioWare(folders, keywords, addDir);

        // 14. KiriKiri / TVP (Visual Novels Japonesas)
        DetectKiriKiri(game, folders, keywords, addDir);
    }

    #region 1. Unity Engine
    private static void DetectUnity(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var isUnity = false;
        var detectedCompany = string.Empty;
        var detectedProduct = string.Empty;

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                // Verifica UnityPlayer.dll ou pastas *_Data
                if (File.Exists(Path.Combine(game.InstallPath, "UnityPlayer.dll")))
                {
                    isUnity = true;
                }

                var dataDirs = Directory.GetDirectories(game.InstallPath, "*_Data", SearchOption.TopDirectoryOnly);
                if (dataDirs.Length > 0)
                {
                    isUnity = true;

                    foreach (var dataDir in dataDirs)
                    {
                        var appInfoPath = Path.Combine(dataDir, "app.info");
                        if (File.Exists(appInfoPath))
                        {
                            var lines = File.ReadAllLines(appInfoPath)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Select(l => l.Trim())
                                .ToArray();

                            if (lines.Length >= 2)
                            {
                                detectedCompany = lines[0];
                                detectedProduct = lines[1];

                                addDir(Path.Combine(folders.AppDataLocalLow, detectedCompany, detectedProduct),
                                    SaveLocationType.AppDataLocalLow, $"Unity ({detectedCompany}\\{detectedProduct})");

                                addDir(Path.Combine(folders.AppDataLocal, detectedCompany, detectedProduct),
                                    SaveLocationType.AppDataLocal, $"Unity Local ({detectedCompany}\\{detectedProduct})");

                                addDir(Path.Combine(folders.AppDataRoaming, detectedCompany, detectedProduct),
                                    SaveLocationType.AppDataRoaming, $"Unity Roaming ({detectedCompany}\\{detectedProduct})");
                            }
                        }

                        // Se não tem app.info, o nome da pasta *_Data sem "_Data" é o Product provável
                        if (string.IsNullOrWhiteSpace(detectedProduct))
                        {
                            var dirName = Path.GetFileName(dataDir);
                            if (dirName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                            {
                                detectedProduct = dirName.Substring(0, dirName.Length - 5);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Se identificou Unity ou se houver candidatos a produto,
        // busca em AppData\LocalLow por qualquer subpasta de estúdio que coincida com o produto ou keywords
        var productCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(detectedProduct)) productCandidates.Add(detectedProduct);
        productCandidates.AddRange(keywords);

        if (Directory.Exists(folders.AppDataLocalLow) && (isUnity || productCandidates.Count > 0))
        {
            try
            {
                var companyDirs = Directory.GetDirectories(folders.AppDataLocalLow);
                foreach (var compDir in companyDirs)
                {
                    var compName = Path.GetFileName(compDir);
                    try
                    {
                        var subDirs = Directory.GetDirectories(compDir);
                        foreach (var subDir in subDirs)
                        {
                            var prodName = Path.GetFileName(subDir);
                            foreach (var cand in productCandidates)
                            {
                                if (IsMatch(prodName, cand))
                                {
                                    addDir(subDir, SaveLocationType.AppDataLocalLow, $"Unity ({compName}\\{prodName})");
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
    #endregion

    #region 2. Unreal Engine
    private static void DetectUnrealEngine(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var ueProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                // Verifica *.uproject
                var uprojectFiles = Directory.GetFiles(game.InstallPath, "*.uproject", SearchOption.AllDirectories);
                foreach (var uproj in uprojectFiles)
                {
                    var projName = Path.GetFileNameWithoutExtension(uproj);
                    if (!string.IsNullOrWhiteSpace(projName))
                        ueProjectNames.Add(projName);
                }

                // Verifica estrutura típica UE: <InstallPath>\<ProjectName>\Binaries\Win64 e <InstallPath>\<ProjectName>\Content
                var subDirs = Directory.GetDirectories(game.InstallPath);
                foreach (var dir in subDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase)) continue;

                    var hasBinaries = Directory.Exists(Path.Combine(dir, "Binaries"));
                    var hasContent = Directory.Exists(Path.Combine(dir, "Content"));
                    if (hasBinaries || hasContent)
                    {
                        ueProjectNames.Add(name);

                        // Save dentro da pasta do jogo
                        var internalSave = Path.Combine(dir, "Saved", "SaveGames");
                        if (Directory.Exists(internalSave))
                        {
                            addDir(internalSave, SaveLocationType.Custom, $"Unreal Engine Interno ({name})");
                        }
                    }
                }

                // Procura executáveis com sufixo -Win64-Shipping.exe
                var exeFiles = Directory.GetFiles(game.InstallPath, "*-Win64-Shipping.exe", SearchOption.AllDirectories);
                foreach (var exe in exeFiles)
                {
                    var exeName = Path.GetFileNameWithoutExtension(exe);
                    var projName = exeName.Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(projName))
                        ueProjectNames.Add(projName);
                }
            }
            catch { }
        }

        // Adiciona keywords como candidatos para projetos UE
        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && kw.Length >= 3)
                ueProjectNames.Add(kw);
        }

        // Verifica AppData\Local\<ProjectName>\Saved\SaveGames e Saved\Config
        foreach (var proj in ueProjectNames)
        {
            var ueSaveGames = Path.Combine(folders.AppDataLocal, proj, "Saved", "SaveGames");
            if (Directory.Exists(ueSaveGames))
            {
                addDir(ueSaveGames, SaveLocationType.AppDataLocal, $"Unreal Engine ({proj})");
            }

            var ueSaved = Path.Combine(folders.AppDataLocal, proj, "Saved");
            if (Directory.Exists(ueSaved))
            {
                addDir(ueSaved, SaveLocationType.AppDataLocal, $"Unreal Engine ({proj})");
            }

            // Unreal Engine 3 / UDK (Documents\My Games\<ProjectName>)
            var myGamesPath = Path.Combine(folders.Documents, "My Games", proj);
            if (Directory.Exists(myGamesPath))
            {
                addDir(myGamesPath, SaveLocationType.Documents, $"Unreal Engine 3 ({proj})");
            }

            var myGamesGamePath = Path.Combine(folders.Documents, "My Games", proj + "Game");
            if (Directory.Exists(myGamesGamePath))
            {
                addDir(myGamesGamePath, SaveLocationType.Documents, $"Unreal Engine 3 ({proj}Game)");
            }
        }
    }
    #endregion

    #region 3. Godot Engine
    private static void DetectGodot(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var godotProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Procura arquivos *.pck na pasta de instalação
        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                var pckFiles = Directory.GetFiles(game.InstallPath, "*.pck", SearchOption.TopDirectoryOnly);
                foreach (var pck in pckFiles)
                {
                    var pckName = Path.GetFileNameWithoutExtension(pck);
                    if (!string.IsNullOrWhiteSpace(pckName) && !pckName.Equals("game", StringComparison.OrdinalIgnoreCase))
                    {
                        godotProjectNames.Add(pckName);
                    }
                }
            }
            catch { }
        }

        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && kw.Length >= 3)
                godotProjectNames.Add(kw);
        }

        // Godot salva em %APPDATA%\Godot\app_userdata\<ProjectName>
        var godotUserDataDir = Path.Combine(folders.AppDataRoaming, "Godot", "app_userdata");
        if (Directory.Exists(godotUserDataDir))
        {
            try
            {
                var existingDirs = Directory.GetDirectories(godotUserDataDir);
                foreach (var dir in existingDirs)
                {
                    var dirName = Path.GetFileName(dir);
                    foreach (var proj in godotProjectNames)
                    {
                        if (IsMatch(dirName, proj))
                        {
                            addDir(dir, SaveLocationType.AppDataRoaming, $"Godot ({dirName})");
                        }
                    }
                }
            }
            catch { }
        }

        // Godot 4 em certos builds pode usar %LOCALAPPDATA%\<ProjectName>
        foreach (var proj in godotProjectNames)
        {
            var localPath = Path.Combine(folders.AppDataLocal, proj);
            if (Directory.Exists(localPath))
            {
                // Se contiver saves ou configurações de Godot
                addDir(localPath, SaveLocationType.AppDataLocal, $"Godot Local ({proj})");
            }
        }
    }
    #endregion

    #region 4. GameMaker
    private static void DetectGameMaker(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var gmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                var optionsIni = Path.Combine(game.InstallPath, "options.ini");
                if (File.Exists(optionsIni))
                {
                    var lines = File.ReadAllLines(optionsIni);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("DisplayName=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("GameId=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = trimmed.Split('=', 2);
                            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                            {
                                gmNames.Add(parts[1].Trim('\"', ' '));
                            }
                        }
                    }
                }
            }
            catch { }
        }

        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && kw.Length >= 3)
                gmNames.Add(kw);
        }

        // GameMaker salva em %LOCALAPPDATA%\<GameName> ou %APPDATA%\<GameName>
        foreach (var name in gmNames)
        {
            var localPath = Path.Combine(folders.AppDataLocal, name);
            if (Directory.Exists(localPath))
            {
                addDir(localPath, SaveLocationType.AppDataLocal, $"GameMaker ({name})");
            }

            var roamPath = Path.Combine(folders.AppDataRoaming, name);
            if (Directory.Exists(roamPath))
            {
                addDir(roamPath, SaveLocationType.AppDataRoaming, $"GameMaker Roaming ({name})");
            }
        }
    }
    #endregion

    #region 5. RPG Maker & Wolf RPG
    private static void DetectRpgMaker(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var gameTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                // MV / MZ: www\save ou save
                var mvSave1 = Path.Combine(game.InstallPath, "www", "save");
                if (Directory.Exists(mvSave1))
                {
                    addDir(mvSave1, SaveLocationType.Custom, "RPG Maker MV/MZ (www\\save)");
                }

                var mvSave2 = Path.Combine(game.InstallPath, "save");
                if (Directory.Exists(mvSave2))
                {
                    addDir(mvSave2, SaveLocationType.Custom, "RPG Maker (save)");
                }

                // MV / MZ package.json ou data\System.json
                var packageJsonPaths = new[]
                {
                    Path.Combine(game.InstallPath, "www", "package.json"),
                    Path.Combine(game.InstallPath, "package.json")
                };

                foreach (var pkg in packageJsonPaths)
                {
                    if (File.Exists(pkg))
                    {
                        try
                        {
                            var content = File.ReadAllText(pkg);
                            using var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("name", out var nameProp))
                            {
                                var name = nameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(name)) gameTitles.Add(name);
                            }
                            if (doc.RootElement.TryGetProperty("window", out var winProp) &&
                                winProp.TryGetProperty("title", out var titleProp))
                            {
                                var title = titleProp.GetString();
                                if (!string.IsNullOrWhiteSpace(title)) gameTitles.Add(title);
                            }
                        }
                        catch { }
                    }
                }

                // XP / VX / VX Ace: Game.ini
                var gameIni = Path.Combine(game.InstallPath, "Game.ini");
                if (File.Exists(gameIni))
                {
                    var lines = File.ReadAllLines(gameIni);
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("Title=", StringComparison.OrdinalIgnoreCase))
                        {
                            var title = line.Substring(line.IndexOf('=') + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(title)) gameTitles.Add(title);
                        }
                    }
                }

                // Wolf RPG: Data.wolf
                var wolfSave = Path.Combine(game.InstallPath, "Save");
                if (Directory.Exists(wolfSave) && (File.Exists(Path.Combine(game.InstallPath, "Data.wolf")) || File.Exists(Path.Combine(game.InstallPath, "Game.dat"))))
                {
                    addDir(wolfSave, SaveLocationType.Custom, "Wolf RPG Editor (Save)");
                }
            }
            catch { }
        }

        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && kw.Length >= 3)
                gameTitles.Add(kw);
        }

        // RPG Maker XP/VX salva em Saved Games\<Title> ou Documents\<Title>
        foreach (var title in gameTitles)
        {
            var savedGamesTitle = Path.Combine(folders.SavedGames, title);
            if (Directory.Exists(savedGamesTitle))
            {
                addDir(savedGamesTitle, SaveLocationType.SavedGames, $"RPG Maker ({title})");
            }

            var docTitle = Path.Combine(folders.Documents, title);
            if (Directory.Exists(docTitle))
            {
                addDir(docTitle, SaveLocationType.Documents, $"RPG Maker Documents ({title})");
            }

            // MV/MZ no NW.js salva em AppData\Local\<Title>
            var localTitle = Path.Combine(folders.AppDataLocal, title);
            if (Directory.Exists(localTitle))
            {
                addDir(localTitle, SaveLocationType.AppDataLocal, $"RPG Maker Local ({title})");
            }
        }
    }
    #endregion

    #region 6. Ren'Py
    private static void DetectRenPy(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                // game\saves
                var gameSaves = Path.Combine(game.InstallPath, "game", "saves");
                if (Directory.Exists(gameSaves))
                {
                    addDir(gameSaves, SaveLocationType.Custom, "Ren'Py (game\\saves)");
                }
            }
            catch { }
        }

        // Ren'Py salva em %APPDATA%\RenPy\<SaveName-Hash>
        var renpyBase = Path.Combine(folders.AppDataRoaming, "RenPy");
        if (Directory.Exists(renpyBase))
        {
            try
            {
                var dirs = Directory.GetDirectories(renpyBase);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    foreach (var kw in keywords)
                    {
                        if (IsMatch(dirName, kw))
                        {
                            addDir(dir, SaveLocationType.AppDataRoaming, $"Ren'Py ({dirName})");
                        }
                    }
                }
            }
            catch { }
        }
    }
    #endregion

    #region 7. Source & Source 2
    private static void DetectSourceEngine(
        SteamGame game,
        Action<string, SaveLocationType, string> addDir)
    {
        if (string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath)) return;

        try
        {
            var subDirs = Directory.GetDirectories(game.InstallPath);
            foreach (var modDir in subDirs)
            {
                var gameInfo = Path.Combine(modDir, "gameinfo.txt");
                var gameInfoGi = Path.Combine(modDir, "gameinfo.gi");

                if (File.Exists(gameInfo) || File.Exists(gameInfoGi))
                {
                    var modName = Path.GetFileName(modDir);
                    var saveCandidates = new[] { "SAVE", "save", "saves" };
                    foreach (var sc in saveCandidates)
                    {
                        var savePath = Path.Combine(modDir, sc);
                        if (Directory.Exists(savePath))
                        {
                            addDir(savePath, SaveLocationType.Custom, $"Source Engine ({modName}\\{sc})");
                        }
                    }
                }
            }
        }
        catch { }
    }
    #endregion

    #region 8. CryEngine / Lumberyard / O3DE
    private static void DetectCryEngine(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            var userSaves = Path.Combine(game.InstallPath, "user", "savegames");
            if (Directory.Exists(userSaves))
            {
                addDir(userSaves, SaveLocationType.Custom, "CryEngine (user\\savegames)");
            }
        }

        // Saved Games\<GameName> e Saved Games\Cryengine\<GameName>
        var cryBase = Path.Combine(folders.SavedGames, "Cryengine");
        if (Directory.Exists(cryBase))
        {
            try
            {
                var dirs = Directory.GetDirectories(cryBase);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    foreach (var kw in keywords)
                    {
                        if (IsMatch(name, kw))
                        {
                            addDir(dir, SaveLocationType.SavedGames, $"CryEngine ({name})");
                        }
                    }
                }
            }
            catch { }
        }
    }
    #endregion

    #region 9. id Tech
    private static void DetectIdTech(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        // Saved Games\id Software\<GameName>
        var idBase = Path.Combine(folders.SavedGames, "id Software");
        if (Directory.Exists(idBase))
        {
            try
            {
                var dirs = Directory.GetDirectories(idBase);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    foreach (var kw in keywords)
                    {
                        if (IsMatch(name, kw))
                        {
                            addDir(dir, SaveLocationType.SavedGames, $"id Tech ({name})");
                        }
                    }
                }
            }
            catch { }
        }

        // Tango Gameworks (The Evil Within)
        var tangoBase = Path.Combine(folders.SavedGames, "TangoGameworks");
        if (Directory.Exists(tangoBase))
        {
            try
            {
                var dirs = Directory.GetDirectories(tangoBase);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    foreach (var kw in keywords)
                    {
                        if (IsMatch(name, kw))
                        {
                            addDir(dir, SaveLocationType.SavedGames, $"Tango Gameworks ({name})");
                        }
                    }
                }
            }
            catch { }
        }
    }
    #endregion

    #region 10. Bethesda Creation Engine
    private static void DetectCreationEngine(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var myGamesDir = Path.Combine(folders.Documents, "My Games");
        if (!Directory.Exists(myGamesDir)) return;

        try
        {
            var dirs = Directory.GetDirectories(myGamesDir);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                foreach (var kw in keywords)
                {
                    if (IsMatch(name, kw))
                    {
                        var savesSub = Path.Combine(dir, "Saves");
                        if (Directory.Exists(savesSub))
                        {
                            addDir(savesSub, SaveLocationType.Documents, $"Bethesda ({name}\\Saves)");
                        }
                        else
                        {
                            addDir(dir, SaveLocationType.Documents, $"My Games ({name})");
                        }
                    }
                }
            }
        }
        catch { }
    }
    #endregion

    #region 11. FNA / MonoGame / XNA
    private static void DetectMonoGameAndFna(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        // Padrão Stardew Valley, Celeste, Terraria, etc.
        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw) || kw.Length < 3) continue;

            // AppData\Roaming\<GameName>\Saves ou AppData\Roaming\<GameName>
            var roamSaves = Path.Combine(folders.AppDataRoaming, kw, "Saves");
            if (Directory.Exists(roamSaves))
            {
                addDir(roamSaves, SaveLocationType.AppDataRoaming, $"MonoGame/FNA ({kw}\\Saves)");
            }
            else
            {
                var roamDir = Path.Combine(folders.AppDataRoaming, kw);
                if (Directory.Exists(roamDir))
                {
                    addDir(roamDir, SaveLocationType.AppDataRoaming, $"MonoGame/FNA Roaming ({kw})");
                }
            }

            // Saved Games\<GameName>
            var savedGamesDir = Path.Combine(folders.SavedGames, kw);
            if (Directory.Exists(savedGamesDir))
            {
                addDir(savedGamesDir, SaveLocationType.SavedGames, $"MonoGame/FNA ({kw})");
            }
        }
    }
    #endregion

    #region 12. Electron / NW.js / Desktop Web
    private static void DetectElectronAndWeb(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var appNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                var pkgFile = Path.Combine(game.InstallPath, "resources", "app", "package.json");
                if (!File.Exists(pkgFile)) pkgFile = Path.Combine(game.InstallPath, "package.json");

                if (File.Exists(pkgFile))
                {
                    var content = File.ReadAllText(pkgFile);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        var n = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(n)) appNames.Add(n);
                    }
                    if (doc.RootElement.TryGetProperty("productName", out var prodProp))
                    {
                        var p = prodProp.GetString();
                        if (!string.IsNullOrWhiteSpace(p)) appNames.Add(p);
                    }
                }
            }
            catch { }
        }

        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && kw.Length >= 3)
                appNames.Add(kw);
        }

        foreach (var appName in appNames)
        {
            var roamPath = Path.Combine(folders.AppDataRoaming, appName);
            if (Directory.Exists(roamPath))
            {
                addDir(roamPath, SaveLocationType.AppDataRoaming, $"Electron/Web App ({appName})");
            }

            var localPath = Path.Combine(folders.AppDataLocal, appName);
            if (Directory.Exists(localPath))
            {
                addDir(localPath, SaveLocationType.AppDataLocal, $"Electron/Web App Local ({appName})");
            }
        }
    }
    #endregion

    #region 13. BioWare
    private static void DetectBioWare(
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        var biowareDir = Path.Combine(folders.Documents, "BioWare");
        if (!Directory.Exists(biowareDir)) return;

        try
        {
            var dirs = Directory.GetDirectories(biowareDir);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                foreach (var kw in keywords)
                {
                    if (IsMatch(name, kw))
                    {
                        var saveDir = Path.Combine(dir, "Save");
                        if (Directory.Exists(saveDir))
                        {
                            addDir(saveDir, SaveLocationType.Documents, $"BioWare ({name}\\Save)");
                        }
                        else
                        {
                            addDir(dir, SaveLocationType.Documents, $"BioWare ({name})");
                        }
                    }
                }
            }
        }
        catch { }
    }
    #endregion

    #region 14. KiriKiri / TVP
    private static void DetectKiriKiri(
        SteamGame game,
        UserProfileFolders folders,
        List<string> keywords,
        Action<string, SaveLocationType, string> addDir)
    {
        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            var candidates = new[] { "savedata", "save" };
            foreach (var cand in candidates)
            {
                var saveDir = Path.Combine(game.InstallPath, cand);
                if (Directory.Exists(saveDir))
                {
                    addDir(saveDir, SaveLocationType.Custom, $"KiriKiri ({cand})");
                }
            }
        }

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw) || kw.Length < 3) continue;

            var savedGames = Path.Combine(folders.SavedGames, kw);
            if (Directory.Exists(savedGames))
            {
                addDir(savedGames, SaveLocationType.SavedGames, $"KiriKiri ({kw})");
            }
        }
    }
    #endregion

    #region Helpers
    private static List<string> GenerateKeywords(SteamGame game)
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

    private static bool IsMatch(string dirName, string keyword)
    {
        var cleanDir = Clean(dirName);
        var cleanKw = Clean(keyword);

        if (string.IsNullOrWhiteSpace(cleanDir) || string.IsNullOrWhiteSpace(cleanKw))
            return false;

        return cleanDir.Equals(cleanKw, StringComparison.OrdinalIgnoreCase) ||
               cleanDir.StartsWith(cleanKw, StringComparison.OrdinalIgnoreCase) ||
               cleanKw.StartsWith(cleanDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }
    #endregion
}
