using System;
using System.IO;

namespace SteamSaveMigrator.Core.Paths;

/// <summary>
/// Fornece resolução das pastas de perfis de usuário do Windows.
/// Suporta o usuário atual do sistema ou qualquer outro usuário informado (ex: C:\Users\NOVO_USUARIO).
/// </summary>
public static class WindowsKnownFolders
{
    public static string GetCurrentProfile() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string GetCurrentAppDataLocal() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string GetCurrentAppDataRoaming() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string GetCurrentAppDataLocalLow() =>
        Path.Combine(GetCurrentProfile(), "AppData", "LocalLow");

    public static string GetCurrentDocuments() =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string GetCurrentSavedGames() =>
        Path.Combine(GetCurrentProfile(), "Saved Games");

    /// <summary>
    /// Calcula os caminhos das pastas padrão para um perfil de usuário arbitrário.
    /// </summary>
    public static UserProfileFolders GetFoldersForUserProfile(string userProfilePath)
    {
        var profile = Path.GetFullPath(userProfilePath.TrimEnd('\\', '/'));
        return new UserProfileFolders(
            UserProfile: profile,
            AppDataLocal: Path.Combine(profile, "AppData", "Local"),
            AppDataLocalLow: Path.Combine(profile, "AppData", "LocalLow"),
            AppDataRoaming: Path.Combine(profile, "AppData", "Roaming"),
            Documents: Path.Combine(profile, "Documents"),
            SavedGames: Path.Combine(profile, "Saved Games")
        );
    }

    /// <summary>
    /// Calcula os caminhos dado apenas o nome do usuário (assumindo raiz padrão C:\Users ou %SystemDrive%\Users).
    /// </summary>
    public static UserProfileFolders GetFoldersForUsername(string username, string? usersRootDirectory = null)
    {
        var root = usersRootDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            root = Path.Combine(systemDrive, "Users");
        }

        var profilePath = Path.Combine(root, username);
        return GetFoldersForUserProfile(profilePath);
    }

    /// <summary>
    /// Lista os perfis de usuário do Windows disponíveis na máquina local (diretórios em C:\Users).
    /// </summary>
    public static System.Collections.Generic.List<WindowsProfileInfo> GetAvailableUserProfiles()
    {
        var list = new System.Collections.Generic.List<WindowsProfileInfo>();
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var usersDir = Path.Combine(systemDrive, "Users");

        if (!Directory.Exists(usersDir)) return list;

        var systemAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Public", "Default", "Default User", "All Users", "desktop.ini"
        };

        try
        {
            var currentProfilePath = GetCurrentProfile();
            var currentProfileDirName = Path.GetFileName(currentProfilePath.TrimEnd('\\', '/'));
            var currentUsername = Environment.UserName;

            foreach (var dir in Directory.GetDirectories(usersDir))
            {
                var dirName = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(dirName) || systemAccounts.Contains(dirName)) continue;

                bool isCurrent = string.Equals(dirName, currentProfileDirName, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(dirName, currentUsername, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(Path.GetFullPath(dir), Path.GetFullPath(currentProfilePath), StringComparison.OrdinalIgnoreCase);

                list.Add(new WindowsProfileInfo(
                    Username: dirName,
                    ProfilePath: dir,
                    IsCurrentAccount: isCurrent
                ));
            }

            // Garante que o usuário atual esteja presente e marcado
            if (!list.Any(p => p.IsCurrentAccount))
            {
                list.Insert(0, new WindowsProfileInfo(currentUsername, currentProfilePath, true));
            }
        }
        catch
        {
            // Fallback: ao menos o usuário atual
            var cur = Environment.UserName;
            var prof = GetCurrentProfile();
            list.Add(new WindowsProfileInfo(cur, prof, true));
        }

        return list;
    }
}

public record WindowsProfileInfo(
    string Username,
    string ProfilePath,
    bool IsCurrentAccount
)
{
    public string DisplayName => IsCurrentAccount ? $"{Username} (Usuário Atual)" : Username;
}

public record UserProfileFolders(
    string UserProfile,
    string AppDataLocal,
    string AppDataLocalLow,
    string AppDataRoaming,
    string Documents,
    string SavedGames
);
