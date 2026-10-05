using System;

namespace SteamSaveMigrator.Core.Models;

/// <summary>
/// Opções para conversão de caminhos e mapeamento de usuários (De: USUARIO_ANTIGO -> Para: NOVO_USUARIO).
/// </summary>
public record PathConversionOptions
{
    /// <summary>
    /// Nome do usuário de origem gravado no backup (ex: "USUARIO_ANTIGO").
    /// </summary>
    public string SourceUsername { get; init; } = string.Empty;

    /// <summary>
    /// Perfil original do usuário (ex: @"C:\Users\USUARIO_ANTIGO").
    /// </summary>
    public string SourceUserProfile { get; init; } = string.Empty;

    /// <summary>
    /// Nome do novo usuário de destino (ex: "NOVO_USUARIO" ou usuário atual).
    /// </summary>
    public string TargetUsername { get; init; } = Environment.UserName;

    /// <summary>
    /// Perfil do novo usuário de destino (ex: "C:\Users\NOVO_USUARIO" ou diretório atual do usuário).
    /// </summary>
    public string TargetUserProfile { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Caminho da Steam no sistema de destino.
    /// </summary>
    public string? TargetSteamPath { get; init; }

    /// <summary>
    /// SteamID3 de origem (se houver).
    /// </summary>
    public uint? SourceSteamId3 { get; init; }

    /// <summary>
    /// SteamID64 de origem (calculado a partir de SourceSteamId3 ou informado).
    /// </summary>
    public ulong? SourceSteamId64 => SourceSteamId3.HasValue ? (0x0110000100000000UL | SourceSteamId3.Value) : null;

    /// <summary>
    /// Nome de exibição da conta Steam de origem.
    /// </summary>
    public string? SourceSteamPersonaName { get; init; }

    /// <summary>
    /// SteamID3 de destino (caso o usuário queira migrar o save para outra conta Steam).
    /// </summary>
    public uint? TargetSteamId3 { get; init; }

    /// <summary>
    /// SteamID64 de destino (calculado a partir de TargetSteamId3).
    /// </summary>
    public ulong? TargetSteamId64 => TargetSteamId3.HasValue ? (0x0110000100000000UL | TargetSteamId3.Value) : null;

    /// <summary>
    /// Nome de exibição da conta Steam conectada de destino.
    /// </summary>
    public string? TargetSteamPersonaName { get; init; }

    /// <summary>
    /// Se verdadeiro, substitui arquivos existentes caso já estejam presentes.
    /// </summary>
    public bool OverwriteExisting { get; init; } = true;

    /// <summary>
    /// Se verdadeiro, simula a restauração sem gravar nada no disco (Dry Run).
    /// </summary>
    public bool DryRun { get; init; } = false;
}
