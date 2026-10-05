using System;
using SteamSaveMigrator.Core.Parsers;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class VdfParserTests
{
    [Fact]
    public void Should_Parse_AppManifest_Correctly()
    {
        var manifestText = """
        "AppState"
        {
            "appid"        "1091500"
            "Universe"        "1"
            "name"        "Cyberpunk 2077"
            "installdir"        "Cyberpunk 2077"
            "LastUpdated"        "1696000000"
            "SizeOnDisk"        "70234567890"
            "buildid"        "1234567"
            "UserConfig"
            {
                "language"        "brazilian"
            }
        }
        """;

        var node = VdfParser.ParseText(manifestText);

        Assert.Equal("AppState", node.Name);
        Assert.Equal(1091500u, node.GetUInt32("appid"));
        Assert.Equal("Cyberpunk 2077", node.GetString("name"));
        Assert.Equal("Cyberpunk 2077", node.GetString("installdir"));
        Assert.Equal(70234567890L, node.GetInt64("SizeOnDisk"));

        var userConfig = node["UserConfig"];
        Assert.NotNull(userConfig);
        Assert.Equal("brazilian", userConfig.GetString("language"));
    }

    [Fact]
    public void Should_Parse_LibraryFolders_Correctly()
    {
        var vdfText = """
        "libraryfolders"
        {
            "0"
            {
                "path"        "C:\\Program Files (x86)\\Steam"
                "label"        ""
                "contentid"        "12345"
                "totalsize"        "500000000000"
                "apps"
                {
                    "228980"        "0"
                    "1091500"        "70234567890"
                }
            }
            "1"
            {
                "path"        "D:\\SteamLibrary"
                "label"        "Secondary Drive"
                "apps"
                {
                    "1245620"        "49000000000"
                }
            }
        }
        """;

        var node = VdfParser.ParseText(vdfText);
        Assert.Equal("libraryfolders", node.Name);

        var folder0 = node["0"];
        Assert.NotNull(folder0);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folder0.GetString("path"));
        Assert.NotNull(folder0["apps"]);
        Assert.Equal("70234567890", folder0["apps"]?.GetString("1091500"));

        var folder1 = node["1"];
        Assert.NotNull(folder1);
        Assert.Equal(@"D:\SteamLibrary", folder1.GetString("path"));
        Assert.Equal("Secondary Drive", folder1.GetString("label"));
        Assert.Equal("49000000000", folder1["apps"]?.GetString("1245620"));
    }

    [Fact]
    public void Should_Parse_LoginUsers_Correctly()
    {
        var vdfText = """
        "users"
        {
            "76561198012345678"
            {
                "AccountName"        "gamer_player"
                "PersonaName"        "GamerPlayer"
                "MostRecent"        "1"
                "Timestamp"        "1700000000"
            }
        }
        """;

        var node = VdfParser.ParseText(vdfText);
        var userNode = node["76561198012345678"];

        Assert.NotNull(userNode);
        Assert.Equal("gamer_player", userNode.GetString("AccountName"));
        Assert.Equal("GamerPlayer", userNode.GetString("PersonaName"));
        Assert.True(userNode.GetBoolean("MostRecent"));
        Assert.Equal(1700000000L, userNode.GetInt64("Timestamp"));
    }
}
