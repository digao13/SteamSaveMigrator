using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Representa um jogo Steam detectado através de seu AppManifest.
/// </summary>
public record SteamGame
{
    public uint AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string InstallDir { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string LibraryPath { get; init; } = string.Empty;
    public long SizeOnDisk { get; init; }
    public DateTime? LastUpdated { get; init; }
    public bool IsSelected { get; set; }

    public List<GameSaveLocation> DetectedSaveLocations { get; init; } = new();

    public bool HasSaves => DetectedSaveLocations.Any(loc => loc.Exists && loc.FileCount > 0);
    public int TotalSaveFiles => DetectedSaveLocations.Sum(loc => loc.FileCount);
    public long TotalSaveSizeBytes => DetectedSaveLocations.Sum(loc => loc.TotalSizeBytes);

    public string SizeFormatted
    {
        get
        {
            if (SizeOnDisk <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = SizeOnDisk;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:0.##} {units[unitIndex]}";
        }
    }
}
