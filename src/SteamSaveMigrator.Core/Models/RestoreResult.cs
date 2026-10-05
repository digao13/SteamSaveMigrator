using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Relatório de restauração de um arquivo individual.
/// </summary>
public record RestoredItemReport
{
    public string OriginalPath { get; init; } = string.Empty;
    public string TokenizedPath { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasOverwritten { get; init; }
}

/// <summary>
/// Resultado da operação de restauração de backup.
/// </summary>
public record RestoreResult
{
    public bool IsSuccess { get; init; }
    public string GameName { get; init; } = string.Empty;
    public uint AppId { get; init; }
    public int TotalFilesProcessed { get; init; }
    public int SuccessCount { get; init; }
    public int SkippedCount { get; init; }
    public int ErrorCount { get; init; }
    public uint? SourceSteamId3 { get; init; }
    public uint? TargetSteamId3 { get; init; }
    public string? TargetSteamPersonaName { get; init; }
    public bool SteamProfileMigrated => TargetSteamId3.HasValue && (!SourceSteamId3.HasValue || SourceSteamId3.Value != TargetSteamId3.Value);
    public int AchievementsSynced { get; init; }
    public AchievementSyncResult? AchievementSyncDetails { get; init; }
    public List<GameAchievementInfo> Achievements { get; init; } = new();
    public List<RestoredItemReport> Items { get; init; } = new();
    public List<string> GlobalErrors { get; init; } = new();
}
