using System.Collections.Generic;
using System.Linq;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Resultado do escaneamento geral de jogos e saves na máquina.
/// </summary>
public record SaveScanResult
{
    public SteamInstallationInfo SteamInfo { get; init; } = new();
    public List<SteamGame> Games { get; init; } = new();

    public int TotalGames => Games.Count;
    public int GamesWithSavesCount => Games.Count(g => g.HasSaves);
    public int TotalSaveFiles => Games.Sum(g => g.TotalSaveFiles);
    public long TotalSaveSizeBytes => Games.Sum(g => g.TotalSaveSizeBytes);
}
