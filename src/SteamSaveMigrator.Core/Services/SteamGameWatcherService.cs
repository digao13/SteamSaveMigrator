using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Monitor em segundo plano que vigia jogos da Steam em execução e dispara backups automáticos
/// (locais e na nuvem Google Drive) assim que o jogo é fechado.
/// </summary>
[SupportedOSPlatform("windows")]
public class SteamGameWatcherService : IDisposable
{
    private readonly SteamSaveMigratorEngine _engine;
    private readonly IGoogleDriveService _driveService;
    private readonly GameWatcherConfig _config;

    private readonly HashSet<uint> _currentlyRunningAppIds = new();
    private CancellationTokenSource? _watcherCts;
    private Task? _watcherTask;
    private bool _isDisposed;

    public event Action<GameWatcherEvent>? OnWatcherEvent;

    public bool IsWatching => _watcherTask != null && !_watcherTask.IsCompleted;
    public IReadOnlyCollection<uint> RunningAppIds => _currentlyRunningAppIds.ToList();
    public GameWatcherConfig Config => _config;

    public SteamGameWatcherService(
        SteamSaveMigratorEngine engine,
        IGoogleDriveService driveService,
        GameWatcherConfig? config = null)
    {
        _engine = engine;
        _driveService = driveService;
        _config = config ?? new GameWatcherConfig();

        if (string.IsNullOrWhiteSpace(_config.LocalBackupOutputDir))
        {
            _config.LocalBackupOutputDir = Path.Combine(Environment.CurrentDirectory, "backups");
        }
    }

    /// <summary>
    /// Atualiza em tempo de execução o diretório de destino onde os backups automáticos são gravados.
    /// </summary>
    public void UpdateOutputDir(string newOutputDir)
    {
        if (string.IsNullOrWhiteSpace(newOutputDir)) return;

        _config.LocalBackupOutputDir = newOutputDir;
        try
        {
            if (!Directory.Exists(newOutputDir))
            {
                Directory.CreateDirectory(newOutputDir);
            }
        }
        catch { }
    }

    /// <summary>
    /// Inicia o monitoramento em segundo plano.
    /// </summary>
    public void Start()
    {
        if (IsWatching) return;

        _watcherCts = new CancellationTokenSource();
        _watcherTask = Task.Run(() => WatcherLoopAsync(_watcherCts.Token));
    }

    /// <summary>
    /// Interrompe o monitoramento em segundo plano.
    /// </summary>
    public async Task StopAsync()
    {
        if (_watcherCts != null)
        {
            _watcherCts.Cancel();
            if (_watcherTask != null)
            {
                try
                {
                    await _watcherTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado
                }
            }
            _watcherCts.Dispose();
            _watcherCts = null;
            _watcherTask = null;
        }

        _currentlyRunningAppIds.Clear();
    }

    private async Task WatcherLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var installedGames = _engine.GetInstalledGames();
                var activeAppIds = DetectRunningAppIds(installedGames);

                // Detecta jogos que acabaram de abrir
                foreach (var appId in activeAppIds)
                {
                    if (!_currentlyRunningAppIds.Contains(appId))
                    {
                        _currentlyRunningAppIds.Add(appId);
                        var game = installedGames.FirstOrDefault(g => g.AppId == appId);
                        if (game != null)
                        {
                            Notify(new GameWatcherEvent
                            {
                                Type = GameWatcherEventType.GameStarted,
                                Game = game,
                                Message = $"Jogo iniciado: {game.Name} (AppID: {game.AppId})"
                            });
                        }
                    }
                }

                // Detecta jogos que fecharam
                var closedAppIds = _currentlyRunningAppIds.Where(id => !activeAppIds.Contains(id)).ToList();
                foreach (var closedAppId in closedAppIds)
                {
                    _currentlyRunningAppIds.Remove(closedAppId);
                    var game = installedGames.FirstOrDefault(g => g.AppId == closedAppId);
                    if (game != null)
                    {
                        Notify(new GameWatcherEvent
                        {
                            Type = GameWatcherEventType.GameExited,
                            Game = game,
                            Message = $"Jogo fechado: {game.Name} (AppID: {game.AppId})"
                        });

                        // Se configurado para backup automático ao fechar
                        if (_config.AutoBackupOnGameExit)
                        {
                            _ = Task.Run(() => HandleGameExitedBackupAsync(game, cancellationToken), cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Erro no ciclo de polling
                Debug.WriteLine($"Erro no WatcherLoop: {ex.Message}");
            }

            var delayMs = Math.Max(1000, _config.PollIntervalSeconds * 1000);
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    internal async Task HandleGameExitedBackupAsync(SteamGame game, CancellationToken cancellationToken = default)
    {
        try
        {
            // Aguarda alguns segundos para o jogo terminar de gravar os saves no disco
            if (_config.PostExitDelaySeconds > 0)
            {
                await Task.Delay(_config.PostExitDelaySeconds * 1000, cancellationToken);
            }

            Notify(new GameWatcherEvent
            {
                Type = GameWatcherEventType.BackupStarted,
                Game = game,
                Message = $"Iniciando backup automático de {game.Name}..."
            });

            // Escaneia os saves mais recentes do jogo
            var scannedGame = _engine.ScanSavesForGame(game);
            if (!scannedGame.HasSaves && game.HasSaves)
            {
                scannedGame = game;
            }

            if (!scannedGame.HasSaves)
            {
                Notify(new GameWatcherEvent
                {
                    Type = GameWatcherEventType.BackupCompleted,
                    Game = game,
                    Message = $"Nenhum save local encontrado para {game.Name}.",
                    Success = false
                });
                return;
            }

            if (!Directory.Exists(_config.LocalBackupOutputDir))
            {
                Directory.CreateDirectory(_config.LocalBackupOutputDir);
            }

            var sanitizedGameName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var zipFileName = $"{sanitizedGameName}_{game.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var localZipPath = Path.Combine(_config.LocalBackupOutputDir, zipFileName);

            // Cria o backup local (.zip)
            var generatedZip = await _engine.BackupGameAsync(
                scannedGame,
                localZipPath,
                cancellationToken: cancellationToken);

            Notify(new GameWatcherEvent
            {
                Type = GameWatcherEventType.BackupCompleted,
                Game = game,
                LocalZipPath = generatedZip,
                Message = $"Backup local salvo: {Path.GetFileName(generatedZip)}"
            });

            // Envia para o Google Drive se configurado e autenticado
            if (_config.AutoUploadToGoogleDrive)
            {
                var isAuth = await _driveService.IsAuthenticatedAsync(cancellationToken);
                if (isAuth)
                {
                    Notify(new GameWatcherEvent
                    {
                        Type = GameWatcherEventType.CloudUploadStarted,
                        Game = game,
                        LocalZipPath = generatedZip,
                        Message = $"Fazendo upload do save de {game.Name} para o Google Drive..."
                    });

                    var manifest = await _engine.ReadBackupManifestAsync(generatedZip, cancellationToken);
                    var persona = manifest?.SourceSteamPersonaName ?? _engine.SteamInfo.ActiveAccount?.PersonaName;
                    var id3 = manifest?.SourceSteamId3 ?? _engine.SteamInfo.ActiveAccount?.SteamId3;
                    var id64 = manifest?.SourceSteamId64 ?? _engine.SteamInfo.ActiveAccount?.SteamId64;
                    var user = manifest?.SourceUsername ?? Environment.UserName;

                    var cloudBackup = await _driveService.UploadBackupAsync(
                        generatedZip,
                        game.Name,
                        game.AppId,
                        sourceSteamPersona: persona,
                        sourceSteamId3: id3,
                        sourceSteamId64: id64,
                        sourceUsername: user,
                        cancellationToken: cancellationToken);

                    Notify(new GameWatcherEvent
                    {
                        Type = GameWatcherEventType.CloudUploadCompleted,
                        Game = game,
                        LocalZipPath = generatedZip,
                        CloudBackup = cloudBackup,
                        Message = $"✅ Backup de {game.Name} salvo no Google Drive com sucesso!"
                    });
                }
                else
                {
                    Notify(new GameWatcherEvent
                    {
                        Type = GameWatcherEventType.Error,
                        Game = game,
                        Message = "Google Drive não autenticado. O backup foi salvo localmente.",
                        Success = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Notify(new GameWatcherEvent
            {
                Type = GameWatcherEventType.Error,
                Game = game,
                Message = $"Erro no backup automático pós-fechamento: {ex.Message}",
                Success = false
            });
        }
    }

    /// <summary>
    /// Detecta os AppIDs dos jogos em execução utilizando registro da Steam e processos do sistema.
    /// </summary>
    public HashSet<uint> DetectRunningAppIds(List<SteamGame> installedGames)
    {
        var result = new HashSet<uint>();

        // 1. Método via Registro do Windows (HKCU\Software\Valve\Steam\Apps)
        try
        {
            using var appsKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps");
            if (appsKey != null)
            {
                foreach (var subKeyName in appsKey.GetSubKeyNames())
                {
                    if (uint.TryParse(subKeyName, out var appId))
                    {
                        using var appKey = appsKey.OpenSubKey(subKeyName);
                        var runningVal = appKey?.GetValue("Running");
                        if (runningVal is int intVal && intVal == 1)
                        {
                            result.Add(appId);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        // 2. Método complementar: checagem de processos com executáveis dentro da pasta dos jogos
        try
        {
            var processes = Process.GetProcesses();
            var gameInstallDirs = installedGames
                .Where(g => !string.IsNullOrWhiteSpace(g.InstallDir) && Directory.Exists(g.InstallDir))
                .Select(g => new { g.AppId, InstallDir = g.InstallDir.TrimEnd('\\', '/') + "\\" })
                .ToList();

            foreach (var proc in processes)
            {
                try
                {
                    // Alguns processos do sistema recusam acesso ao MainModule
                    string? modulePath = null;
                    try
                    {
                        modulePath = proc.MainModule?.FileName;
                    }
                    catch
                    {
                        // Acesso negado para processos do sistema
                    }

                    if (!string.IsNullOrWhiteSpace(modulePath))
                    {
                        foreach (var g in gameInstallDirs)
                        {
                            if (modulePath.StartsWith(g.InstallDir, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(g.AppId);
                            }
                        }
                    }
                }
                catch
                {
                    // Processo finalizado ou inacessível
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return result;
    }

    private void Notify(GameWatcherEvent ev)
    {
        OnWatcherEvent?.Invoke(ev);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _watcherCts?.Cancel();
        _watcherCts?.Dispose();
    }
}
