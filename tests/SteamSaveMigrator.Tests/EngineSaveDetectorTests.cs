using System;
using System.IO;
using System.Linq;
using SteamSaveMigrator.Core.Detectors;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class EngineSaveDetectorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _userProfile;
    private readonly UserProfileFolders _folders;
    private readonly SteamInstallationInfo _steamInfo;

    public EngineSaveDetectorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SSM_EngineTests_" + Guid.NewGuid().ToString("N"));
        _userProfile = Path.Combine(_tempRoot, "Users", "TestPlayer");

        _folders = WindowsKnownFolders.GetFoldersForUserProfile(_userProfile);

        Directory.CreateDirectory(_folders.AppDataLocal);
        Directory.CreateDirectory(_folders.AppDataLocalLow);
        Directory.CreateDirectory(_folders.AppDataRoaming);
        Directory.CreateDirectory(_folders.Documents);
        Directory.CreateDirectory(_folders.SavedGames);

        var steamPath = Path.Combine(_tempRoot, "Steam");
        Directory.CreateDirectory(Path.Combine(steamPath, "userdata"));

        _steamInfo = new SteamInstallationInfo
        {
            IsFound = true,
            SteamPath = steamPath,
            Accounts = new()
        };
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
    public void Should_Detect_Unity_Game_With_AppInfo_And_Studio_Folder()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "IndieUnityGame");
        var dataDir = Path.Combine(gameInstall, "IndieGame_Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "app.info"), "StudioPixel\nSuperIndie\n");

        var saveFolder = Path.Combine(_folders.AppDataLocalLow, "StudioPixel", "SuperIndie");
        Directory.CreateDirectory(saveFolder);
        File.WriteAllText(Path.Combine(saveFolder, "SaveData.dat"), "UNITY_SAVE_DATA");

        var game = new SteamGame
        {
            AppId = 1001,
            Name = "Super Indie",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(saveFolder, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_Unreal_Engine_4_5_From_Uproject_And_LocalAppData()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "CyberShooter");
        Directory.CreateDirectory(gameInstall);
        File.WriteAllText(Path.Combine(gameInstall, "CyberShooter.uproject"), "{}");

        var ueSaveFolder = Path.Combine(_folders.AppDataLocal, "CyberShooter", "Saved", "SaveGames");
        Directory.CreateDirectory(ueSaveFolder);
        File.WriteAllText(Path.Combine(ueSaveFolder, "SaveSlot_0.sav"), "UE_SAVED_BYTES");

        var game = new SteamGame
        {
            AppId = 2002,
            Name = "Cyber Shooter Definitive Edition",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.Files.Any(f => f.RelativePath.EndsWith("SaveSlot_0.sav", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Should_Detect_Unreal_Engine_3_In_Documents_My_Games()
    {
        var myGamesDir = Path.Combine(_folders.Documents, "My Games", "RetroShooter");
        Directory.CreateDirectory(myGamesDir);
        File.WriteAllText(Path.Combine(myGamesDir, "SaveData.bin"), "UE3_SAVE");

        var game = new SteamGame
        {
            AppId = 3003,
            Name = "Retro Shooter"
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(myGamesDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_Godot_Game_From_Pck_In_AppData_Roaming_Godot()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "GodotGalaxy");
        Directory.CreateDirectory(gameInstall);
        File.WriteAllText(Path.Combine(gameInstall, "SpaceAdventure.pck"), "GODOT_PCK_HEADER");

        var godotSave = Path.Combine(_folders.AppDataRoaming, "Godot", "app_userdata", "SpaceAdventure");
        Directory.CreateDirectory(godotSave);
        File.WriteAllText(Path.Combine(godotSave, "save.json"), "{\"level\": 10}");

        var game = new SteamGame
        {
            AppId = 4004,
            Name = "Space Adventure",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(godotSave, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_GameMaker_From_OptionsIni_In_LocalAppData()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "PizzaGame");
        Directory.CreateDirectory(gameInstall);
        File.WriteAllText(Path.Combine(gameInstall, "data.win"), "GAMEMAKER_DATA");
        File.WriteAllText(Path.Combine(gameInstall, "options.ini"), "[Windows]\nDisplayName=\"PizzaKingdom\"\n");

        var gmSave = Path.Combine(_folders.AppDataLocal, "PizzaKingdom");
        Directory.CreateDirectory(gmSave);
        File.WriteAllText(Path.Combine(gmSave, "save.ini"), "Score=9999");

        var game = new SteamGame
        {
            AppId = 5005,
            Name = "Pizza Kingdom Game",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(gmSave, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_RpgMaker_MV_From_Install_Www_Save()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "FantasyQuest");
        var wwwSave = Path.Combine(gameInstall, "www", "save");
        Directory.CreateDirectory(wwwSave);
        File.WriteAllText(Path.Combine(wwwSave, "file1.rpgsave"), "RPGMAKER_DATA");

        var game = new SteamGame
        {
            AppId = 6006,
            Name = "Fantasy Quest",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(wwwSave, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_RpgMaker_XP_From_GameIni_In_SavedGames()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "TearfulStory");
        Directory.CreateDirectory(gameInstall);
        File.WriteAllText(Path.Combine(gameInstall, "Game.ini"), "[Game]\nTitle=Tearful Story\n");

        var savedGame = Path.Combine(_folders.SavedGames, "Tearful Story");
        Directory.CreateDirectory(savedGame);
        File.WriteAllText(Path.Combine(savedGame, "Save1.rxdata"), "RPGMAKER_XP_SAVE");

        var game = new SteamGame
        {
            AppId = 7007,
            Name = "Tearful Story",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(savedGame, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_RenPy_VisualNovel_In_AppData_Roaming_RenPy()
    {
        var renpyDir = Path.Combine(_folders.AppDataRoaming, "RenPy", "NovelHearts-16999999");
        Directory.CreateDirectory(renpyDir);
        File.WriteAllText(Path.Combine(renpyDir, "1-LT1.save"), "RENPY_SAVE");

        var game = new SteamGame
        {
            AppId = 8008,
            Name = "Novel Hearts"
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(renpyDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_Source_Engine_In_Install_ModDir_SAVE()
    {
        var gameInstall = Path.Combine(_tempRoot, "Games", "HalfPortal");
        var modSave = Path.Combine(gameInstall, "portal_custom", "SAVE");
        Directory.CreateDirectory(modSave);
        File.WriteAllText(Path.Combine(gameInstall, "portal_custom", "gameinfo.txt"), "\"GameInfo\" { game \"Portal Custom\" }");
        File.WriteAllText(Path.Combine(modSave, "autosave.sav"), "SOURCE_SAV_BYTES");

        var game = new SteamGame
        {
            AppId = 9009,
            Name = "Portal Custom",
            InstallPath = gameInstall
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(modSave, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_Bethesda_CreationEngine_In_Documents_My_Games()
    {
        var bethesdaSave = Path.Combine(_folders.Documents, "My Games", "Skyrim Special Edition", "Saves");
        Directory.CreateDirectory(bethesdaSave);
        File.WriteAllText(Path.Combine(bethesdaSave, "Save1_Player.ess"), "BETHESDA_ESS");

        var game = new SteamGame
        {
            AppId = 489830,
            Name = "The Elder Scrolls V: Skyrim Special Edition"
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.SourcePath.Equals(bethesdaSave, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_MonoGame_FNA_In_AppData_Roaming()
    {
        var farmSave = Path.Combine(_folders.AppDataRoaming, "FarmValley", "Saves");
        Directory.CreateDirectory(farmSave);
        File.WriteAllText(Path.Combine(farmSave, "Farmer_12345"), "MONOGAME_XML_SAVE");

        var game = new SteamGame
        {
            AppId = 11111,
            Name = "Farm Valley"
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.Files.Any(f => f.RelativePath.EndsWith("Farmer_12345", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Should_Replace_Redundant_Child_Paths_When_Parent_Directory_Is_Detected()
    {
        // Cria pasta de jogo com subpasta de Steam ID (padrão Solateria/Unity)
        var gameDir = Path.Combine(_folders.AppDataLocalLow, "CoolStudio", "EpicGame");
        var steamIdDir = Path.Combine(gameDir, "76561198000000000");
        Directory.CreateDirectory(steamIdDir);
        File.WriteAllText(Path.Combine(steamIdDir, "SaveData.txt"), "SLOT1");
        File.WriteAllText(Path.Combine(gameDir, "global_settings.json"), "SETTINGS");

        var game = new SteamGame
        {
            AppId = 121212,
            Name = "Epic Game"
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, _steamInfo, _userProfile);

        Assert.NotEmpty(detected);

        // Deve conter a pasta pai do jogo (gameDir) e NÃO manter a subpasta redundante duplicada
        Assert.Contains(detected, loc => loc.SourcePath.Equals(gameDir, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detected, loc => loc.SourcePath.Equals(steamIdDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Detect_Solateria_Real_Data_Accurately()
    {
        var localLowSolateria = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Studio Doodal", "Solateria");
        if (!Directory.Exists(localLowSolateria)) return;

        var game = new SteamGame
        {
            AppId = 2947280,
            Name = "Solateria",
            InstallPath = @"G:\SteamLibrary\steamapps\common\Solateria"
        };

        var steamInfo = new SteamInstallationInfo
        {
            IsFound = true,
            SteamPath = @"G:\SteamLibrary",
            Accounts = new()
        };

        var detected = SaveLocationDetector.DetectSaveLocations(game, steamInfo);
        Assert.NotEmpty(detected);
        Assert.Contains(detected, loc => loc.Files.Any(f => f.RelativePath.EndsWith("SaveData.txt", StringComparison.OrdinalIgnoreCase)));
    }
}
