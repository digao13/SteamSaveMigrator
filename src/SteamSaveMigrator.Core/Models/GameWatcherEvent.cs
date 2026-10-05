using System;

namespace SteamSaveMigrator.Core.Models;

public enum GameWatcherEventType
{
    GameStarted,
    GameExited,
    BackupStarted,
    BackupCompleted,
    CloudUploadStarted,
    CloudUploadCompleted,
    Error
}

/// <summary>
/// Evento gerado pelo monitor automático de jogos da Steam.
/// </summary>
public record GameWatcherEvent
{
    public GameWatcherEventType Type { get; init; }
    public SteamGame Game { get; init; } = null!;
    public string Message { get; init; } = string.Empty;
    public string? LocalZipPath { get; init; }
    public CloudBackupInfo? CloudBackup { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public bool Success { get; init; } = true;
}
