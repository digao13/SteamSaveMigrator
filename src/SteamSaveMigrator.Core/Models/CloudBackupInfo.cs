namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Metadados de um arquivo de backup armazenado no Google Drive.
/// </summary>
public record CloudBackupInfo
{
    public string FileId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string GameName { get; init; } = string.Empty;
    public uint AppId { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime? CreatedTime { get; init; }
    public string SourceUsername { get; init; } = string.Empty;
    public string SourceSteamPersona { get; init; } = string.Empty;
    public uint? SourceSteamId3 { get; init; }
    public ulong? SourceSteamId64 { get; init; }
    public string SourceMachineName { get; init; } = string.Empty;
    public string? WebViewLink { get; init; }
    public int AchievementsCount { get; init; }
    public int AchievementsUnlockedCount { get; init; }
    public string FormattedSize => Utils.ByteSizeFormatter.FormatBytes(FileSizeBytes);

    public string AchievementsDisplay => AchievementsCount > 0
        ? $"🏆 {AchievementsUnlockedCount}/{AchievementsCount}"
        : "—";

    public string SourceSteamDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SourceSteamPersona))
            {
                return SourceSteamId3.HasValue
                    ? $"{SourceSteamPersona} ({SourceSteamId3})"
                    : SourceSteamPersona;
            }

            return SourceSteamId3.HasValue
                ? $"SteamID: {SourceSteamId3}"
                : "Steam (Geral)";
        }
    }

    public string SourceUserDisplay =>
        !string.IsNullOrWhiteSpace(SourceUsername) ? SourceUsername : "Windows";
}

