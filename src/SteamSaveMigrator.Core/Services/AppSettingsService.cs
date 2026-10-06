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

    /// <summary>
    /// Caminho alternativo de arquivo exclusivo para testes unitários isolados.
    /// </summary>
    public static string? OverrideConfigPathForTesting { get; set; }

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

    private static string GetLocalAppConfigPath()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "settings.json");
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Normaliza e valida um caminho de diretório (remove aspas, espaços extras e expande variáveis).
    /// </summary>
    public static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim().Trim('\"', '\'').Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;

        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
            return Path.GetFullPath(trimmed).TrimEnd('\\', '/');
        }
        catch
        {
            return trimmed.TrimEnd('\\', '/');
        }
    }

    /// <summary>
    /// Retorna o diretório de backups efetivo configurado ou o padrão de perfil do usuário.
    /// </summary>
    public static string GetEffectiveBackupDirectory()
    {
        var settings = LoadSettings();
        var normalized = NormalizeDirectoryPath(settings.CustomBackupDirectory);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamSavesBackup");
    }

    public static UserSettings LoadSettings()
    {
        if (!string.IsNullOrWhiteSpace(OverrideConfigPathForTesting))
        {
            if (File.Exists(OverrideConfigPathForTesting))
            {
                try
                {
                    var json = File.ReadAllText(OverrideConfigPathForTesting);
                    var testSettings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
                    testSettings.CustomBackupDirectory = NormalizeDirectoryPath(testSettings.CustomBackupDirectory);
                    return testSettings;
                }
                catch { }
            }
            return new UserSettings();
        }

        var userPath = GetUserConfigPath();
        var commonPath = GetCommonConfigPath();
        var localPath = GetLocalAppConfigPath();

        UserSettings? userSettings = null;
        DateTime userTime = DateTime.MinValue;

        UserSettings? commonSettings = null;
        DateTime commonTime = DateTime.MinValue;

        UserSettings? localSettings = null;
        DateTime localTime = DateTime.MinValue;

        // 1. Tenta carregar do AppData do usuário
        if (File.Exists(userPath))
        {
            try
            {
                var json = File.ReadAllText(userPath);
                userSettings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                userTime = File.GetLastWriteTimeUtc(userPath);
            }
            catch { }
        }

        // 2. Tenta carregar do ProgramData compartilhado
        if (File.Exists(commonPath))
        {
            try
            {
                var json = File.ReadAllText(commonPath);
                commonSettings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                commonTime = File.GetLastWriteTimeUtc(commonPath);
            }
            catch { }
        }

        // 3. Tenta carregar do diretório local da aplicação
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            try
            {
                var json = File.ReadAllText(localPath);
                localSettings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                localTime = File.GetLastWriteTimeUtc(localPath);
            }
            catch { }
        }

        // Seleciona a fonte mais recente ou que possui o diretório de backup configurado
        UserSettings? chosen = null;

        // Lista de candidatos com suas respectivas datas
        var candidates = new (UserSettings? settings, DateTime time)[]
        {
            (userSettings, userTime),
            (commonSettings, commonTime),
            (localSettings, localTime)
        }
        .Where(c => c.settings != null)
        .OrderByDescending(c => !string.IsNullOrWhiteSpace(c.settings!.CustomBackupDirectory))
        .ThenByDescending(c => c.time)
        .ToList();

        if (candidates.Count > 0)
        {
            chosen = candidates[0].settings;
        }

        chosen ??= new UserSettings();

        // Se uma fonte tinha CustomBackupDirectory mas a escolhida não, preserva o diretório
        if (string.IsNullOrWhiteSpace(chosen.CustomBackupDirectory))
        {
            var withDir = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.settings!.CustomBackupDirectory));
            if (withDir.settings != null)
            {
                chosen.CustomBackupDirectory = withDir.settings.CustomBackupDirectory;
            }
        }

        chosen.CustomBackupDirectory = NormalizeDirectoryPath(chosen.CustomBackupDirectory);

        return chosen;
    }

    public static void SaveSettings(UserSettings settings)
    {
        try
        {
            settings.CustomBackupDirectory = NormalizeDirectoryPath(settings.CustomBackupDirectory);

            if (!string.IsNullOrWhiteSpace(OverrideConfigPathForTesting))
            {
                var testJson = JsonSerializer.Serialize(settings, JsonOptions);
                var testDir = Path.GetDirectoryName(OverrideConfigPathForTesting);
                if (!string.IsNullOrWhiteSpace(testDir) && !Directory.Exists(testDir))
                {
                    Directory.CreateDirectory(testDir);
                }
                File.WriteAllText(OverrideConfigPathForTesting, testJson);
                return;
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var userPath = GetUserConfigPath();
            try
            {
                File.WriteAllText(userPath, json);
            }
            catch { }

            var commonPath = GetCommonConfigPath();
            try
            {
                File.WriteAllText(commonPath, json);
                EnsureFilePermissions(commonPath);
            }
            catch { }

            var localPath = GetLocalAppConfigPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                try
                {
                    File.WriteAllText(localPath, json);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Atualiza e persiste imediatamente apenas o diretório de backups do usuário.
    /// </summary>
    public static void UpdateCustomBackupDirectory(string newDirectory)
    {
        var normalized = NormalizeDirectoryPath(newDirectory);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        try
        {
            var settings = LoadSettings();
            settings.CustomBackupDirectory = normalized;
            SaveSettings(settings);
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
