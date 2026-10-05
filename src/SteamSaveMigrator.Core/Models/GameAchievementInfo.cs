using System;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Representa os dados de uma conquista da Steam (Achievement) associada a um jogo.
/// </summary>
public record GameAchievementInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsUnlocked { get; init; }
    public DateTime? UnlockTimeUtc { get; init; }
    public bool IsHidden { get; init; }
    public string? IconUrl { get; init; }
    public string? StatToken { get; init; }
    public int? GroupId { get; init; }
    public int? BitId { get; init; }
}
