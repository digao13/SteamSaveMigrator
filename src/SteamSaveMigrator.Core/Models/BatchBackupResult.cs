using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Relatório de progresso de um backup em lote (múltiplos jogos).
/// </summary>
public record BatchBackupProgressReport
{
    public int TotalGames { get; init; }
    public int CurrentGameIndex { get; init; }
    public string CurrentGameName { get; init; } = string.Empty;
    public uint CurrentGameAppId { get; init; }
    public int CurrentFileIndex { get; init; }
    public int CurrentGameTotalFiles { get; init; }
    public string CurrentFileName { get; init; } = string.Empty;
    public double OverallPercent { get; init; }
    public double CurrentGamePercent { get; init; }
}

/// <summary>
/// Resultado da operação de backup de múltiplos jogos simultâneos.
/// </summary>
public record BatchBackupResult
{
    public bool IsSuccess => FailureCount == 0;
    public int TotalGamesProcessed { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public List<string> GeneratedZipFiles { get; init; } = new();
    public Dictionary<string, string> Errors { get; init; } = new();
    public int TotalFilesArchived { get; init; }
    public long TotalSizeBytesArchived { get; init; }
}
