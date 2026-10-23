using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Representa um jogo e local de save detectado a partir de correções/emuladores non-Steam
/// (CODEX, PLAZA, CPY, HOODLUM, Rune, Voices38, Goldberg, EMPRESS, etc.).
/// </summary>
public class NonSteamGameSave : INotifyPropertyChanged
{
    private string _gameName = string.Empty;
    private bool _isSelected = true;

    public uint AppId { get; set; }

    public string GameName
    {
        get => _gameName;
        set
        {
            if (_gameName != value)
            {
                _gameName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Identificação da correção ou emulador (ex: CODEX, Rune, PLAZA, CPY, HOODLUM, Voices38, Goldberg, EMPRESS, etc.).
    /// </summary>
    public string CrackSource { get; set; } = string.Empty;

    /// <summary>
    /// Caminho da pasta raiz onde os saves desse jogo estão localizados.
    /// </summary>
    public string SavePath { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    public DateTime? LastModified { get; set; }

    public List<SaveFileInfo> Files { get; set; } = new();

    public string Notes { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string AppIdDisplay => AppId > 0 ? AppId.ToString() : "N/D";

    public string SizeFormatted
    {
        get
        {
            if (TotalSizeBytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = TotalSizeBytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:0.##} {units[unitIndex]}";
        }
    }

    public string LastModifiedDisplay => LastModified.HasValue
        ? LastModified.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        : "Desconhecido";

    /// <summary>
    /// Converte este save non-steam em uma representação SteamGame compatível com o motor de migração.
    /// </summary>
    public SteamGame ToSteamGame()
    {
        var locations = new List<GameSaveLocation>
        {
            new()
            {
                LocationType = SaveLocationType.Custom,
                DisplayName = $"Save Non-Steam ({CrackSource})",
                SourcePath = SavePath,
                TokenizedPath = SavePath,
                Exists = true,
                FileCount = FileCount,
                TotalSizeBytes = TotalSizeBytes,
                Files = Files
            }
        };

        return new SteamGame
        {
            AppId = AppId,
            Name = !string.IsNullOrWhiteSpace(GameName) ? GameName : (AppId > 0 ? $"Jogo AppID {AppId}" : "Jogo Non-Steam"),
            InstallDir = string.Empty,
            InstallPath = string.Empty,
            ManifestPath = string.Empty,
            LibraryPath = string.Empty,
            SizeOnDisk = TotalSizeBytes,
            DetectedSaveLocations = locations,
            IsSelected = IsSelected
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
