using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Parsers;

namespace SteamSaveMigrator.Core.Detectors;

/// <summary>
/// Detector da instalação da Steam e das contas de usuário configuradas.
/// </summary>
public static class SteamDetector
{
    public static SteamInstallationInfo Detect(string? customSteamPath = null)
    {
        string? steamPath = customSteamPath;

        if (string.IsNullOrWhiteSpace(steamPath))
        {
            steamPath = FindSteamPathFromRegistry();
        }

        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            steamPath = FindSteamPathFromCommonLocations();
        }

        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return new SteamInstallationInfo
            {
                IsFound = false,
                SteamPath = string.Empty,
                SteamExe = string.Empty
            };
        }

        steamPath = Path.GetFullPath(steamPath);
        var steamExe = Path.Combine(steamPath, "steam.exe");

        var accounts = DetectAccounts(steamPath);

        return new SteamInstallationInfo
        {
            IsFound = true,
            SteamPath = steamPath,
            SteamExe = File.Exists(steamExe) ? steamExe : string.Empty,
            Accounts = accounts
        };
    }

    private static string? FindSteamPathFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // 1. HKCU
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key != null)
                {
                    var val = key.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                    {
                        return val.Replace('/', '\\');
                    }
                }
            }

            // 2. HKLM 64-bit
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                if (key != null)
                {
                    var val = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                    {
                        return val.Replace('/', '\\');
                    }
                }
            }

            // 3. HKLM 32-bit
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key != null)
                {
                    var val = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                    {
                        return val.Replace('/', '\\');
                    }
                }
            }
        }
        catch
        {
            // Tratamento silencioso caso o usuário não tenha permissão de leitura no registro
        }

        return null;
    }

    private static string? FindSteamPathFromCommonLocations()
    {
        var candidates = new List<string>();

        var p86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(p86))
            candidates.Add(Path.Combine(p86, "Steam"));

        var pFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(pFiles))
            candidates.Add(Path.Combine(pFiles, "Steam"));

        candidates.Add(@"C:\Steam");
        candidates.Add(@"D:\Steam");
        candidates.Add(@"E:\Steam");
        candidates.Add(@"F:\Steam");

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && (File.Exists(Path.Combine(dir, "steam.exe")) || Directory.Exists(Path.Combine(dir, "steamapps"))))
            {
                return dir;
            }
        }

        return null;
    }

    public static uint? GetActiveSteamUserId()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (key != null)
            {
                var activeUserVal = key.GetValue("ActiveUser");
                if (activeUserVal is int activeInt && activeInt > 0)
                {
                    return (uint)activeInt;
                }
                if (activeUserVal is long activeLong && activeLong > 0)
                {
                    return (uint)activeLong;
                }
            }
        }
        catch
        {
            // Tratamento silencioso
        }

        return null;
    }

    public static bool IsSteamProcessRunning()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (key != null)
            {
                var pidVal = key.GetValue("pid");
                if (pidVal is int pid && pid > 0)
                {
                    var proc = System.Diagnostics.Process.GetProcessById(pid);
                    return !proc.HasExited && proc.ProcessName.Contains("steam", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Tratamento silencioso
        }

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("steam");
            return processes != null && processes.Length > 0 && !processes[0].HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inicia o processo da Steam caso ela não esteja aberta no momento.
    /// </summary>
    public static bool LaunchSteamIfNotRunning(string? customSteamPath = null)
    {
        if (IsSteamProcessRunning()) return true;

        var info = Detect(customSteamPath);
        if (!string.IsNullOrWhiteSpace(info.SteamExe) && File.Exists(info.SteamExe))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = info.SteamExe,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                // Silencioso se der erro de permissão
            }
        }

        // Fallback via protocolo do Windows 'steam://'
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "steam://open/main",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetAutoLoginUserFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                return key.GetValue("AutoLoginUser") as string;
            }
        }
        catch
        {
            // Tratamento silencioso
        }

        return null;
    }

    private static List<SteamUserAccount> DetectAccounts(string steamPath)
    {
        var accounts = new Dictionary<uint, SteamUserAccount>();
        var userdataPath = Path.Combine(steamPath, "userdata");

        var activeSteamId3 = GetActiveSteamUserId();
        var autoLoginUser = GetAutoLoginUserFromRegistry();

        // 1. Tentar ler config/loginusers.vdf
        var loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (File.Exists(loginUsersPath))
        {
            try
            {
                var root = VdfParser.ParseFile(loginUsersPath);
                var usersNode = root["users"] ?? root;

                foreach (var userChild in usersNode.Children)
                {
                    if (ulong.TryParse(userChild.Name, out var steamId64))
                    {
                        var steamId3 = (uint)(steamId64 & 0xFFFFFFFF);
                        var accountName = userChild.GetString("AccountName") ?? string.Empty;
                        var personaName = userChild.GetString("PersonaName") ?? string.Empty;
                        var mostRecent = userChild.GetBoolean("MostRecent");
                        var timestamp = userChild.GetInt64("Timestamp");
                        DateTime? lastLogin = timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime : null;

                        var accountUserdataPath = Path.Combine(userdataPath, steamId3.ToString());

                        bool isActive = (activeSteamId3.HasValue && activeSteamId3.Value == steamId3) ||
                                        (!activeSteamId3.HasValue && !string.IsNullOrWhiteSpace(autoLoginUser) && accountName.Equals(autoLoginUser, StringComparison.OrdinalIgnoreCase));

                        accounts[steamId3] = new SteamUserAccount
                        {
                            SteamId64 = steamId64,
                            SteamId3 = steamId3,
                            AccountName = accountName,
                            PersonaName = personaName,
                            UserdataPath = accountUserdataPath,
                            MostRecent = mostRecent,
                            IsActiveNow = isActive,
                            LastLoginTime = lastLogin
                        };
                    }
                }
            }
            catch
            {
                // Erro de leitura no loginusers.vdf, segue com as pastas em userdata
            }
        }

        // 2. Verificar pastas em userdata/<SteamID3>
        if (Directory.Exists(userdataPath))
        {
            try
            {
                foreach (var subDir in Directory.GetDirectories(userdataPath))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (uint.TryParse(dirName, out var steamId3) && steamId3 > 0)
                    {
                        if (!accounts.ContainsKey(steamId3))
                        {
                            bool isActive = activeSteamId3.HasValue && activeSteamId3.Value == steamId3;
                            accounts[steamId3] = new SteamUserAccount
                            {
                                SteamId64 = 0x0110000100000000UL | steamId3,
                                SteamId3 = steamId3,
                                AccountName = $"User_{steamId3}",
                                PersonaName = $"Steam User {steamId3}",
                                UserdataPath = subDir,
                                MostRecent = false,
                                IsActiveNow = isActive,
                                LastLoginTime = null
                            };
                        }
                    }
                }
            }
            catch
            {
                // Sem permissão para listar userdata
            }
        }

        // 3. Se temos activeSteamId3 do registro mas não achou no VDF ou userdata, garante sua presença
        if (activeSteamId3.HasValue && activeSteamId3.Value > 0 && !accounts.ContainsKey(activeSteamId3.Value))
        {
            var id3 = activeSteamId3.Value;
            var path = Path.Combine(userdataPath, id3.ToString());
            accounts[id3] = new SteamUserAccount
            {
                SteamId64 = 0x0110000100000000UL | id3,
                SteamId3 = id3,
                AccountName = autoLoginUser ?? $"User_{id3}",
                PersonaName = autoLoginUser ?? $"Steam User {id3}",
                UserdataPath = path,
                MostRecent = true,
                IsActiveNow = true,
                LastLoginTime = DateTime.Now
            };
        }

        // Se nenhuma conta foi marcada como ativa ainda, marca a mais recente
        if (!accounts.Values.Any(a => a.IsActiveNow) && accounts.Count > 0)
        {
            var mostRecentAcc = accounts.Values.OrderByDescending(a => a.MostRecent).ThenByDescending(a => a.LastLoginTime).First();
            accounts[mostRecentAcc.SteamId3] = mostRecentAcc with { IsActiveNow = true };
        }

        return accounts.Values
            .OrderByDescending(a => a.IsActiveNow)
            .ThenByDescending(a => a.MostRecent)
            .ThenByDescending(a => a.LastLoginTime)
            .ToList();
    }
}
