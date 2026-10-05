using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Informações sobre um arquivo individual de save detectado.
/// </summary>
public record SaveFileInfo
{
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public System.DateTime LastWriteTimeUtc { get; init; }
}

/// <summary>
/// Representa uma localização de save detectada para um jogo específico.
/// </summary>
public record GameSaveLocation
{
    public SaveLocationType LocationType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string TokenizedPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public List<SaveFileInfo> Files { get; init; } = new();

    public string SizeFormatted
    {
        get
        {
            if (TotalSizeBytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = TotalSizeBytes;
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
