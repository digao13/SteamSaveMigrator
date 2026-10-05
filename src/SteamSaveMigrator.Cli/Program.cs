using System.Text;
using System.Threading.Tasks;
using SteamSaveMigrator.Cli;

namespace SteamSaveMigrator.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Garante suporte UTF-8 no console para acentuação e emojis
        System.Console.OutputEncoding = Encoding.UTF8;

        return await CliCommands.ExecuteAsync(args);
    }
}
