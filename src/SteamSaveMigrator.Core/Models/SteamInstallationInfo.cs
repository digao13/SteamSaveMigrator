using System.Collections.Generic;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Informações completas sobre a instalação da Steam detectada.
/// </summary>
public record SteamInstallationInfo
{
    public bool IsFound { get; init; }
    public string SteamPath { get; init; } = string.Empty;
    public string SteamExe { get; init; } = string.Empty;
    public string UserdataDirectory => System.IO.Path.Combine(SteamPath, "userdata");
    public string ConfigDirectory => System.IO.Path.Combine(SteamPath, "config");
    public string SteamAppsDirectory => System.IO.Path.Combine(SteamPath, "steamapps");
    public string LibraryFoldersVdfPath => System.IO.Path.Combine(SteamAppsDirectory, "libraryfolders.vdf");

    public List<SteamUserAccount> Accounts { get; init; } = new();
    public List<SteamLibraryFolder> Libraries { get; init; } = new();

    public SteamUserAccount? ActiveAccount =>
        Accounts.Find(a => a.IsActiveNow) ??
        Accounts.Find(a => a.MostRecent) ??
        (Accounts.Count > 0 ? Accounts[0] : null);

    public SteamUserAccount? PrimaryAccount => ActiveAccount;

    public bool HasActiveAccount => ActiveAccount != null;

    public string ActiveAccountDisplayName =>
        ActiveAccount != null
            ? $"{ActiveAccount.PersonaName} (SteamID3: {ActiveAccount.SteamId3})"
            : "Nenhuma conta ativa detectada";
}
