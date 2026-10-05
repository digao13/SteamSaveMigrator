using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Representa uma biblioteca da Steam (definida em libraryfolders.vdf).
/// </summary>
public record SteamLibraryFolder
{
    public int Index { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public long TotalSizeBytes { get; init; }
    public Dictionary<uint, long> AppIdsWithSizes { get; init; } = new();

    public string SteamAppsPath => System.IO.Path.Combine(Path, "steamapps");
    public string CommonPath => System.IO.Path.Combine(SteamAppsPath, "common");
}
