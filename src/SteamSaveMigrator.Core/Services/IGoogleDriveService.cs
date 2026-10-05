using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Contrato para serviços de integração com o Google Drive para backups de saves.
/// </summary>
public interface IGoogleDriveService
{
    /// <summary>
    /// Verifica se o usuário já possui token de autenticação válido.
    /// </summary>
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia o fluxo de autenticação OAuth2 no navegador local.
    /// </summary>
    Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela qualquer tentativa ativa de autenticação OAuth2 no navegador local, liberando listeners e sockets.
    /// </summary>
    void CancelAuthentication();

    /// <summary>
    /// Desconecta a conta e remove os tokens salvos localmente.
    /// </summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém o e-mail ou nome da conta Google conectada.
    /// </summary>
    Task<string?> GetUserEmailAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia um arquivo .zip de backup para a pasta do SteamSaveMigrator no Google Drive.
    /// </summary>
    Task<CloudBackupInfo> UploadBackupAsync(
        string localZipPath,
        string gameName,
        uint appId,
        string? sourceSteamPersona = null,
        uint? sourceSteamId3 = null,
        ulong? sourceSteamId64 = null,
        string? sourceUsername = null,
        int achievementsCount = 0,
        int achievementsUnlockedCount = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os backups de jogos armazenados no Google Drive.
    /// </summary>
    Task<List<CloudBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Baixa um arquivo de backup do Google Drive para o disco local.
    /// </summary>
    Task<string> DownloadBackupAsync(
        string fileId,
        string? targetLocalZipPath = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exclui um backup da nuvem pelo ID.
    /// </summary>
    Task<bool> DeleteBackupAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica se o cliente possui credenciais OAuth configuradas (Client ID e Secret válidos).
    /// </summary>
    bool HasConfiguredCredentials { get; }

    /// <summary>
    /// Client ID atualmente configurado.
    /// </summary>
    string? CurrentClientId { get; }

    /// <summary>
    /// Permite customizar as credenciais OAuth (Client ID e Secret).
    /// </summary>
    void ConfigureCredentials(string clientId, string clientSecret);

    /// <summary>
    /// Alias para ConfigureCredentials.
    /// </summary>
    void ConfigureClientSecrets(string clientId, string clientSecret);

    /// <summary>
    /// Configura credenciais importando o arquivo credentials.json ou client_secret.json baixado do Google Cloud Console.
    /// </summary>
    bool ConfigureCredentialsFromJson(string jsonFilePath);
}
