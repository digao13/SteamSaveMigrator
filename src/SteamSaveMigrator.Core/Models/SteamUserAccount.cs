namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Representa uma conta de usuário Steam detectada na máquina.
/// </summary>
public record SteamUserAccount
{
    public ulong SteamId64 { get; init; }
    public uint SteamId3 { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string PersonaName { get; init; } = string.Empty;
    public string UserdataPath { get; init; } = string.Empty;
    public bool MostRecent { get; init; }
    public bool IsActiveNow { get; init; }
    public DateTime? LastLoginTime { get; init; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(PersonaName)
            ? $"{PersonaName} ({AccountName} - ID3: {SteamId3}){(IsActiveNow ? " [Conectado]" : "")}"
            : $"Conta {AccountName} (ID3: {SteamId3}){(IsActiveNow ? " [Conectado]" : "")}";
}

