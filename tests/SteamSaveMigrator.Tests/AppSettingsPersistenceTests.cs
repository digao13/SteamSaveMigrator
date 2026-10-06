using System;
using System.IO;
using SteamSaveMigrator.Core.Services;
using Xunit;

namespace SteamSaveMigrator.Tests;

public class AppSettingsPersistenceTests
{
    [Theory]
    [InlineData("\"C:\\Backups\\Steam\"", "C:\\Backups\\Steam")]
    [InlineData("  D:\\Jogos\\Saves  ", "D:\\Jogos\\Saves")]
    [InlineData("'E:\\Backups'", "E:\\Backups")]
    public void NormalizeDirectoryPath_ShouldCleanQuotesAndWhitespace(string input, string expected)
    {
        var normalized = AppSettingsService.NormalizeDirectoryPath(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeDirectoryPath_ShouldExpandEnvironmentVariables()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var input = "%USERPROFILE%\\SteamBackups";
        var expected = Path.Combine(userProfile, "SteamBackups");

        var normalized = AppSettingsService.NormalizeDirectoryPath(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void UpdateCustomBackupDirectory_ShouldSaveAndBeRetrievable()
    {
        var testConfigFile = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_TestConfig_" + Guid.NewGuid().ToString("N"), "settings.json");
        var tempDir = Path.Combine(Path.GetTempPath(), "SteamSaveMigrator_TestDir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            AppSettingsService.OverrideConfigPathForTesting = testConfigFile;
            AppSettingsService.UpdateCustomBackupDirectory(tempDir);
            var effective = AppSettingsService.GetEffectiveBackupDirectory();

            Assert.Equal(tempDir, effective);
        }
        finally
        {
            AppSettingsService.OverrideConfigPathForTesting = null;
            if (File.Exists(testConfigFile))
            {
                File.Delete(testConfigFile);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
