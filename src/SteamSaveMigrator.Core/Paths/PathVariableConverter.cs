using System;
using System.IO;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Core.Paths;

/// <summary>
/// Responsável pela abstração, tokenização e conversão de caminhos entre perfis de usuários.
/// Implementa a conversão:
///   C:\Users\USUARIO_ANTIGO
///         ↓
///   %USERPROFILE%
///         ↓
///   C:\Users\NOVO_USUARIO
/// </summary>
public static class PathVariableConverter
{
    public const string TokenUserProfile = "%USERPROFILE%";
    public const string TokenAppData = "%APPDATA%";
    public const string TokenLocalAppData = "%LOCALAPPDATA%";
    public const string TokenAppDataLocalLow = "%USERPROFILE%\\AppData\\LocalLow";
    public const string TokenDocuments = "%DOCUMENTS%";
    public const string TokenSavedGames = "%SAVEDGAMES%";
    public const string TokenSteamPath = "%STEAM_PATH%";
    public const string TokenSteamUserdata = "%STEAM_USERDATA%";

    /// <summary>
    /// Converte um caminho absoluto do sistema em um caminho parametrizado/tokenizado.
    /// Exemplo: C:\Users\USUARIO_ANTIGO\AppData\Local\Game -> %LOCALAPPDATA%\Game
    /// </summary>
    public static string Tokenize(string fullPath, string sourceUserProfile, string? steamPath = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;

        var normalizedPath = Path.GetFullPath(fullPath);
        var sourceFolders = WindowsKnownFolders.GetFoldersForUserProfile(sourceUserProfile);

        // Verifica Steam Userdata
        if (!string.IsNullOrWhiteSpace(steamPath))
        {
            var normalizedSteamPath = Path.GetFullPath(steamPath);
            var userdataPath = Path.Combine(normalizedSteamPath, "userdata");

            if (normalizedPath.StartsWith(userdataPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedPath.Substring(userdataPath.Length).TrimStart('\\', '/');
                return Path.Combine(TokenSteamUserdata, relative);
            }

            if (normalizedPath.StartsWith(normalizedSteamPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedPath.Substring(normalizedSteamPath.Length).TrimStart('\\', '/');
                return Path.Combine(TokenSteamPath, relative);
            }
        }

        // Pastas conhecidas do perfil
        // ATENÇÃO: AppDataLocalLow DEVE ser verificado ANTES de AppDataLocal para evitar colisão de prefixo
        if (IsPathUnder(normalizedPath, sourceFolders.AppDataLocalLow))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.AppDataLocalLow);
            return Path.Combine(TokenAppDataLocalLow, relative);
        }

        if (IsPathUnder(normalizedPath, sourceFolders.AppDataLocal))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.AppDataLocal);
            return Path.Combine(TokenLocalAppData, relative);
        }

        if (IsPathUnder(normalizedPath, sourceFolders.AppDataRoaming))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.AppDataRoaming);
            return Path.Combine(TokenAppData, relative);
        }

        if (IsPathUnder(normalizedPath, sourceFolders.Documents))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.Documents);
            return Path.Combine(TokenDocuments, relative);
        }

        if (IsPathUnder(normalizedPath, sourceFolders.SavedGames))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.SavedGames);
            return Path.Combine(TokenSavedGames, relative);
        }

        if (IsPathUnder(normalizedPath, sourceFolders.UserProfile))
        {
            var relative = GetRelativeSubPath(normalizedPath, sourceFolders.UserProfile);
            return Path.Combine(TokenUserProfile, relative);
        }

        return normalizedPath;
    }

    private static bool IsPathUnder(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(basePath)) return false;
        var normPath = Path.GetFullPath(path).TrimEnd('\\', '/');
        var normBase = Path.GetFullPath(basePath).TrimEnd('\\', '/');
        return normPath.Equals(normBase, StringComparison.OrdinalIgnoreCase)
            || normPath.StartsWith(normBase + "\\", StringComparison.OrdinalIgnoreCase)
            || normPath.StartsWith(normBase + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativeSubPath(string path, string basePath)
    {
        var normPath = Path.GetFullPath(path);
        var normBase = Path.GetFullPath(basePath).TrimEnd('\\', '/');
        if (normPath.Length <= normBase.Length) return string.Empty;
        return normPath.Substring(normBase.Length).TrimStart('\\', '/');
    }

    /// <summary>
    /// Converte um caminho (seja tokenizado ou com caminho do usuário antigo) para o caminho real do novo usuário.
    /// Exemplo: %USERPROFILE%\AppData\Local\Game -> C:\Users\NOVO_USUARIO\AppData\Local\Game
    /// Exemplo: C:\Users\USUARIO_ANTIGO\Documents\Save -> C:\Users\NOVO_USUARIO\Documents\Save
    /// </summary>
    public static string Resolve(string path, PathConversionOptions options)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var targetFolders = !string.IsNullOrWhiteSpace(options.TargetUserProfile)
            ? WindowsKnownFolders.GetFoldersForUserProfile(options.TargetUserProfile)
            : WindowsKnownFolders.GetFoldersForUsername(options.TargetUsername);

        var result = path;

        // Se o caminho começa com tokens conhecidos
        if (result.StartsWith(TokenSteamUserdata, StringComparison.OrdinalIgnoreCase))
        {
            var steamDir = !string.IsNullOrWhiteSpace(options.TargetSteamPath)
                ? options.TargetSteamPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

            var userdataDir = Path.Combine(steamDir, "userdata");
            var relative = result.Substring(TokenSteamUserdata.Length).TrimStart('\\', '/');

            // Se quisermos trocar o SteamId3 de origem pelo de destino
            if (options.TargetSteamId3.HasValue)
            {
                var newIdStr = options.TargetSteamId3.Value.ToString();
                var slashIdx = relative.IndexOfAny(new[] { '\\', '/' });
                var firstSeg = slashIdx >= 0 ? relative.Substring(0, slashIdx) : relative;

                if (options.SourceSteamId3.HasValue && relative.StartsWith(options.SourceSteamId3.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    relative = newIdStr + relative.Substring(options.SourceSteamId3.Value.ToString().Length);
                }
                else if (uint.TryParse(firstSeg, out _))
                {
                    relative = newIdStr + (slashIdx >= 0 ? relative.Substring(slashIdx) : string.Empty);
                }
            }

            return Path.GetFullPath(Path.Combine(userdataDir, relative));
        }

        if (result.StartsWith(TokenSteamPath, StringComparison.OrdinalIgnoreCase))
        {
            var steamDir = !string.IsNullOrWhiteSpace(options.TargetSteamPath)
                ? options.TargetSteamPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

            var relative = result.Substring(TokenSteamPath.Length).TrimStart('\\', '/');
            result = Path.Combine(steamDir, relative);
        }
        else if (result.StartsWith(TokenLocalAppData, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenLocalAppData.Length).TrimStart('\\', '/');
            // Correção inteligente para backups legados que salvaram LocalLow como %LOCALAPPDATA%\Low\...
            if (relative.StartsWith("Low\\", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Low/", StringComparison.OrdinalIgnoreCase))
            {
                var correctedRelative = relative.Substring(4).TrimStart('\\', '/');
                result = Path.Combine(targetFolders.AppDataLocalLow, correctedRelative);
            }
            else if (string.Equals(relative, "Low", StringComparison.OrdinalIgnoreCase))
            {
                result = targetFolders.AppDataLocalLow;
            }
            else
            {
                result = Path.Combine(targetFolders.AppDataLocal, relative);
            }
        }
        else if (result.StartsWith(TokenAppData, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenAppData.Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.AppDataRoaming, relative);
        }
        else if (result.StartsWith(TokenAppDataLocalLow, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenAppDataLocalLow.Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.AppDataLocalLow, relative);
        }
        else if (result.StartsWith("%APPDATALOCALLOW%", StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring("%APPDATALOCALLOW%".Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.AppDataLocalLow, relative);
        }
        else if (result.StartsWith(TokenDocuments, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenDocuments.Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.Documents, relative);
        }
        else if (result.StartsWith(TokenSavedGames, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenSavedGames.Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.SavedGames, relative);
        }
        else if (result.StartsWith(TokenUserProfile, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result.Substring(TokenUserProfile.Length).TrimStart('\\', '/');
            result = Path.Combine(targetFolders.UserProfile, relative);
        }
        else if (!string.IsNullOrWhiteSpace(options.SourceUserProfile))
        {
            // Se o caminho não tinha token, mas é um caminho absoluto do usuário antigo (ex: C:\Users\USUARIO_ANTIGO\...)
            var normSourceProfile = Path.GetFullPath(options.SourceUserProfile.TrimEnd('\\', '/'));
            if (result.StartsWith(normSourceProfile, StringComparison.OrdinalIgnoreCase))
            {
                var relative = result.Substring(normSourceProfile.Length).TrimStart('\\', '/');
                result = Path.Combine(targetFolders.UserProfile, relative);
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.SourceUsername) && !string.IsNullOrWhiteSpace(options.TargetUsername))
        {
            // Fallback por substituição de nome de usuário caso o perfil antigo não tenha sido definido
            var oldUserToken1 = $"\\Users\\{options.SourceUsername}\\";
            var newUserToken1 = $"\\Users\\{options.TargetUsername}\\";
            if (result.Contains(oldUserToken1, StringComparison.OrdinalIgnoreCase))
            {
                int idx = result.IndexOf(oldUserToken1, StringComparison.OrdinalIgnoreCase);
                result = result.Substring(0, idx) + newUserToken1 + result.Substring(idx + oldUserToken1.Length);
            }
        }

        // Remapeamento genérico para qualquer outro usuário do Windows em caminhos absolutos
        if (!result.StartsWith(targetFolders.UserProfile, StringComparison.OrdinalIgnoreCase))
        {
            var usersIdx = result.IndexOf("\\Users\\", StringComparison.OrdinalIgnoreCase);
            if (usersIdx >= 0)
            {
                var userStart = usersIdx + "\\Users\\".Length;
                var nextSlash = result.IndexOfAny(new[] { '\\', '/' }, userStart);
                if (nextSlash > userStart)
                {
                    var oldUserInPath = result.Substring(userStart, nextSlash - userStart);
                    if (!string.Equals(oldUserInPath, options.TargetUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativeFromUser = result.Substring(nextSlash).TrimStart('\\', '/');
                        result = Path.Combine(targetFolders.UserProfile, relativeFromUser);
                    }
                }
            }
        }

        // Remapeia pastas de jogos com SteamID64 ou SteamID3 (ex: FromSoftware, Palworld, etc.)
        result = RemapSteamIdInPath(result, options);

        // Segurança final: Windows não possui pasta oficial "AppData\Local\Low", jogos Unity/outros usam "AppData\LocalLow"
        if (result.Contains("\\AppData\\Local\\Low\\", StringComparison.OrdinalIgnoreCase))
        {
            result = result.Replace("\\AppData\\Local\\Low\\", "\\AppData\\LocalLow\\", StringComparison.OrdinalIgnoreCase);
        }
        else if (result.EndsWith("\\AppData\\Local\\Low", StringComparison.OrdinalIgnoreCase))
        {
            result = result.Substring(0, result.Length - "\\AppData\\Local\\Low".Length) + "\\AppData\\LocalLow";
        }

        return Path.GetFullPath(result);
    }

    /// <summary>
    /// Converte um SteamID3 (ID de 32 bits de conta) em SteamID64 (ID de 64 bits da comunidade).
    /// </summary>
    public static ulong SteamId3ToSteamId64(uint steamId3) => 0x0110000100000000UL | steamId3;

    /// <summary>
    /// Converte um SteamID64 (ID de 64 bits da comunidade) em SteamID3 (ID de 32 bits de conta).
    /// </summary>
    public static uint SteamId64ToSteamId3(ulong steamId64) => (uint)(steamId64 & 0xFFFFFFFF);

    /// <summary>
    /// Remapeia segmentos de diretório que contenham SteamID3 ou SteamID64 de origem para destino.
    /// Exemplo: C:\Users\User\AppData\Roaming\EldenRing\76561198041671480\ER0000.sl2
    ///       -> C:\Users\User\AppData\Roaming\EldenRing\76561199839960293\ER0000.sl2
    /// </summary>
    public static string RemapSteamIdInPath(string path, PathConversionOptions options)
    {
        if (string.IsNullOrWhiteSpace(path) || !options.TargetSteamId3.HasValue)
            return path;

        var targetId3Str = options.TargetSteamId3.Value.ToString();
        var targetId64Str = ((ulong)0x0110000100000000UL | options.TargetSteamId3.Value).ToString();

        var sep = path.Contains('/') ? '/' : '\\';
        var segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.None);
        bool modified = false;

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];

            if (options.SourceSteamId3.HasValue)
            {
                var sourceId3Str = options.SourceSteamId3.Value.ToString();
                var sourceId64Str = ((ulong)0x0110000100000000UL | options.SourceSteamId3.Value).ToString();

                if (seg.Equals(sourceId64Str, StringComparison.OrdinalIgnoreCase))
                {
                    segments[i] = targetId64Str;
                    modified = true;
                    continue;
                }

                if (seg.Equals(sourceId3Str, StringComparison.OrdinalIgnoreCase))
                {
                    segments[i] = targetId3Str;
                    modified = true;
                    continue;
                }
            }
            else
            {
                // Se não temos SourceSteamId3 explícito, mas o segmento é um SteamID64 válido (17 dígitos começando com 7656119)
                if (seg.Length == 17 && seg.StartsWith("7656119", StringComparison.Ordinal) && ulong.TryParse(seg, out _))
                {
                    segments[i] = targetId64Str;
                    modified = true;
                    continue;
                }
            }
        }

        return modified ? string.Join(sep, segments) : path;
    }
}
