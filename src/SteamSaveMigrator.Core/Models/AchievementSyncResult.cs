namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Resultado da operação de sincronização de conquistas entre perfis da Steam.
/// </summary>
public record AchievementSyncResult
{
    public bool IsSuccess { get; init; }
    public int SyncedCount { get; init; }
    public bool LiveSyncedViaSteamworks { get; init; }
    public string? ActiveConnectedPersona { get; init; }
    public uint? ActiveConnectedSteamId3 { get; init; }
    public string Message { get; init; } = string.Empty;
}
