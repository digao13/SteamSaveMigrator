using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using SteamSaveMigrator.Core;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using SteamSaveMigrator.Core.Services;

namespace SteamSaveMigrator.Cli;

public static class CliCommands
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var menu = new InteractiveMenu();
            await menu.RunAsync();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var engine = new SteamSaveMigratorEngine();
        engine.InitializeSteam();

        switch (command)
        {
            case "detect":
            case "status":
            case "info":
                ConsoleUi.ShowBanner();
                ConsoleUi.DisplaySteamInfo(engine.SteamInfo);
                return 0;

            case "list":
            case "list-games":
            case "games":
                ConsoleUi.ShowBanner();
                var games = engine.GetInstalledGames();
                ConsoleUi.DisplayGamesTable(games);
                return 0;

            case "scan":
                ConsoleUi.ShowBanner();
                var scanAppId = GetArgValue(args, "--appid");
                if (!string.IsNullOrWhiteSpace(scanAppId) && uint.TryParse(scanAppId, out var appIdVal))
                {
                    var g = engine.GetInstalledGames().FirstOrDefault(x => x.AppId == appIdVal);
                    if (g == null)
                    {
                        AnsiConsole.MarkupLine($"[red]Jogo com AppID {appIdVal} não encontrado.[/]");
                        return 1;
                    }
                    var scanned = engine.ScanSavesForGame(g);
                    ConsoleUi.DisplaySaveLocations(scanned);
                }
                else
                {
                    var allGames = engine.GetInstalledGames();
                    foreach (var g in allGames)
                    {
                        var scanned = engine.ScanSavesForGame(g);
                        if (scanned.HasSaves)
                        {
                            ConsoleUi.DisplaySaveLocations(scanned);
                        }
                    }
                }
                return 0;

            case "backup":
            case "backup-all":
                return await HandleBackupCommand(args, engine);

            case "restore":
            case "restore-auto":
                return await HandleRestoreCommand(args, engine);

            case "cloud":
            case "drive":
            case "gdrive":
                return await HandleCloudCommand(args, engine);

            case "watch":
            case "monitor":
                return await HandleWatchCommand(args, engine);

            case "help":
            case "--help":
            case "-h":
                ShowHelp();
                return 0;

            default:
                AnsiConsole.MarkupLine($"[red]Comando desconhecido:[/] {command}");
                ShowHelp();
                return 1;
        }
    }

    private static async Task<int> HandleBackupCommand(string[] args, SteamSaveMigratorEngine engine)
    {
        bool isAll = args.Contains("--all") || args[0].Equals("backup-all", StringComparison.OrdinalIgnoreCase);

        if (isAll)
        {
            var defaultBackupDir = GetArgValue(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "backups");
            AnsiConsole.MarkupLine("[bold cyan]Iniciando escaneamento de todos os jogos para backup em lote...[/]");

            var allGames = engine.GetInstalledGames();
            var gamesWithSaves = new List<SteamGame>();

            foreach (var g in allGames)
            {
                var scanned = engine.ScanSavesForGame(g);
                if (scanned.HasSaves)
                {
                    gamesWithSaves.Add(scanned);
                }
            }

            if (gamesWithSaves.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Nenhum jogo com saves locais foi detectado.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Iniciando backup de {gamesWithSaves.Count} jogos selecionados...[/]");
            var batchResult = await engine.BackupMultipleGamesAsync(gamesWithSaves, defaultBackupDir);

            AnsiConsole.MarkupLine($"\n[bold green]🎉 Backup em lote concluído com sucesso![/]");
            AnsiConsole.MarkupLine($"[white]Total de jogos arquivados:[/] [yellow]{batchResult.SuccessCount}[/]");
            AnsiConsole.MarkupLine($"[white]Pasta de saída:[/] [cyan]{defaultBackupDir}[/]");
            return 0;
        }

        var appIdStr = GetArgValue(args, "--appid");
        if (string.IsNullOrWhiteSpace(appIdStr) || !uint.TryParse(appIdStr, out var appId))
        {
            AnsiConsole.MarkupLine("[red]Erro: Informe --appid <id> ou use --all para fazer backup de todos os jogos de uma vez.[/]");
            return 1;
        }

        var game = engine.GetInstalledGames().FirstOrDefault(g => g.AppId == appId);
        if (game == null)
        {
            AnsiConsole.MarkupLine($"[red]Jogo com AppID {appId} não foi encontrado na biblioteca Steam.[/]");
            return 1;
        }

        var outZip = GetArgValue(args, "--out");
        if (string.IsNullOrWhiteSpace(outZip))
        {
            var defaultBackupDir = Path.Combine(Environment.CurrentDirectory, "backups");
            var sanitizedName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            outZip = Path.Combine(defaultBackupDir, $"{sanitizedName}_{game.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        }

        var userProfile = GetArgValue(args, "--userprofile");

        AnsiConsole.MarkupLine($"[cyan]Iniciando backup de:[/] [bold white]{game.Name}[/] (AppID: {game.AppId})...");
        var resultZip = await engine.BackupGameAsync(game, outZip, userProfile);

        AnsiConsole.MarkupLine($"[bold green]✅ Backup concluído:[/] [white]{resultZip}[/]");
        return 0;
    }

    private static async Task<int> HandleRestoreCommand(string[] args, SteamSaveMigratorEngine engine)
    {
        var file = GetArgValue(args, "--file");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            AnsiConsole.MarkupLine("[red]Erro: O parâmetro --file <caminho.zip> é obrigatório e o arquivo deve existir.[/]");
            return 1;
        }

        var isDryRun = args.Contains("--dry-run");
        var targetUser = GetArgValue(args, "--user");
        var steamIdArg = GetArgValue(args, "--steam-id") ?? GetArgValue(args, "--steamid") ?? GetArgValue(args, "--target-steam-id");
        uint? explicitSteamId = null;
        if (!string.IsNullOrWhiteSpace(steamIdArg) && uint.TryParse(steamIdArg, out var sIdVal))
        {
            explicitSteamId = sIdVal;
        }

        var manifest = await engine.ReadBackupManifestAsync(file);
        if (manifest == null)
        {
            AnsiConsole.MarkupLine("[red]Erro: Não foi possível ler o manifesto do arquivo de backup.[/]");
            return 1;
        }

        // Identifica conta Steam de destino (explicitamente passada ou conectada no momento)
        var targetSteamAccount = explicitSteamId.HasValue
            ? engine.SteamInfo.Accounts.FirstOrDefault(a => a.SteamId3 == explicitSteamId.Value) ?? new SteamUserAccount { SteamId3 = explicitSteamId.Value, PersonaName = $"Steam User {explicitSteamId.Value}" }
            : engine.SteamInfo.ActiveAccount;

        var sourceSteamDisplay = !string.IsNullOrWhiteSpace(manifest.SourceSteamPersonaName)
            ? $"{manifest.SourceSteamPersonaName} (ID3: {manifest.SourceSteamId3})"
            : (manifest.SourceSteamId3.HasValue ? $"ID3: {manifest.SourceSteamId3}" : "Não especificado no backup");

        // Se NÃO informou --user, ativa a RESTAURAÇÃO AUTOMÁTICA TRANSPARENTE!
        if (string.IsNullOrWhiteSpace(targetUser))
        {
            var currentUsername = Environment.UserName;
            var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AnsiConsole.MarkupLine("\n[bold green]✨ Restauração 100% Automática Detectada (Windows + Steam)[/]");
            AnsiConsole.MarkupLine($"  [bold]• Origem Windows:[/] [yellow]{manifest.SourceUsername}[/] ([grey]{manifest.SourceUserProfile}[/])");
            AnsiConsole.MarkupLine($"  [bold]• Destino Windows:[/] [green]{currentUsername}[/] ([grey]{currentUserProfile}[/])");

            if (targetSteamAccount != null)
            {
                AnsiConsole.MarkupLine($"  [bold]• Origem Steam:[/] [yellow]{sourceSteamDisplay}[/]");
                AnsiConsole.MarkupLine($"  [bold]• Destino Steam:[/] [bold green]{targetSteamAccount.PersonaName}[/] (ID3: [green]{targetSteamAccount.SteamId3}[/]) {(targetSteamAccount.IsActiveNow ? "[bold cyan][[CONECTADO ATUALMENTE]][/]" : "")}");
            }

            AnsiConsole.MarkupLine("[grey]Convertendo e ajustando todos os caminhos para o usuário e perfil Steam atuais deste computador...[/]\n");

            var autoResult = await engine.AutoRestoreAsync(
                file,
                targetSteamId3: targetSteamAccount?.SteamId3,
                overwriteExisting: true,
                dryRun: isDryRun);

            if (autoResult.IsSuccess)
            {
                var steamMigrationMsg = autoResult.SteamProfileMigrated
                    ? $" e dados remapeados para a conta Steam '{targetSteamAccount?.PersonaName ?? targetSteamAccount?.SteamId3.ToString()}'"
                    : "";

                AnsiConsole.MarkupLine(isDryRun
                    ? $"[bold yellow]🔍 Simulação concluída: {autoResult.SuccessCount} arquivo(s) mapeados com sucesso![/]"
                    : $"[bold green]🎉 Sucesso total! {autoResult.SuccessCount} arquivo(s) de save restaurados no perfil '{currentUsername}'{steamMigrationMsg} sem complicações![/]");
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]❌ Falha ao restaurar: {autoResult.ErrorCount} erro(s).[/]");
                return 1;
            }
        }

        var targetProfile = GetArgValue(args, "--targetprofile") ?? WindowsKnownFolders.GetFoldersForUsername(targetUser).UserProfile;

        var options = new PathConversionOptions
        {
            TargetUsername = targetUser,
            TargetUserProfile = targetProfile,
            TargetSteamId3 = targetSteamAccount?.SteamId3,
            TargetSteamPersonaName = targetSteamAccount?.PersonaName,
            DryRun = isDryRun,
            OverwriteExisting = true
        };

        AnsiConsole.MarkupLine($"[cyan]Restaurando backup:[/] [white]{file}[/]");
        AnsiConsole.MarkupLine($"[cyan]Destino Windows:[/] Usuário [green]{targetUser}[/] ({targetProfile}) {(isDryRun ? "[yellow](SIMULAÇÃO)[/]" : "")}");
        if (targetSteamAccount != null)
        {
            AnsiConsole.MarkupLine($"[cyan]Destino Steam:[/] [green]{targetSteamAccount.PersonaName}[/] (ID3: {targetSteamAccount.SteamId3})");
        }

        var result = await engine.RestoreBackupAsync(file, options);

        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[bold green]✅ Sucesso: {result.SuccessCount} arquivo(s) processados com sucesso![/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]❌ Falha ao restaurar: {result.ErrorCount} erro(s) encontrados.[/]");
            return 1;
        }
    }

    private static async Task<int> HandleCloudCommand(string[] args, SteamSaveMigratorEngine engine)
    {
        ConsoleUi.ShowBanner();
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";

        switch (sub)
        {
            case "status":
            {
                AnsiConsole.MarkupLine("[bold cyan]=== Status do Google Drive ===[/]");
                AnsiConsole.MarkupLine($"  [bold]• Credenciais OAuth:[/] {(engine.GoogleDrive.HasConfiguredCredentials ? "[green]Configuradas[/]" : "[yellow]Não configuradas (Execute 'steamsavemigrator cloud config')[/]")}");
                if (engine.GoogleDrive.HasConfiguredCredentials && !string.IsNullOrWhiteSpace(engine.GoogleDrive.CurrentClientId))
                {
                    AnsiConsole.MarkupLine($"  [bold]• Client ID:[/] [grey]{engine.GoogleDrive.CurrentClientId}[/]");
                }

                var isAuth = await engine.GoogleDrive.IsAuthenticatedAsync();
                if (isAuth)
                {
                    var user = await engine.GoogleDrive.GetUserEmailAsync();
                    AnsiConsole.MarkupLine($"  [bold]• Status:[/] [bold green]Conectado[/]");
                    AnsiConsole.MarkupLine($"  [bold]• Conta:[/] [yellow]{user ?? "Autenticado"}[/]");
                    var backups = await engine.GoogleDrive.ListBackupsAsync();
                    AnsiConsole.MarkupLine($"  [bold]• Backups na Nuvem:[/] [cyan]{backups.Count}[/] arquivo(s)");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [bold]• Status:[/] [bold yellow]Não conectado[/]");
                    AnsiConsole.MarkupLine("  [grey]Execute [cyan]steamsavemigrator cloud login[/] para autenticar com sua conta Google.[/]");
                }
                return 0;
            }

            case "config":
            {
                var jsonPath = GetArgValue(args, "--json") ?? GetArgValue(args, "--file");
                var clientId = GetArgValue(args, "--client-id") ?? GetArgValue(args, "--id");
                var clientSecret = GetArgValue(args, "--client-secret") ?? GetArgValue(args, "--secret");

                if (!string.IsNullOrWhiteSpace(jsonPath))
                {
                    if (!File.Exists(jsonPath))
                    {
                        AnsiConsole.MarkupLine($"[bold red]❌ Arquivo não encontrado:[/] [white]{jsonPath}[/]");
                        return 1;
                    }

                    if (engine.GoogleDrive.ConfigureCredentialsFromJson(jsonPath))
                    {
                        AnsiConsole.MarkupLine("[bold green]✅ Credenciais importadas com sucesso a partir do arquivo JSON![/]");
                        AnsiConsole.MarkupLine($"  [bold]• Client ID:[/] [cyan]{engine.GoogleDrive.CurrentClientId}[/]");
                        AnsiConsole.MarkupLine("Agora você pode rodar [bold cyan]steamsavemigrator cloud login[/] para autenticar.");
                        return 0;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]❌ Não foi possível extrair 'client_id' e 'client_secret' do JSON fornecido.[/]");
                        return 1;
                    }
                }

                if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                {
                    engine.GoogleDrive.ConfigureClientSecrets(clientId, clientSecret);
                    AnsiConsole.MarkupLine("[bold green]✅ Credenciais configuradas com sucesso![/]");
                    AnsiConsole.MarkupLine($"  [bold]• Client ID:[/] [cyan]{clientId}[/]");
                    AnsiConsole.MarkupLine("Agora você pode rodar [bold cyan]steamsavemigrator cloud login[/] para autenticar.");
                    return 0;
                }

                // Modo interativo se não passou argumentos
                AnsiConsole.MarkupLine("[bold cyan]Configuração de Credenciais OAuth do Google Drive[/]");
                AnsiConsole.MarkupLine("[grey]Para usar o Google Drive, o Google exige um aplicativo de Desktop criado no Google Cloud Console.[/]\n");

                var mode = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Como deseja configurar as credenciais?")
                        .AddChoices("📁 Importar arquivo credentials.json", "⌨️ Inserir Client ID e Client Secret manualmente", "Cancelar"));

                if (mode.StartsWith("📁"))
                {
                    var path = AnsiConsole.Ask<string>("Caminho do arquivo [bold]credentials.json[/]:");
                    if (!File.Exists(path))
                    {
                        AnsiConsole.MarkupLine("[bold red]❌ Arquivo não encontrado.[/]");
                        return 1;
                    }

                    if (engine.GoogleDrive.ConfigureCredentialsFromJson(path))
                    {
                        AnsiConsole.MarkupLine("[bold green]✅ Credenciais importadas com sucesso![/]");
                        AnsiConsole.MarkupLine("Execute [cyan]steamsavemigrator cloud login[/] para conectar.");
                        return 0;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]❌ Falha ao processar o arquivo JSON.[/]");
                        return 1;
                    }
                }
                else if (mode.StartsWith("⌨️"))
                {
                    var id = AnsiConsole.Ask<string>("Client ID:");
                    var secret = AnsiConsole.Prompt(new TextPrompt<string>("Client Secret:").Secret());
                    engine.GoogleDrive.ConfigureClientSecrets(id, secret);
                    AnsiConsole.MarkupLine("[bold green]✅ Credenciais salvas com sucesso![/]");
                    AnsiConsole.MarkupLine("Execute [cyan]steamsavemigrator cloud login[/] para conectar.");
                    return 0;
                }

                return 0;
            }

            case "login":
            {
                if (!engine.GoogleDrive.HasConfiguredCredentials)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Credenciais OAuth do Google ainda não configuradas.[/]");
                    AnsiConsole.MarkupLine("O Google exige um client_id gerado no Google Cloud Console para autorizar o acesso.");
                    AnsiConsole.MarkupLine("Execute [bold cyan]steamsavemigrator cloud config[/] para importar seu arquivo credentials.json ou inserir as chaves.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[bold cyan]Iniciando autenticação com o Google Drive...[/]");
                AnsiConsole.MarkupLine("[grey]Uma janela do seu navegador será aberta para autorizar o acesso aos backups.[/]");
                
                try
                {
                    var success = await engine.GoogleDrive.AuthenticateAsync();
                    if (success)
                    {
                        var email = await engine.GoogleDrive.GetUserEmailAsync();
                        AnsiConsole.MarkupLine($"\n[bold green]✅ Autenticado com sucesso no Google Drive![/]");
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            AnsiConsole.MarkupLine($"  [bold]Conta:[/] [cyan]{email}[/]");
                        }
                        return 0;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("\n[bold red]❌ Falha na autenticação ou processo cancelado pelo usuário.[/]");
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[bold red]❌ Erro ao autenticar no Google Drive:[/] {Markup.Escape(ex.Message)}");
                    if (ex.Message.Contains("401") || ex.Message.Contains("invalid_client"))
                    {
                        AnsiConsole.MarkupLine("\n[bold yellow]Dica:[/] O erro 401 (invalid_client) indica que o Client ID/Secret não existe no Google Cloud.");
                        AnsiConsole.MarkupLine("Certifique-se de criar credenciais do tipo 'Aplicativo para Computador / Desktop' no Google Cloud Console e rodar:");
                        AnsiConsole.MarkupLine("  [cyan]steamsavemigrator cloud config --json credentials.json[/]");
                    }
                    return 1;
                }
            }

            case "logout":
            {
                await engine.GoogleDrive.SignOutAsync();
                AnsiConsole.MarkupLine("[bold green]✅ Sessão do Google Drive encerrada e credenciais locais removidas com sucesso.[/]");
                return 0;
            }

            case "list":
            case "ls":
            {
                var isAuth = await engine.GoogleDrive.IsAuthenticatedAsync();
                if (!isAuth)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Você precisa estar conectado ao Google Drive.[/]");
                    AnsiConsole.MarkupLine("Execute [cyan]steamsavemigrator cloud login[/] para conectar.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[bold cyan]Buscando backups salvos na pasta 'SteamSaveMigrator_Backups' no Google Drive...[/]");
                var backups = await engine.GoogleDrive.ListBackupsAsync();

                if (backups.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]Nenhum backup encontrado na nuvem.[/]");
                    return 0;
                }

                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("[bold]ID na Nuvem[/]");
                table.AddColumn("[bold]Arquivo[/]");
                table.AddColumn("[bold]Jogo[/]");
                table.AddColumn("[bold]AppID[/]");
                table.AddColumn("[bold]Tamanho[/]");
                table.AddColumn("[bold]Data[/]");

                foreach (var b in backups)
                {
                    table.AddRow(
                        $"[grey]{b.FileId}[/]",
                        $"[white]{Markup.Escape(b.FileName)}[/]",
                        $"[cyan]{Markup.Escape(b.GameName)}[/]",
                        b.AppId > 0 ? $"[yellow]{b.AppId}[/]" : "[grey]-[/]",
                        $"[green]{b.FormattedSize}[/]",
                        b.CreatedTime.HasValue ? b.CreatedTime.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "-"
                    );
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[grey]Total de backups na nuvem: {backups.Count}[/]");
                return 0;
            }

            case "upload":
            {
                var filePath = GetArgValue(args, "--file");
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    AnsiConsole.MarkupLine("[bold red]Especifique um arquivo válido usando: --file <caminho.zip>[/]");
                    return 1;
                }

                string gameName = Path.GetFileNameWithoutExtension(filePath);
                uint appId = 0;
                try
                {
                    var manifest = await engine.ReadBackupManifestAsync(filePath);
                    if (manifest != null)
                    {
                        if (!string.IsNullOrWhiteSpace(manifest.GameName)) gameName = manifest.GameName;
                        appId = manifest.AppId;
                    }
                }
                catch
                {
                    // Usa defaults do nome do arquivo
                }

                AnsiConsole.MarkupLine($"[bold cyan]Fazendo upload de[/] [white]{filePath}[/] [bold cyan]para o Google Drive...[/]");
                var uploaded = await engine.GoogleDrive.UploadBackupAsync(filePath, gameName, appId);
                if (uploaded != null)
                {
                    AnsiConsole.MarkupLine($"[bold green]✅ Upload concluído com sucesso![/]");
                    AnsiConsole.MarkupLine($"  [bold]• ID:[/] [cyan]{uploaded.FileId}[/]");
                    AnsiConsole.MarkupLine($"  [bold]• Arquivo:[/] [white]{uploaded.FileName}[/]");
                    AnsiConsole.MarkupLine($"  [bold]• Tamanho:[/] [green]{uploaded.FormattedSize}[/]");
                    return 0;
                }
                else
                {
                    AnsiConsole.MarkupLine("[bold red]❌ Falha ao realizar upload para o Google Drive.[/]");
                    return 1;
                }
            }

            case "restore":
            {
                var fileId = GetArgValue(args, "--id");
                if (string.IsNullOrWhiteSpace(fileId))
                {
                    AnsiConsole.MarkupLine("[bold red]Especifique o ID do arquivo na nuvem usando: --id <cloudFileId>[/]");
                    AnsiConsole.MarkupLine("Dica: Use [cyan]steamsavemigrator cloud list[/] para ver os IDs disponíveis.");
                    return 1;
                }

                var targetSteamIdStr = GetArgValue(args, "--steam-id");
                uint? targetSteamId = null;
                if (!string.IsNullOrWhiteSpace(targetSteamIdStr) && uint.TryParse(targetSteamIdStr, out var parsedSteamId))
                {
                    targetSteamId = parsedSteamId;
                }

                var isDryRun = args.Contains("--dry-run");

                AnsiConsole.MarkupLine($"[bold cyan]Iniciando download e restauração do backup ID: {fileId}[/]");
                var res = await engine.RestoreCloudBackupAsync(
                    fileId,
                    targetSteamId3: targetSteamId,
                    overwriteExisting: true,
                    dryRun: isDryRun);

                if (res.IsSuccess)
                {
                    AnsiConsole.MarkupLine(isDryRun
                        ? $"[bold yellow]🔍 Simulação de restauração concluída: {res.SuccessCount} arquivo(s) mapeados com sucesso![/]"
                        : $"[bold green]🎉 Sucesso total! {res.SuccessCount} arquivo(s) de save baixados da nuvem e restaurados com sucesso![/]");
                    return 0;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[bold red]❌ Erro na restauração da nuvem: {res.ErrorCount} erro(s).[/]");
                    return 1;
                }
            }

            default:
                AnsiConsole.MarkupLine($"[red]Subcomando desconhecido:[/] cloud {sub}");
                AnsiConsole.MarkupLine("Subcomandos disponíveis: [cyan]status, config, login, logout, list, upload, restore[/]");
                return 1;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<int> HandleWatchCommand(string[] args, SteamSaveMigratorEngine engine)
    {
        ConsoleUi.ShowBanner();

        var pollStr = GetArgValue(args, "--poll");
        var delayStr = GetArgValue(args, "--delay");

        var config = new GameWatcherConfig();
        if (!string.IsNullOrWhiteSpace(pollStr) && int.TryParse(pollStr, out var pollSec))
            config.PollIntervalSeconds = Math.Max(1, pollSec);

        if (!string.IsNullOrWhiteSpace(delayStr) && int.TryParse(delayStr, out var delaySec))
            config.PostExitDelaySeconds = Math.Max(1, delaySec);

        if (args.Contains("--no-cloud"))
            config.AutoUploadToGoogleDrive = false;

        if (args.Contains("--no-backup"))
            config.AutoBackupOnGameExit = false;

        var isCloudAuth = await engine.GoogleDrive.IsAuthenticatedAsync();
        string cloudStatusMsg = isCloudAuth
            ? "[bold green]Conectado (Uploads ativos)[/]"
            : "[bold yellow]Desconectado (Apenas backups locais serão feitos)[/]";

        if (!isCloudAuth && config.AutoUploadToGoogleDrive)
        {
            AnsiConsole.MarkupLine("[yellow]ℹ️ Dica: Você ainda não se conectou ao Google Drive. Os backups serão salvos localmente.[/]");
            AnsiConsole.MarkupLine("[grey]Para habilitar sincronização em nuvem, rode em outro terminal:[/] [cyan]steamsavemigrator cloud login[/]\n");
        }

        AnsiConsole.MarkupLine("[bold cyan]╔═══════════════════════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║          👁️  MONITOR AUTOMÁTICO DE JOGOS DA STEAM             ║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚═══════════════════════════════════════════════════════════════╝[/]");
        AnsiConsole.MarkupLine($"  [bold]• Verificação:[/] a cada {config.PollIntervalSeconds}s");
        AnsiConsole.MarkupLine($"  [bold]• Estabilização pós-fechamento:[/] {config.PostExitDelaySeconds}s");
        AnsiConsole.MarkupLine($"  [bold]• Backup Automático:[/] {(config.AutoBackupOnGameExit ? "[green]Ativado[/]" : "[red]Desativado[/]")}");
        AnsiConsole.MarkupLine($"  [bold]• Google Drive:[/] {(config.AutoUploadToGoogleDrive ? cloudStatusMsg : "[grey]Desativado por parâmetro[/]")}");
        AnsiConsole.MarkupLine("\n[bold green]🟢 Monitor em execução! Abra qualquer jogo da Steam normalmente.[/]");
        AnsiConsole.MarkupLine("[grey]Ao fechar o jogo, o SteamSaveMigrator fará backup e upload automáticos.[/]");
        AnsiConsole.MarkupLine("[grey]Pressione [bold white][Q][/] ou [bold white][Ctrl+C][/] para encerrar o monitoramento.[/]\n");

        using var watcher = engine.CreateWatcher(config);

        watcher.OnWatcherEvent += (e) =>
        {
            var time = e.Timestamp.ToString("HH:mm:ss");
            var gameName = e.Game?.Name ?? "Jogo Steam";
            var appId = e.Game?.AppId ?? 0;

            switch (e.Type)
            {
                case GameWatcherEventType.GameStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]🎮 JOGO INICIADO:[/] [white]{Markup.Escape(gameName)}[/] (AppID: [yellow]{appId}[/])");
                    break;

                case GameWatcherEventType.GameExited:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold yellow]⏹️ JOGO FECHADO:[/] [white]{Markup.Escape(gameName)}[/]! Aguardando {config.PostExitDelaySeconds}s para gravação final dos saves...");
                    break;

                case GameWatcherEventType.BackupStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold cyan]💾 BACKUP INICIADO:[/] Localizando e compactando saves de [white]{Markup.Escape(gameName)}[/]...");
                    break;

                case GameWatcherEventType.BackupCompleted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]✅ BACKUP LOCAL CONCLUÍDO:[/] Arquivo gerado em [grey]{Markup.Escape(e.LocalZipPath ?? "")}[/]");
                    break;

                case GameWatcherEventType.CloudUploadStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold cyan]☁️ UPLOAD EM NUVEM:[/] Enviando backup de [white]{Markup.Escape(gameName)}[/] para o Google Drive...");
                    break;

                case GameWatcherEventType.CloudUploadCompleted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]🎉 SINCRONIZADO NO GOOGLE DRIVE:[/] Salvo com sucesso! ID: [cyan]{e.CloudBackup?.FileId ?? "-"}[/]");
                    break;

                case GameWatcherEventType.Error:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold red]❌ ERRO:[/] {Markup.Escape(e.Message)}");
                    break;
            }
        };

        watcher.Start();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                {
                    break;
                }
            }
            await Task.Delay(200, cts.Token).ContinueWith(_ => { });
        }

        await watcher.StopAsync();
        AnsiConsole.MarkupLine("\n[bold yellow]🛑 Monitor de jogos encerrado com sucesso.[/]");
        return 0;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void ShowHelp()
    {
        ConsoleUi.ShowBanner();
        AnsiConsole.MarkupLine("[bold]USO:[/] steamsavemigrator [[comando]] [[opções]]\n");
        AnsiConsole.MarkupLine("[bold]COMANDOS DISPONÍVEIS:[/]");
        AnsiConsole.MarkupLine("  [cyan](sem argumentos)[/]                      Inicia a interface de console interativa rica");
        AnsiConsole.MarkupLine("  [cyan]detect / status / info[/]                 Detecta instalação da Steam, bibliotecas e contas ativas");
        AnsiConsole.MarkupLine("  [cyan]list-games[/]                             Lista todos os jogos instalados nas bibliotecas");
        AnsiConsole.MarkupLine("  [cyan]scan [[--appid <id>]][/]                    Analisa e localiza pastas de saves de jogos");
        AnsiConsole.MarkupLine("  [cyan]backup --appid <id> [[--out <zip>]][/]      Realiza o backup dos saves de um jogo");
        AnsiConsole.MarkupLine("  [cyan]restore --file <zip> [[options]][/]         Restaura o backup convertendo para o usuário e perfil Steam");
        AnsiConsole.MarkupLine("                                            Opções: [[--user <nome>]] [[--steam-id <id3>]] [[--dry-run]]");
        AnsiConsole.MarkupLine("  [cyan]cloud [[status|config|login|logout|list|restore]][/] Gerencia credenciais e backups no Google Drive");
        AnsiConsole.MarkupLine("  [cyan]watch [[--poll <ms>]] [[--delay <ms>]][/]     Monitor em tempo real de fechamento de jogos com backup em nuvem\n");
    }
}
