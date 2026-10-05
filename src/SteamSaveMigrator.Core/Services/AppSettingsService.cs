using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Modelo com as preferências do usuário no SteamSaveMigrator.
/// </summary>
public class UserSettings
{
    /// <summary>
    /// Diretório personalizado onde os backups locais (.zip) são salvos.
    /// </summary>
    public string CustomBackupDirectory { get; set; } = string.Empty;

    public bool AutoStartWatcherOnLaunch { get; set; } = true;
    public bool WatcherAutoBackup { get; set; } = true;
    public bool WatcherAutoUpload { get; set; } = true;
    public int WatcherPollInterval { get; set; } = 3;
    public int WatcherPostExitDelay { get; set; } = 5;
    public bool StartWithWindows { get; set; } = false;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Inicia o aplicativo da Steam automaticamente ao abrir o SteamSaveMigrator.
    /// </summary>
    public bool OpenSteamOnStartup { get; set; } = true;

    /// <summary>
    /// Último perfil Steam selecionado como destino de restauração.
    /// </summary>
    public uint? LastTargetSteamId3 { get; set; }

    /// <summary>
    /// Último perfil Windows selecionado como destino de restauração.
    /// </summary>
    public string LastTargetWindowsUsername { get; set; } = string.Empty;

    /// <summary>
    /// Último perfil Steam selecionado como origem de backup.
    /// </summary>
    public uint? LastSourceSteamId3 { get; set; }

    /// <summary>
    /// Último perfil Windows selecionado como origem de backup.
    /// </summary>
    public string LastSourceWindowsUsername { get; set; } = string.Empty;
}

/// <summary>
/// Serviço de persistência e gerenciamento de configurações da aplicação.
/// Suporta sincronização transparente entre perfis do Windows.
/// </summary>
public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GetUserConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "SteamSaveMigrator");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private static string GetCommonConfigPath()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(common, "SteamSaveMigrator");
        if (!Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
                EnsureDirectoryPermissions(dir);
            }
            catch { }
        }
        return Path.Combine(dir, "settings.json");
    }

    public static UserSettings LoadSettings()
    {
        var userPath = GetUserConfigPath();
        var commonPath = GetCommonConfigPath();

        // 1. Tenta carregar do AppData do usuário
        if (File.Exists(userPath))
        {
            try
            {
                var json = File.ReadAllText(userPath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (settings != null) return settings;
            }
            catch { }
        }

        // 2. Fallback para ProgramData compartilhado
        if (File.Exists(commonPath))
        {
            try
            {
                var json = File.ReadAllText(commonPath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // Copia para o usuário local
                    try { File.WriteAllText(userPath, json); } catch { }
                    return settings;
                }
            }
            catch { }
        }

        return new UserSettings();
    }

    public static void SaveSettings(UserSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var userPath = GetUserConfigPath();
            File.WriteAllText(userPath, json);

            var commonPath = GetCommonConfigPath();
            try
            {
                File.WriteAllText(commonPath, json);
                EnsureFilePermissions(commonPath);
            }
            catch { }
        }
        catch { }
    }

    public static void EnsureDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(path)) return;
        try
        {
            var dInfo = new DirectoryInfo(path);
            var dSec = dInfo.GetAccessControl();
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var rule = new FileSystemAccessRule(
                usersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            dSec.AddAccessRule(rule);
            dInfo.SetAccessControl(dSec);
        }
        catch { }
    }

    public static void EnsureFilePermissions(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath)) return;
        try
        {
            var fInfo = new FileInfo(filePath);
            var fSec = fInfo.GetAccessControl();
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var rule = new FileSystemAccessRule(
                usersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                AccessControlType.Allow);

            fSec.AddAccessRule(rule);
            fInfo.SetAccessControl(fSec);
        }
        catch { }
    }
}
