namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Tipos de diretórios onde jogos salvam dados no Windows e na Steam.
/// </summary>
public enum SaveLocationType
{
    SteamUserdata,
    AppDataLocal,
    AppDataLocalLow,
    AppDataRoaming,
    Documents,
    SavedGames,
    Custom
}
