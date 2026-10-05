using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Resultado da restauração automática de múltiplos backups.
/// </summary>
public record BatchRestoreResult
{
    public bool IsSuccess => FailureCount == 0;
    public int TotalPackages { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int TotalFilesRestored { get; init; }
    public List<RestoreResult> Results { get; init; } = new();
    public List<string> GlobalErrors { get; init; } = new();
}
