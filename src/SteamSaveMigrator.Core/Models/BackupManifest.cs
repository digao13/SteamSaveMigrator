using System;
using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Entrada individual de um arquivo salvo dentro do backup.
/// </summary>
public record BackupManifestEntry
{
    public string OriginalFullPath { get; init; } = string.Empty;
    public string TokenizedPath { get; init; } = string.Empty;
    public SaveLocationType LocationType { get; init; }
    public string ZipEntryName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public string? Sha256Checksum { get; init; }
}

/// <summary>
/// Manifesto de metadados gravado dentro do pacote de backup de saves.
/// </summary>
public record BackupManifest
{
    public string ManifestVersion { get; init; } = "1.0";
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string SourceMachineName { get; init; } = Environment.MachineName;
    public string SourceUsername { get; init; } = string.Empty;
    public string SourceUserProfile { get; init; } = string.Empty;
    public uint? SourceSteamId3 { get; init; }
    public ulong? SourceSteamId64 { get; init; }
    public string? SourceSteamPersonaName { get; init; }
    public uint AppId { get; init; }
    public string GameName { get; init; } = string.Empty;
    public List<BackupManifestEntry> Entries { get; init; } = new();
    public List<GameAchievementInfo> Achievements { get; init; } = new();

    public int TotalFiles => Entries.Count;
    public long TotalSizeBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in Entries) total += entry.FileSizeBytes;
            return total;
        }
    }
}
