using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Gerencia o registro do aplicativo na inicialização do Windows (junto com a Steam / logon).
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultAppName = "SteamSaveMigrator";

    /// <summary>
    /// Verifica se o aplicativo está configurado para iniciar com o Windows.
    /// </summary>
    public static bool IsStartupEnabled(string appName = DefaultAppName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            if (key == null) return false;

            var val = key.GetValue(appName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Habilita ou desabilita a inicialização automática com o Windows.
    /// </summary>
    public static bool SetStartup(bool enable, string? executablePath = null, string arguments = "--minimized", string appName = DefaultAppName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return false;

            if (enable)
            {
                var exe = executablePath ?? Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    exe = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    return false;
                }

                var command = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{exe}\""
                    : $"\"{exe}\" {arguments.Trim()}";

                key.SetValue(appName, command);
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, false);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
