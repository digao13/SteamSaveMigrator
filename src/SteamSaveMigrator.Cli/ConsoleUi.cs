using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Cli;

public static class ConsoleUi
{
    public static void ShowBanner()
    {
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowHeight > 0)
            {
                AnsiConsole.Clear();
            }
        }
        catch { }

        var rule = new Rule("[bold cyan]STEAM SAVE MIGRATOR[/]")
        {
            Style = Style.Parse("deepskyblue1")
        };
        AnsiConsole.Write(rule);

        var banner = new FigletText("SteamSave")
            .LeftJustified()
            .Color(Color.DeepSkyBlue1);
        AnsiConsole.Write(banner);

        var subBanner = new Markup("[bold white]Migrador e Conversor de Saves da Steam entre Usuários e PCs[/]\n" +
                                   "[grey]Versão 1.0.0 • Suporte a Conversão de Caminhos e Perfis do Windows[/]\n");
        AnsiConsole.Write(subBanner);
        AnsiConsole.WriteLine();
    }

    public static void DisplaySteamInfo(SteamInstallationInfo steam)
    {
        if (!steam.IsFound)
        {
            AnsiConsole.MarkupLine("[bold red]❌ Instalação da Steam não foi detectada automaticamente.[/]");
            AnsiConsole.MarkupLine("[grey]Verifique se a Steam está instalada ou informe o caminho manualmente.[/]\n");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.DeepSkyBlue1)
            .Title("[bold green]🔍 Steam Detectada com Sucesso[/]");

        table.AddColumn("[bold]Propriedade[/]");
        table.AddColumn("[bold]Valor[/]");

        table.AddRow("Diretório Steam", $"[green]{steam.SteamPath}[/]");
        table.AddRow("Executável", File.Exists(steam.SteamExe) ? $"[green]{steam.SteamExe}[/]" : "[yellow]Não encontrado[/]");
        table.AddRow("Pastas de Usuário (userdata)", Directory.Exists(steam.UserdataDirectory) ? $"[green]{steam.UserdataDirectory}[/]" : "[yellow]Vazio[/]");
        table.AddRow("Bibliotecas de Jogos", $"{steam.Libraries.Count} detectada(s)");
        table.AddRow("Contas de Usuário", $"{steam.Accounts.Count} conta(s) encontrada(s)");

        AnsiConsole.Write(table);

        if (steam.Accounts.Count > 0)
        {
            var accTable = new Table()
                .Border(TableBorder.Simple)
                .Title("[bold cyan]👤 Contas Steam Detectadas[/]");

            accTable.AddColumn("SteamID3");
            accTable.AddColumn("SteamID64");
            accTable.AddColumn("Nome da Conta");
            accTable.AddColumn("Nome de Exibição");
            accTable.AddColumn("Status");

            foreach (var acc in steam.Accounts)
            {
                var status = acc.IsActiveNow
                    ? "[bold green]🟢 Conectado Agora[/]"
                    : (acc.MostRecent ? "[cyan]Último Acesso[/]" : "[grey]Inativo[/]");
                accTable.AddRow(
                    acc.SteamId3.ToString(),
                    acc.SteamId64.ToString(),
                    acc.AccountName,
                    acc.PersonaName,
                    status
                );
            }

            AnsiConsole.Write(accTable);
        }

        if (steam.Libraries.Count > 0)
        {
            var libTable = new Table()
                .Border(TableBorder.Simple)
                .Title("[bold yellow]📚 Bibliotecas da Steam (libraryfolders.vdf)[/]");

            libTable.AddColumn("#");
            libTable.AddColumn("Caminho no Disco");
            libTable.AddColumn("Rótulo");
            libTable.AddColumn("Jogos Registrados");

            foreach (var lib in steam.Libraries)
            {
                libTable.AddRow(
                    lib.Index.ToString(),
                    lib.Path,
                    string.IsNullOrWhiteSpace(lib.Label) ? "[grey]Padrão[/]" : lib.Label,
                    $"{lib.AppIdsWithSizes.Count} app(s)"
                );
            }

            AnsiConsole.Write(libTable);
        }

        AnsiConsole.WriteLine();
    }

    public static void DisplayGamesTable(List<SteamGame> games)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold green]🎮 Jogos Detectados ({games.Count} jogos)[/]");

        table.AddColumn("[bold]AppID[/]");
        table.AddColumn("[bold]Nome do Jogo[/]");
        table.AddColumn("[bold]Pasta de Instalação[/]");
        table.AddColumn("[bold]Tamanho no Disco[/]");

        foreach (var g in games)
        {
            table.AddRow(
                $"[cyan]{g.AppId}[/]",
                $"[bold white]{g.Name}[/]",
                $"[grey]{g.InstallDir}[/]",
                g.SizeFormatted
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void DisplaySaveLocations(SteamGame game)
    {
        var panel = new Panel(new Markup(
            $"[bold cyan]Jogo:[/] [bold white]{game.Name}[/]  •  [bold cyan]AppID:[/] [bold yellow]{game.AppId}[/]\n" +
            $"[bold cyan]Instalação:[/] [grey]{game.InstallPath}[/]\n" +
            $"[bold cyan]Total de Saves Detectados:[/] [bold green]{game.TotalSaveFiles} arquivo(s) ({FormatBytes(game.TotalSaveSizeBytes)})[/]"
        ))
        {
            Header = new PanelHeader("[bold green]💾 Análise de Saves[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);

        if (game.DetectedSaveLocations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️ Nenhuma localização de save com arquivos foi detectada para este jogo.[/]");
            AnsiConsole.MarkupLine("[grey]O jogo pode não ter sido iniciado ainda ou salva exclusivamente em nuvem/registro.[/]\n");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Simple)
            .Title("[bold]Locais Onde os Saves Foram Encontrados[/]");

        table.AddColumn("Tipo");
        table.AddColumn("Descrição");
        table.AddColumn("Caminho Original");
        table.AddColumn("Caminho Tokenizado");
        table.AddColumn("Arquivos");
        table.AddColumn("Tamanho");

        foreach (var loc in game.DetectedSaveLocations)
        {
            var typeColor = loc.LocationType switch
            {
                SaveLocationType.SteamUserdata => "cyan",
                SaveLocationType.SavedGames => "green",
                SaveLocationType.Documents => "yellow",
                SaveLocationType.AppDataLocal => "blue",
                SaveLocationType.AppDataRoaming => "magenta",
                SaveLocationType.AppDataLocalLow => "purple",
                _ => "white"
            };

            table.AddRow(
                $"[{typeColor}]{loc.LocationType}[/]",
                loc.DisplayName,
                $"[grey]{loc.SourcePath}[/]",
                $"[bold white]{loc.TokenizedPath}[/]",
                $"[green]{loc.FileCount}[/]",
                loc.SizeFormatted
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.##} {units[unitIndex]}";
    }
}
