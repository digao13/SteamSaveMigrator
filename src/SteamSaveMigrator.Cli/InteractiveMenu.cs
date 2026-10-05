using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using SteamSaveMigrator.Core;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using SteamSaveMigrator.Core.Services;

namespace SteamSaveMigrator.Cli;

public class InteractiveMenu
{
    private readonly SteamSaveMigratorEngine _engine = new();

    public async Task RunAsync()
    {
        _engine.InitializeSteam();

        while (true)
        {
            ConsoleUi.ShowBanner();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]Selecione uma opção no menu:[/]")
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                        "🔍 1. Detectar Steam e Contas",
                        "🎮 2. Detectar Jogos Instalados (AppID e Manifest)",
                        "💾 3. Fazer Backup de Saves de um Jogo",
                        "📦 4. Fazer Backup em Lote (Todos os Jogos com Saves)",
                        "♻️  5. Restaurar Backup (com Migração de Usuário e Perfil Steam)",
                        "☁️  6. Google Drive & Backups na Nuvem (Conectar, Listar e Restaurar)",
                        "👁️  7. Monitor Automático de Jogos (Backup e Upload ao Fechar Jogo)",
                        "❌ 0. Sair"
                    }));

            if (choice.StartsWith("❌") || choice.StartsWith("0"))
            {
                AnsiConsole.MarkupLine("[bold yellow]Encerrando o SteamSaveMigrator. Até logo![/]");
                break;
            }

            try
            {
                if (choice.StartsWith("🔍"))
                {
                    HandleDetectSteam();
                }
                else if (choice.StartsWith("🎮"))
                {
                    HandleDetectGames();
                }
                else if (choice.StartsWith("💾"))
                {
                    await HandleBackupGameAsync();
                }
                else if (choice.StartsWith("📦"))
                {
                    await HandleBatchBackupAsync();
                }
                else if (choice.StartsWith("♻️"))
                {
                    await HandleRestoreBackupAsync();
                }
                else if (choice.StartsWith("☁️"))
                {
                    await HandleGoogleDriveMenuAsync();
                }
                else if (choice.StartsWith("👁️"))
                {
                    await HandleGameWatcherMenuAsync();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }

            AnsiConsole.MarkupLine("\n[grey]Pressione qualquer tecla para voltar ao menu principal...[/]");
            Console.ReadKey(true);
        }
    }

    private void HandleDetectSteam()
    {
        AnsiConsole.Status()
            .Start("Detectando instalação da Steam e contas...", ctx =>
            {
                _engine.InitializeSteam();
            });

        ConsoleUi.DisplaySteamInfo(_engine.SteamInfo);
    }

    private void HandleDetectGames()
    {
        List<SteamGame> games = new();

        AnsiConsole.Status()
            .Start("Varrendo bibliotecas e manifestos ACF...", ctx =>
            {
                games = _engine.GetInstalledGames();
            });

        if (games.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhum jogo instalado foi encontrado nas bibliotecas da Steam.[/]");
            return;
        }

        ConsoleUi.DisplayGamesTable(games);
    }

    private async Task HandleBackupGameAsync()
    {
        var games = _engine.GetInstalledGames();
        if (games.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhum jogo encontrado para backup.[/]");
            return;
        }

        var gameChoice = AnsiConsole.Prompt(
            new SelectionPrompt<SteamGame>()
                .Title("[bold cyan]Escolha o jogo para escanear saves e fazer backup:[/]")
                .PageSize(15)
                .UseConverter(g => $"{g.Name} (AppID: {g.AppId}) - {g.SizeFormatted}")
                .AddChoices(games));

        SteamGame scannedGame = null!;
        await AnsiConsole.Status()
            .StartAsync($"Escaneando saves de [green]{gameChoice.Name}[/]...", async ctx =>
            {
                scannedGame = _engine.ScanSavesForGame(gameChoice);
            });

        ConsoleUi.DisplaySaveLocations(scannedGame);

        if (!scannedGame.HasSaves)
        {
            var force = AnsiConsole.Confirm("Nenhum save com arquivos foi detectado. Deseja tentar fazer backup mesmo assim?", false);
            if (!force) return;
        }

        var defaultBackupDir = Path.Combine(Environment.CurrentDirectory, "backups");
        var sanitizedName = string.Join("_", scannedGame.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var defaultFileName = $"{sanitizedName}_{scannedGame.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var defaultPath = Path.Combine(defaultBackupDir, defaultFileName);

        var outputZip = AnsiConsole.Ask<string>(
            "Informe o caminho do arquivo de backup (.zip):",
            defaultPath
        );

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Compactando saves de {scannedGame.Name}[/]", maxValue: 100);

                var progress = new Progress<BackupProgressReport>(report =>
                {
                    if (report.TotalFiles > 0)
                    {
                        task.Value = (double)report.CurrentFileIndex / report.TotalFiles * 100;
                        task.Description = $"[green]Salvando:[/] [grey]{report.CurrentFileName}[/]";
                    }
                });

                await _engine.BackupGameAsync(scannedGame, outputZip, progress: progress);
                task.Value = 100;
                task.Description = "[bold green]Backup concluído com sucesso![/]";
            });

        var fileInfo = new FileInfo(outputZip);
        AnsiConsole.MarkupLine($"\n[bold green]✅ Backup gerado com sucesso![/]");
        AnsiConsole.MarkupLine($"[bold]Arquivo:[/] [cyan]{outputZip}[/]");
        AnsiConsole.MarkupLine($"[bold]Tamanho compactado:[/] [yellow]{ConsoleUi.FormatBytes(fileInfo.Length)}[/]");
    }

    private async Task HandleRestoreBackupAsync()
    {
        var defaultBackupDir = Path.Combine(Environment.CurrentDirectory, "backups");
        var backupFiles = Directory.Exists(defaultBackupDir)
            ? Directory.GetFiles(defaultBackupDir, "*.zip")
            : Array.Empty<string>();

        string zipPath;
        if (backupFiles.Length > 0)
        {
            var options = new List<string>(backupFiles) { "[Digitar outro caminho...]" };
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]Selecione o arquivo de backup para restaurar:[/]")
                    .AddChoices(options));

            if (selected == "[Digitar outro caminho...]")
            {
                zipPath = AnsiConsole.Ask<string>("Informe o caminho completo do arquivo de backup .zip:");
            }
            else
            {
                zipPath = selected;
            }
        }
        else
        {
            zipPath = AnsiConsole.Ask<string>("Informe o caminho completo do arquivo de backup .zip:");
        }

        if (!File.Exists(zipPath))
        {
            AnsiConsole.MarkupLine($"[red]Arquivo não encontrado: {zipPath}[/]");
            return;
        }

        // Lê manifesto
        var manifest = await _engine.ReadBackupManifestAsync(zipPath);
        if (manifest == null)
        {
            AnsiConsole.MarkupLine("[red]O arquivo selecionado não contém um manifesto válido do SteamSaveMigrator.[/]");
            return;
        }

        var sourceSteamInfo = !string.IsNullOrWhiteSpace(manifest.SourceSteamPersonaName)
            ? $"{manifest.SourceSteamPersonaName} (ID3: {manifest.SourceSteamId3})"
            : (manifest.SourceSteamId3.HasValue ? $"ID3: {manifest.SourceSteamId3}" : "Padrão / Não gravado");

        var activeSteamAccount = _engine.SteamInfo.ActiveAccount;
        var activeSteamDisplay = activeSteamAccount != null
            ? $"{activeSteamAccount.PersonaName} (ID3: {activeSteamAccount.SteamId3}) [Conectado]"
            : "Nenhuma conta detectada";

        var panel = new Panel(new Markup(
            $"[bold cyan]Jogo:[/] [bold white]{manifest.GameName}[/] (AppID: [yellow]{manifest.AppId}[/])\n" +
            $"[bold cyan]Criado em:[/] [grey]{manifest.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}[/]\n" +
            $"[bold cyan]Máquina de Origem:[/] [grey]{manifest.SourceMachineName}[/]\n" +
            $"[bold cyan]Usuário Windows de Origem:[/] [red]{manifest.SourceUsername}[/] ([grey]{manifest.SourceUserProfile}[/])\n" +
            $"[bold cyan]Conta Steam de Origem:[/] [yellow]{sourceSteamInfo}[/]\n" +
            $"[bold cyan]Conta Steam Conectada Aqui:[/] [bold green]{activeSteamDisplay}[/]\n" +
            $"[bold cyan]Total de Arquivos:[/] [bold green]{manifest.TotalFiles}[/] ({ConsoleUi.FormatBytes(manifest.TotalSizeBytes)})"
        ))
        {
            Header = new PanelHeader("[bold green]📦 Informações do Backup e Contas Detectadas[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);

        var currentUsername = Environment.UserName;
        var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var targetUsername = AnsiConsole.Ask<string>(
            "Informe o nome do novo usuário de destino (Windows):",
            currentUsername
        );

        var targetUserProfile = AnsiConsole.Ask<string>(
            "Informe a pasta de perfil do novo usuário:",
            targetUsername.Equals(currentUsername, StringComparison.OrdinalIgnoreCase)
                ? currentUserProfile
                : WindowsKnownFolders.GetFoldersForUsername(targetUsername).UserProfile
        );

        // Pergunta sobre a migração de perfil da Steam
        uint? targetSteamId = null;
        string? targetSteamPersona = null;

        if (activeSteamAccount != null)
        {
            var migrateSteam = AnsiConsole.Confirm(
                $"Deseja migrar os dados do save para a conta Steam conectada no momento ([green]{activeSteamAccount.PersonaName}[/] - ID3: {activeSteamAccount.SteamId3})?",
                true
            );

            if (migrateSteam)
            {
                targetSteamId = activeSteamAccount.SteamId3;
                targetSteamPersona = activeSteamAccount.PersonaName;
            }
            else if (_engine.SteamInfo.Accounts.Count > 1)
            {
                var chooseAccount = AnsiConsole.Confirm("Deseja escolher outra conta Steam existente nesta máquina?", false);
                if (chooseAccount)
                {
                    var selectedAcc = AnsiConsole.Prompt(
                        new SelectionPrompt<SteamUserAccount>()
                            .Title("Selecione a conta Steam de destino:")
                            .UseConverter(a => a.DisplayName)
                            .AddChoices(_engine.SteamInfo.Accounts));
                    targetSteamId = selectedAcc.SteamId3;
                    targetSteamPersona = selectedAcc.PersonaName;
                }
            }
        }

        var isDryRun = AnsiConsole.Confirm("Deseja executar apenas uma simulação (Dry Run) para conferir os caminhos?", false);

        var optionsConversion = new PathConversionOptions
        {
            SourceUsername = manifest.SourceUsername,
            SourceUserProfile = manifest.SourceUserProfile,
            SourceSteamId3 = manifest.SourceSteamId3,
            SourceSteamPersonaName = manifest.SourceSteamPersonaName,
            TargetUsername = targetUsername,
            TargetUserProfile = targetUserProfile,
            TargetSteamId3 = targetSteamId,
            TargetSteamPersonaName = targetSteamPersona,
            OverwriteExisting = true,
            DryRun = isDryRun
        };

        RestoreResult result = null!;
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Restaurando saves e convertendo caminhos...[/]", maxValue: 100);
                var progress = new Progress<RestoreProgressReport>(report =>
                {
                    if (report.TotalFiles > 0)
                    {
                        task.Value = (double)report.CurrentFileIndex / report.TotalFiles * 100;
                        task.Description = $"[green]Gravando:[/] [grey]{report.FileName}[/]";
                    }
                });

                result = await _engine.RestoreBackupAsync(zipPath, optionsConversion, progress);
                task.Value = 100;
            });

        AnsiConsole.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(isDryRun ? "[bold yellow]🔍 Pré-visualização da Restauração (Simulação)[/]" : "[bold green]✅ Relatório de Restauração[/]");

        table.AddColumn("Arquivo");
        table.AddColumn("Caminho Original");
        table.AddColumn("Novo Caminho no Destino");
        table.AddColumn("Status");

        foreach (var item in result.Items)
        {
            var statusStr = item.Success
                ? (isDryRun ? "[yellow]Simulado[/]" : "[green]Restaurado[/]")
                : $"[red]Falha: {item.ErrorMessage}[/]";

            table.AddRow(
                Path.GetFileName(item.DestinationPath),
                $"[grey]{item.OriginalPath}[/]",
                $"[bold white]{item.DestinationPath}[/]",
                statusStr
            );
        }

        AnsiConsole.Write(table);

        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine(isDryRun
                ? "\n[bold yellow]🔍 Simulação concluída! Todos os caminhos foram convertidos com sucesso.[/]"
                : $"\n[bold green]🎉 Todos os saves foram restaurados com sucesso para o usuário '{targetUsername}'![/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold red]⚠️ Restauração finalizada com {result.ErrorCount} erro(s).[/]");
        }
    }

    private async Task HandleBatchBackupAsync()
    {
        var games = _engine.GetInstalledGames();
        if (games.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhum jogo encontrado.[/]");
            return;
        }

        var backupDir = Path.Combine(Environment.CurrentDirectory, "backups");
        if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

        var gamesWithSaves = new List<SteamGame>();

        await AnsiConsole.Status()
            .StartAsync("Escaneando todos os jogos e localizações de saves...", async ctx =>
            {
                foreach (var g in games)
                {
                    var scanned = _engine.ScanSavesForGame(g);
                    if (scanned.HasSaves)
                    {
                        gamesWithSaves.Add(scanned);
                    }
                }
            });

        if (gamesWithSaves.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhum jogo com saves locais foi detectado nesta máquina.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Foram encontrados {gamesWithSaves.Count} jogos com saves prontos para backup:[/]");
        foreach (var g in gamesWithSaves)
        {
            AnsiConsole.MarkupLine($"  • [bold white]{g.Name}[/] - [green]{g.TotalSaveFiles} arquivo(s)[/] ({ConsoleUi.FormatBytes(g.TotalSaveSizeBytes)})");
        }

        if (!AnsiConsole.Confirm("\nDeseja iniciar o backup de todos esses jogos agora?", true))
        {
            return;
        }

        int successCount = 0;
        foreach (var g in gamesWithSaves)
        {
            var sanitizedName = string.Join("_", g.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var outZip = Path.Combine(backupDir, $"{sanitizedName}_{g.AppId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            AnsiConsole.MarkupLine($"[cyan]Compactando:[/] [bold white]{g.Name}[/]...");
            await _engine.BackupGameAsync(g, outZip);
            successCount++;
        }

        AnsiConsole.MarkupLine($"\n[bold green]🎉 Backup em lote concluído! {successCount} jogo(s) salvos na pasta '{backupDir}'.[/]");
    }

    private async Task HandleGoogleDriveMenuAsync()
    {
        while (true)
        {
            var isAuth = await _engine.GoogleDrive.IsAuthenticatedAsync();
            var userEmail = isAuth ? await _engine.GoogleDrive.GetUserEmailAsync() : null;

            var statusBadge = isAuth
                ? $"[bold green]Conectado ({userEmail ?? "Conta Google"})[/]"
                : "[bold yellow]Não conectado[/]";

            var credStatus = _engine.GoogleDrive.HasConfiguredCredentials
                ? "[bold green]Configuradas[/]"
                : "[bold yellow]Não configuradas[/]";

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold cyan]=== Nuvem Google Drive ===[/]\n  Status: {statusBadge} | Credenciais OAuth: {credStatus}")
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                        "⚙️  1. Configurar Credenciais OAuth (Importar credentials.json)",
                        "🔑 2. Conectar / Fazer Login no Google Drive",
                        "📋 3. Listar Backups Armazenados na Nuvem",
                        "⚡ 4. Baixar e Restaurar Backup da Nuvem",
                        "⬆️  5. Fazer Upload de Arquivo Local para a Nuvem",
                        "🚪 6. Desconectar Conta (Logout)",
                        "⬅️  0. Voltar ao Menu Principal"
                    }));

            if (choice.StartsWith("⬅️") || choice.StartsWith("0"))
            {
                break;
            }

            if (choice.StartsWith("⚙️"))
            {
                AnsiConsole.MarkupLine("\n[bold cyan]Configuração de Credenciais OAuth do Google Drive[/]");
                AnsiConsole.MarkupLine("[grey]Para o Google autorizar o acesso aos seus backups, é necessário fornecer o arquivo 'credentials.json' gerado no Google Cloud Console.[/]\n");

                var mode = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Como deseja configurar?")
                        .AddChoices("📁 Importar arquivo credentials.json", "⌨️ Inserir Client ID e Client Secret manualmente", "Cancelar"));

                if (mode.StartsWith("📁"))
                {
                    var path = AnsiConsole.Ask<string>("Caminho do arquivo [bold]credentials.json[/]:");
                    if (!File.Exists(path))
                    {
                        AnsiConsole.MarkupLine("[bold red]❌ Arquivo não encontrado.[/]");
                    }
                    else if (_engine.GoogleDrive.ConfigureCredentialsFromJson(path))
                    {
                        AnsiConsole.MarkupLine("[bold green]✅ Credenciais importadas com sucesso![/]");
                        AnsiConsole.MarkupLine($"[grey]Client ID configurado:[/] [cyan]{_engine.GoogleDrive.CurrentClientId}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]❌ Não foi possível extrair as chaves do arquivo informado.[/]");
                    }
                }
                else if (mode.StartsWith("⌨️"))
                {
                    var id = AnsiConsole.Ask<string>("Client ID:");
                    var secret = AnsiConsole.Prompt(new TextPrompt<string>("Client Secret:").Secret());
                    _engine.GoogleDrive.ConfigureClientSecrets(id, secret);
                    AnsiConsole.MarkupLine("[bold green]✅ Credenciais salvas com sucesso![/]");
                }
            }
            else if (choice.StartsWith("🔑"))
            {
                if (!_engine.GoogleDrive.HasConfiguredCredentials)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Nenhuma credencial OAuth configurada ainda.[/]");
                    AnsiConsole.MarkupLine("O Google exige um Client ID para autorizar o acesso. Use a [cyan]Opção 1[/] para importar o credentials.json baixado do Google Cloud.");
                }
                else
                {
                    AnsiConsole.MarkupLine("[bold cyan]Iniciando navegador para autenticação OAuth2 no Google Drive...[/]");
                    try
                    {
                        var success = await _engine.GoogleDrive.AuthenticateAsync();
                        if (success)
                        {
                            var email = await _engine.GoogleDrive.GetUserEmailAsync();
                            AnsiConsole.MarkupLine($"\n[bold green]✅ Autenticado com sucesso![/] [cyan]{email}[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("\n[bold red]❌ Não foi possível autenticar com o Google Drive.[/]");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"\n[bold red]❌ Erro na autenticação:[/] {Markup.Escape(ex.Message)}");
                        if (ex.Message.Contains("401") || ex.Message.Contains("invalid_client"))
                        {
                            AnsiConsole.MarkupLine("\n[bold yellow]Dica:[/] O erro 401 (invalid_client) acontece quando o Client ID não existe no Google Cloud.");
                            AnsiConsole.MarkupLine("Verifique se você criou um cliente do tipo 'Aplicativo para Computador / Desktop' no Google Cloud Console e reimporte as credenciais na Opção 1.");
                        }
                    }
                }
            }
            else if (choice.StartsWith("📋"))
            {
                if (!isAuth)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Conecte-se ao Google Drive primeiro (Opção 1).[/]");
                }
                else
                {
                    List<CloudBackupInfo> backups = new();
                    await AnsiConsole.Status()
                        .StartAsync("Buscando backups na pasta 'SteamSaveMigrator_Backups'...", async _ =>
                        {
                            backups = await _engine.GoogleDrive.ListBackupsAsync();
                        });

                    if (backups.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]Nenhum backup encontrado na sua pasta do Google Drive.[/]");
                    }
                    else
                    {
                        var table = new Table().Border(TableBorder.Rounded);
                        table.AddColumn("[bold]Arquivo[/]");
                        table.AddColumn("[bold]Jogo[/]");
                        table.AddColumn("[bold]AppID[/]");
                        table.AddColumn("[bold]Tamanho[/]");
                        table.AddColumn("[bold]Data[/]");

                        foreach (var b in backups)
                        {
                            table.AddRow(
                                $"[white]{Markup.Escape(b.FileName)}[/]",
                                $"[cyan]{Markup.Escape(b.GameName)}[/]",
                                b.AppId > 0 ? $"[yellow]{b.AppId}[/]" : "[grey]-[/]",
                                $"[green]{b.FormattedSize}[/]",
                                b.CreatedTime.HasValue ? b.CreatedTime.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "-"
                            );
                        }

                        AnsiConsole.Write(table);
                    }
                }
            }
            else if (choice.StartsWith("⚡"))
            {
                if (!isAuth)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Conecte-se ao Google Drive primeiro (Opção 1).[/]");
                }
                else
                {
                    List<CloudBackupInfo> backups = new();
                    await AnsiConsole.Status()
                        .StartAsync("Carregando backups da nuvem...", async _ =>
                        {
                            backups = await _engine.GoogleDrive.ListBackupsAsync();
                        });

                    if (backups.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]Nenhum backup encontrado na nuvem para restauração.[/]");
                    }
                    else
                    {
                        var selectedBackup = AnsiConsole.Prompt(
                            new SelectionPrompt<CloudBackupInfo>()
                                .Title("[bold cyan]Selecione o backup na nuvem que deseja restaurar:[/]")
                                .UseConverter(b => $"{b.GameName} ({b.FormattedSize}) - {b.FileName}")
                                .AddChoices(backups));

                        var activeSteam = _engine.SteamInfo.ActiveAccount ?? _engine.SteamInfo.PrimaryAccount;
                        AnsiConsole.MarkupLine($"\n[bold cyan]Restaurando backup de:[/] [white]{selectedBackup.GameName}[/]");
                        AnsiConsole.MarkupLine($"  [bold]• Usuário Windows de Destino:[/] [green]{Environment.UserName}[/]");
                        if (activeSteam != null)
                        {
                            AnsiConsole.MarkupLine($"  [bold]• Conta Steam de Destino:[/] [bold green]{activeSteam.PersonaName}[/] (ID3: {activeSteam.SteamId3})");
                        }

                        if (AnsiConsole.Confirm("Deseja baixar e restaurar este backup agora?", true))
                        {
                            RestoreResult? result = null;
                            await AnsiConsole.Progress()
                                .StartAsync(async ctx =>
                                {
                                    var task = ctx.AddTask("[green]Baixando do Google Drive e restaurando saves...[/]");
                                    var progress = new Progress<double>(p => task.Value = p * 100);

                                    result = await _engine.RestoreCloudBackupAsync(
                                        selectedBackup.FileId,
                                        targetSteamId3: activeSteam?.SteamId3,
                                        overwriteExisting: true,
                                        dryRun: false,
                                        downloadProgress: progress);
                                });

                            if (result != null && result.IsSuccess)
                            {
                                AnsiConsole.MarkupLine($"\n[bold green]🎉 Sucesso total! {result.SuccessCount} arquivo(s) restaurados com perfeição![/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"\n[bold red]❌ Falha na restauração: {result?.ErrorCount ?? 1} erro(s).[/]");
                            }
                        }
                    }
                }
            }
            else if (choice.StartsWith("⬆️"))
            {
                if (!isAuth)
                {
                    AnsiConsole.MarkupLine("[bold yellow]⚠️ Conecte-se ao Google Drive primeiro (Opção 1).[/]");
                }
                else
                {
                    var backupDir = Path.Combine(Environment.CurrentDirectory, "backups");
                    var localZips = Directory.Exists(backupDir)
                        ? Directory.GetFiles(backupDir, "*.zip")
                        : Array.Empty<string>();

                    string filePath;
                    if (localZips.Length > 0)
                    {
                        var zipChoices = localZips.ToList();
                        zipChoices.Add("Outro arquivo (digitar caminho)");

                        var selected = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("[bold cyan]Escolha o backup local para enviar à nuvem:[/]")
                                .AddChoices(zipChoices));

                        if (selected.StartsWith("Outro"))
                        {
                            filePath = AnsiConsole.Ask<string>("Digite o caminho completo do arquivo .zip:");
                        }
                        else
                        {
                            filePath = selected;
                        }
                    }
                    else
                    {
                        filePath = AnsiConsole.Ask<string>("Digite o caminho completo do arquivo .zip:");
                    }

                    if (!File.Exists(filePath))
                    {
                        AnsiConsole.MarkupLine("[bold red]Arquivo não encontrado.[/]");
                    }
                    else
                    {
                        string gameName = Path.GetFileNameWithoutExtension(filePath);
                        uint appId = 0;
                        try
                        {
                            var manifest = await _engine.ReadBackupManifestAsync(filePath);
                            if (manifest != null)
                            {
                                if (!string.IsNullOrWhiteSpace(manifest.GameName)) gameName = manifest.GameName;
                                appId = manifest.AppId;
                            }
                        }
                        catch { }

                        CloudBackupInfo? uploaded = null;
                        await AnsiConsole.Progress()
                            .StartAsync(async ctx =>
                            {
                                var task = ctx.AddTask("[green]Fazendo upload para o Google Drive...[/]");
                                var prog = new Progress<double>(p => task.Value = p * 100);
                                uploaded = await _engine.GoogleDrive.UploadBackupAsync(filePath, gameName, appId, progress: prog);
                            });

                        if (uploaded != null)
                        {
                            AnsiConsole.MarkupLine($"\n[bold green]✅ Upload concluído com sucesso![/] ID: [cyan]{uploaded.FileId}[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("\n[bold red]❌ Falha no upload para o Google Drive.[/]");
                        }
                    }
                }
            }
            else if (choice.StartsWith("🚪"))
            {
                await _engine.GoogleDrive.SignOutAsync();
                AnsiConsole.MarkupLine("[bold green]✅ Sessão do Google Drive encerrada e credenciais locais removidas.[/]");
            }

            AnsiConsole.MarkupLine("\n[grey]Pressione qualquer tecla para continuar...[/]");
            Console.ReadKey(true);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task HandleGameWatcherMenuAsync()
    {
        var isAuth = await _engine.GoogleDrive.IsAuthenticatedAsync();
        var userEmail = isAuth ? await _engine.GoogleDrive.GetUserEmailAsync() : null;

        var config = new GameWatcherConfig
        {
            PollIntervalSeconds = 2,
            PostExitDelaySeconds = 3,
            AutoBackupOnGameExit = true,
            AutoUploadToGoogleDrive = isAuth
        };

        AnsiConsole.MarkupLine("\n[bold cyan]╔═══════════════════════════════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║          👁️  MONITOR AUTOMÁTICO DE JOGOS DA STEAM                    ║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚═══════════════════════════════════════════════════════════════════════╝[/]");
        AnsiConsole.MarkupLine($"  [bold]• Status Google Drive:[/] {(isAuth ? $"[bold green]Conectado ({userEmail})[/]" : "[bold yellow]Desconectado (backups apenas locais)[/]")}");
        AnsiConsole.MarkupLine($"  [bold]• Backup Automático:[/] [green]Ativado[/]");
        AnsiConsole.MarkupLine($"  [bold]• Sincronização em Nuvem:[/] {(config.AutoUploadToGoogleDrive ? "[bold green]Ativada (Upload ao fechar jogo)[/]" : "[grey]Desativada[/]")}");
        AnsiConsole.MarkupLine("\n[bold green]🟢 O monitor vigiará o início e o fechamento de qualquer jogo da sua Steam.[/]");
        AnsiConsole.MarkupLine("[grey]Assim que você sair do jogo, o backup dos saves será gerado e enviado para a nuvem automaticamente.[/]");
        AnsiConsole.MarkupLine("[bold yellow]Pressione qualquer tecla ou [Q] para parar o monitoramento e voltar.[/]\n");

        using var watcher = _engine.CreateWatcher(config);

        watcher.OnWatcherEvent += (e) =>
        {
            var time = e.Timestamp.ToString("HH:mm:ss");
            var gameName = e.Game?.Name ?? "Jogo Steam";
            var appId = e.Game?.AppId ?? 0;

            switch (e.Type)
            {
                case GameWatcherEventType.GameStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]🎮 JOGO DETECTADO:[/] [white]{Markup.Escape(gameName)}[/] (AppID: [yellow]{appId}[/]) está rodando!");
                    break;

                case GameWatcherEventType.GameExited:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold yellow]⏹️ JOGO FECHADO:[/] [white]{Markup.Escape(gameName)}[/]! Aguardando {config.PostExitDelaySeconds}s para que os arquivos de save sejam liberados...");
                    break;

                case GameWatcherEventType.BackupStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold cyan]💾 BACKUP LOCAL:[/] Compactando saves de [white]{Markup.Escape(gameName)}[/]...");
                    break;

                case GameWatcherEventType.BackupCompleted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]✅ BACKUP PRONTO:[/] Arquivo gerado em [grey]{Markup.Escape(e.LocalZipPath ?? "")}[/]");
                    break;

                case GameWatcherEventType.CloudUploadStarted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold cyan]☁️ UPLOAD GOOGLE DRIVE:[/] Enviando backup de [white]{Markup.Escape(gameName)}[/] para a nuvem...");
                    break;

                case GameWatcherEventType.CloudUploadCompleted:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold green]🎉 SINCRONIZADO NO GOOGLE DRIVE:[/] Salvo com sucesso na nuvem! ID: [cyan]{e.CloudBackup?.FileId ?? "-"}[/]");
                    break;

                case GameWatcherEventType.Error:
                    AnsiConsole.MarkupLine($"[grey][{time}][/] [bold red]❌ ERRO:[/] {Markup.Escape(e.Message)}");
                    break;
            }
        };

        watcher.Start();

        var cts = new CancellationTokenSource();
        while (!cts.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                Console.ReadKey(true);
                break;
            }
            await Task.Delay(200, cts.Token).ContinueWith(_ => { });
        }

        await watcher.StopAsync();
        AnsiConsole.MarkupLine("\n[bold yellow]🛑 Monitor de jogos interrompido.[/]");
    }
}
