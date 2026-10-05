using System.Collections.Generic;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Core.Detectors;

public record KnownGameSaveRule(
    uint AppId,
    string GameName,
    SaveLocationType LocationType,
    string RelativePathFormat, // Suporta {UserProfile}, {AppDataLocal}, {AppDataRoaming}, {AppDataLocalLow}, {Documents}, {SavedGames}
    string Description
);

/// <summary>
/// Base de dados de caminhos específicos para jogos populares.
/// </summary>
public static class KnownGameDatabase
{
    private static readonly List<KnownGameSaveRule> Rules = new()
    {
        // FromSoftware
        new(1245620, "ELDEN RING", SaveLocationType.AppDataRoaming, "EldenRing", "Saves e Perfis FromSoftware"),
        new(374320, "DARK SOULS III", SaveLocationType.AppDataRoaming, "DarkSoulsIII", "Saves Dark Souls 3"),
        new(814380, "Sekiro: Shadows Die Twice", SaveLocationType.AppDataRoaming, "Sekiro", "Saves Sekiro"),
        new(211420, "DARK SOULS: Prepare To Die Edition", SaveLocationType.Documents, "NBGI\\DarkSouls", "Saves Dark Souls PTDE"),

        // CD PROJEKT RED
        new(1091500, "Cyberpunk 2077", SaveLocationType.SavedGames, "CD Projekt Red\\Cyberpunk 2077", "Saves Cyberpunk 2077"),
        new(292030, "The Witcher 3: Wild Hunt", SaveLocationType.Documents, "The Witcher 3\\gamesaves", "Saves The Witcher 3"),

        // Larian Studios
        new(1086940, "Baldur's Gate 3", SaveLocationType.AppDataLocal, "Larian Studios\\Baldur's Gate 3\\PlayerProfiles", "Saves Baldur's Gate 3"),
        new(435150, "Divinity: Original Sin 2", SaveLocationType.Documents, "Larian Studios\\Divinity Original Sin 2 Definitive Edition\\PlayerProfiles", "Saves DOS2"),

        // Unity & Indie Games (LocalLow)
        new(367520, "Hollow Knight", SaveLocationType.AppDataLocalLow, "Team Cherry\\Hollow Knight", "Saves Hollow Knight"),
        new(892970, "Valheim", SaveLocationType.AppDataLocalLow, "IronGate\\Valheim", "Saves e Mundos Valheim"),
        new(1966720, "Lethal Company", SaveLocationType.AppDataLocalLow, "ZeekerssRBLX\\Lethal Company", "Saves Lethal Company"),
        new(646570, "Slay the Spire", SaveLocationType.SteamUserdata, "{AppId}\\remote", "Saves Slay the Spire"),
        new(413150, "Stardew Valley", SaveLocationType.AppDataRoaming, "StardewValley\\Saves", "Fazendas e Saves Stardew Valley"),
        new(1145360, "Hades", SaveLocationType.Documents, "Saved Games\\Hades", "Saves Hades"),
        new(1145350, "Hades II", SaveLocationType.SavedGames, "Hades II", "Saves Hades 2"),
        new(105600, "Terraria", SaveLocationType.Documents, "My Games\\Terraria\\Players", "Jogadores Terraria"),
        new(105600, "Terraria", SaveLocationType.Documents, "My Games\\Terraria\\Worlds", "Mundos Terraria"),

        // Bethesda
        new(489830, "The Elder Scrolls V: Skyrim Special Edition", SaveLocationType.Documents, "My Games\\Skyrim Special Edition\\Saves", "Saves Skyrim SE"),
        new(72850, "The Elder Scrolls V: Skyrim", SaveLocationType.Documents, "My Games\\Skyrim\\Saves", "Saves Skyrim"),
        new(377160, "Fallout 4", SaveLocationType.Documents, "My Games\\Fallout4\\Saves", "Saves Fallout 4"),

        // Rockstar Games
        new(271590, "Grand Theft Auto V", SaveLocationType.Documents, "Rockstar Games\\GTA V\\Profiles", "Perfis GTA V"),
        new(1174180, "Red Dead Redemption 2", SaveLocationType.Documents, "Rockstar Games\\Red Dead Redemption 2\\Profiles", "Saves RDR2"),

        // Pocketpair
        new(1623730, "Palworld", SaveLocationType.AppDataLocal, "Pal\\Saved\\SaveGames", "Saves e Mundos Palworld"),

        // Capcom / Monster Hunter / Resident Evil
        new(582010, "Monster Hunter: World", SaveLocationType.SteamUserdata, "{AppId}\\remote", "Saves MHW"),
        new(1446780, "MONSTER HUNTER RISE", SaveLocationType.SteamUserdata, "{AppId}\\remote", "Saves MHRise"),
        new(2050650, "Resident Evil 4", SaveLocationType.SteamUserdata, "{AppId}\\remote", "Saves RE4 Remake"),
        new(1196590, "Resident Evil Village", SaveLocationType.SteamUserdata, "{AppId}\\remote", "Saves RE Village")
    };

    public static IEnumerable<KnownGameSaveRule> GetRulesForApp(uint appId)
    {
        foreach (var rule in Rules)
        {
            if (rule.AppId == appId)
            {
                yield return rule;
            }
        }
    }
}
