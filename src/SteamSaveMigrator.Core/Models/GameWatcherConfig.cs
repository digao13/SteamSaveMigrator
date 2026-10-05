namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Configurações do monitor de processos e fechamento de jogos da Steam.
/// </summary>
public record GameWatcherConfig
{
    public bool AutoBackupOnGameExit { get; set; } = true;
    public bool AutoUploadToGoogleDrive { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 3;
    public int PostExitDelaySeconds { get; set; } = 2;
    public int MaxCloudBackupsPerGame { get; set; } = 5;
    public string LocalBackupOutputDir { get; set; } = string.Empty;
}
