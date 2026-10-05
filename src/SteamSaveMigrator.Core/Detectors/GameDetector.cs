using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Parsers;

namespace SteamSaveMigrator.Core.Detectors;

/// <summary>
/// Responsável por detectar bibliotecas da Steam e todos os jogos instalados através dos arquivos ACF/VDF.
/// </summary>
public static class GameDetector
{
    private static readonly HashSet<uint> IgnoredAppIds = new()
    {
        228980, // Steamworks Common Redistributables
        1070560, // Steam Linux Runtime
        1391110, // Steam Linux Runtime - Soldier
        1628350, // Steam Linux Runtime - Sniper
        2180100  // Steam Linux Runtime - Medic
    };

    public static List<SteamLibraryFolder> DetectLibraries(string steamPath)
    {
        var libraries = new List<SteamLibraryFolder>();
        var candidates = new[]
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf")
        };

        string? vdfPath = candidates.FirstOrDefault(File.Exists);

        if (vdfPath != null)
        {
            try
            {
                var root = VdfParser.ParseFile(vdfPath);
                var libraryRoot = root["libraryfolders"] ?? root;

                foreach (var folderNode in libraryRoot.Children)
                {
                    if (int.TryParse(folderNode.Name, out var index))
                    {
                        var path = folderNode.GetString("path");
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            path = path.Replace(@"\\", @"\");
                            if (Directory.Exists(path))
                            {
                                var label = folderNode.GetString("label") ?? string.Empty;
                                var totalsize = folderNode.GetInt64("totalsize");

                                var appsDict = new Dictionary<uint, long>();
                                var appsNode = folderNode["apps"];
                                if (appsNode != null)
                                {
                                    foreach (var appNode in appsNode.Children)
                                    {
                                        if (uint.TryParse(appNode.Name, out var appId))
                                        {
                                            long.TryParse(appNode.Value, out var appSize);
                                            appsDict[appId] = appSize;
                                        }
                                    }
                                }

                                libraries.Add(new SteamLibraryFolder
                                {
                                    Index = index,
                                    Path = Path.GetFullPath(path),
                                    Label = label,
                                    TotalSizeBytes = totalsize,
                                    AppIdsWithSizes = appsDict
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Erro de leitura, faz fallback para a pasta principal
            }
        }

        // Se nenhuma biblioteca foi encontrada, adiciona o próprio diretório Steam
        if (libraries.Count == 0 && Directory.Exists(steamPath))
        {
            libraries.Add(new SteamLibraryFolder
            {
                Index = 0,
                Path = Path.GetFullPath(steamPath),
                Label = "Steam Principal",
                TotalSizeBytes = 0
            });
        }

        return libraries;
    }

    public static List<SteamGame> DetectInstalledGames(SteamInstallationInfo steamInfo, bool includeRedistributables = false)
    {
        var games = new List<SteamGame>();
        var seenAppIds = new HashSet<uint>();

        var libraries = steamInfo.Libraries.Count > 0
            ? steamInfo.Libraries
            : DetectLibraries(steamInfo.SteamPath);

        foreach (var library in libraries)
        {
            var steamAppsDir = library.SteamAppsPath;
            if (!Directory.Exists(steamAppsDir)) continue;

            var manifestFiles = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");

            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    var root = VdfParser.ParseFile(manifestFile);
                    var appState = root["AppState"] ?? root;

                    var appId = appState.GetUInt32("appid");
                    if (appId == 0) continue;

                    if (!includeRedistributables && IgnoredAppIds.Contains(appId))
                        continue;

                    if (seenAppIds.Contains(appId))
                        continue;

                    var name = appState.GetString("name") ?? $"Jogo ({appId})";
                    var installDir = appState.GetString("installdir") ?? string.Empty;
                    var sizeOnDisk = appState.GetInt64("SizeOnDisk");
                    var lastUpdatedTs = appState.GetInt64("LastUpdated");
                    DateTime? lastUpdated = lastUpdatedTs > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastUpdatedTs).LocalDateTime : null;

                    var fullInstallPath = !string.IsNullOrWhiteSpace(installDir)
                        ? Path.Combine(library.CommonPath, installDir)
                        : string.Empty;

                    var game = new SteamGame
                    {
                        AppId = appId,
                        Name = name,
                        InstallDir = installDir,
                        InstallPath = fullInstallPath,
                        ManifestPath = manifestFile,
                        LibraryPath = library.Path,
                        SizeOnDisk = sizeOnDisk,
                        LastUpdated = lastUpdated
                    };

                    games.Add(game);
                    seenAppIds.Add(appId);
                }
                catch
                {
                    // Erro ao ler manifest individual, ignora e segue para os demais
                }
            }
        }

        return games.OrderBy(g => g.Name).ToList();
    }
}
