using System.IO;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Paths;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class PathVariableConverterTests
{
    [Fact]
    public void Should_Convert_Path_From_Original_To_New_User_Via_UserProfile()
    {
        // Cenário de Migração:
        // C:\Users\USUARIO_ORIGEM
        //       ↓
        // %USERPROFILE%
        //       ↓
        // C:\Users\NOVO_USUARIO

        var sourcePath = @"C:\Users\USUARIO_ORIGEM\Saved Games\CD Projekt Red\Cyberpunk 2077\save.dat";
        var sourceProfile = @"C:\Users\USUARIO_ORIGEM";
        var targetProfile = @"C:\Users\NOVO_USUARIO";

        // 1. Tokenização
        var tokenized = PathVariableConverter.Tokenize(sourcePath, sourceProfile);
        Assert.StartsWith("%SAVEDGAMES%", tokenized);

        // 2. Resolução para novo usuário
        var options = new PathConversionOptions
        {
            SourceUsername = "USUARIO_ORIGEM",
            SourceUserProfile = sourceProfile,
            TargetUsername = "NOVO_USUARIO",
            TargetUserProfile = targetProfile
        };

        var resolved = PathVariableConverter.Resolve(tokenized, options);

        var expected = Path.GetFullPath(@"C:\Users\NOVO_USUARIO\Saved Games\CD Projekt Red\Cyberpunk 2077\save.dat");
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Should_Convert_AppData_Locations_Properly()
    {
        var localPath = @"C:\Users\USUARIO_ORIGEM\AppData\Local\GameStudio\MyGame\save.bin";
        var roamingPath = @"C:\Users\USUARIO_ORIGEM\AppData\Roaming\EldenRing\76561198\ER0000.sl2";
        var sourceProfile = @"C:\Users\USUARIO_ORIGEM";
        var targetProfile = @"C:\Users\NOVO_USUARIO";

        var tokenLocal = PathVariableConverter.Tokenize(localPath, sourceProfile);
        var tokenRoaming = PathVariableConverter.Tokenize(roamingPath, sourceProfile);

        Assert.StartsWith("%LOCALAPPDATA%", tokenLocal);
        Assert.StartsWith("%APPDATA%", tokenRoaming);

        var options = new PathConversionOptions
        {
            SourceUsername = "USUARIO_ORIGEM",
            SourceUserProfile = sourceProfile,
            TargetUsername = "NOVO_USUARIO",
            TargetUserProfile = targetProfile
        };

        var resolvedLocal = PathVariableConverter.Resolve(tokenLocal, options);
        var resolvedRoaming = PathVariableConverter.Resolve(tokenRoaming, options);

        Assert.Equal(Path.GetFullPath(@"C:\Users\NOVO_USUARIO\AppData\Local\GameStudio\MyGame\save.bin"), resolvedLocal);
        Assert.Equal(Path.GetFullPath(@"C:\Users\NOVO_USUARIO\AppData\Roaming\EldenRing\76561198\ER0000.sl2"), resolvedRoaming);
    }

    [Fact]
    public void Should_Convert_Steam_Userdata_With_Different_Account()
    {
        var steamPath = @"C:\Program Files (x86)\Steam";
        var userdataSave = @"C:\Program Files (x86)\Steam\userdata\12345678\1091500\remote\sav.dat";

        var token = PathVariableConverter.Tokenize(userdataSave, @"C:\Users\USUARIO_ORIGEM", steamPath);
        Assert.StartsWith("%STEAM_USERDATA%", token);

        // Migrar de SteamID3 12345678 para 87654321
        var options = new PathConversionOptions
        {
            TargetSteamPath = @"D:\Steam",
            SourceSteamId3 = 12345678,
            TargetSteamId3 = 87654321
        };

        var resolved = PathVariableConverter.Resolve(token, options);
        var expected = Path.GetFullPath(@"D:\Steam\userdata\87654321\1091500\remote\sav.dat");
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Should_Tokenize_And_Resolve_LocalLow_Without_Colliding_With_Local()
    {
        var localLowPath = @"C:\Users\USUARIO_ORIGEM\AppData\LocalLow\Studio Doodal\Solateria\76561198041671480\SaveData.txt";
        var sourceProfile = @"C:\Users\USUARIO_ORIGEM";
        var targetProfile = @"C:\Users\NOVO_USUARIO";

        var token = PathVariableConverter.Tokenize(localLowPath, sourceProfile);
        Assert.False(token.StartsWith("%LOCALAPPDATA%\\Low", System.StringComparison.OrdinalIgnoreCase), "LocalLow não pode ser tokenizado como %LOCALAPPDATA%\\Low");
        Assert.Contains("LocalLow", token);

        var options = new PathConversionOptions
        {
            SourceUsername = "USUARIO_ORIGEM",
            SourceUserProfile = sourceProfile,
            TargetUsername = "NOVO_USUARIO",
            TargetUserProfile = targetProfile,
            SourceSteamId3 = 81405752,
            TargetSteamId3 = 1879694565
        };

        var resolved = PathVariableConverter.Resolve(token, options);
        var expected = Path.GetFullPath(@"C:\Users\NOVO_USUARIO\AppData\LocalLow\Studio Doodal\Solateria\76561199839960293\SaveData.txt");
        Assert.Equal(expected, resolved);
        Assert.DoesNotContain(@"\AppData\Local\Low", resolved);
    }

    [Fact]
    public void Should_Resolve_Legacy_LocalLow_In_LocalAppData_To_Real_LocalLow()
    {
        // Backups legados antigos que foram tokenizados com %LOCALAPPDATA%\Low\...
        var legacyToken = @"%LOCALAPPDATA%\Low\Studio Doodal\Solateria\76561198041671480\SaveData.txt";
        var options = new PathConversionOptions
        {
            TargetUsername = "Arianne",
            TargetUserProfile = @"C:\Users\Arianne",
            SourceSteamId3 = 81405752,
            TargetSteamId3 = 1879694565
        };

        var resolved = PathVariableConverter.Resolve(legacyToken, options);
        var expected = Path.GetFullPath(@"C:\Users\Arianne\AppData\LocalLow\Studio Doodal\Solateria\76561199839960293\SaveData.txt");
        Assert.Equal(expected, resolved);
        Assert.DoesNotContain(@"\AppData\Local\Low", resolved);
    }
}
