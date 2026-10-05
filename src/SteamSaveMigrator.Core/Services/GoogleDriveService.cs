using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Text.Json;
using SteamSaveMigrator.Core.Models;
using SteamSaveMigrator.Core.Utils;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Implementação oficial do serviço Google Drive utilizando a API v3.
/// </summary>
public class GoogleDriveService : IGoogleDriveService
{
    private const string ApplicationName = "SteamSaveMigrator";
    private const string BackupFolderName = "SteamSaveMigrator_Backups";

    // Escopos de acesso para gerenciar pastas e backups do usuário
    private static readonly string[] Scopes =
    {
        DriveService.Scope.Drive,
        DriveService.Scope.DriveFile,
        "https://www.googleapis.com/auth/userinfo.email"
    };

    /// <summary>
    /// Credenciais OAuth para integração com o Google Drive.
    /// Em ambiente open-source, podem ser definidas através de:
    /// 1. Variáveis de ambiente: GOOGLE_CLIENT_ID e GOOGLE_CLIENT_SECRET
    /// 2. Arquivo client_secret.json ou credentials.json na pasta do aplicativo
    /// 3. Pela interface do usuário (botão "Configurar Credenciais do Google")
    /// </summary>
    public static string DefaultBuiltInClientId =>
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;

    public static string DefaultBuiltInClientSecret =>
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty;

    private string _clientId = DefaultBuiltInClientId;
    private string _clientSecret = DefaultBuiltInClientSecret;

    private readonly string _storageDataPath;
    private readonly string _commonStoragePath;
    private readonly string _cacheDownloadPath;
    private readonly bool _isCustomStorage;
    private DriveService? _driveService;
    private string? _cachedFolderId;

    public GoogleDriveService(string? customStoragePath = null)
    {
        var currentAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _isCustomStorage = true;
            _storageDataPath = customStoragePath;
            _commonStoragePath = Path.Combine(customStoragePath, "CommonAuth");
            _cacheDownloadPath = Path.Combine(customStoragePath, "CloudCache");
        }
        else
        {
            _isCustomStorage = false;
            // O DataStore do Google roda preferencialmente no %APPDATA% do usuário local para evitar
            // conflitos de lock e permissões NTFS entre diferentes contas do Windows.
            _storageDataPath = Path.Combine(currentAppData, "SteamSaveMigrator", "GoogleDriveAuth");
            _commonStoragePath = Path.Combine(commonData, "SteamSaveMigrator", "GoogleDriveAuth");
            _cacheDownloadPath = Path.Combine(currentAppData, "SteamSaveMigrator", "CloudCache");
        }

        try
        {
            if (!Directory.Exists(_storageDataPath)) Directory.CreateDirectory(_storageDataPath);
            if (!Directory.Exists(_cacheDownloadPath)) Directory.CreateDirectory(_cacheDownloadPath);
            if (!Directory.Exists(_commonStoragePath))
            {
                Directory.CreateDirectory(_commonStoragePath);
                AppSettingsService.EnsureDirectoryPermissions(_commonStoragePath);
            }
        }
        catch { }

        // Sincroniza credenciais e tokens entre o repositório comum e o perfil do usuário
        SyncAndMigrateUserCredentials();

        LoadSavedClientSecrets();
    }

    /// <summary>
    /// Sincroniza credenciais e tokens salvos entre o repositório comum e os perfis do usuário.
    /// Garante que se um usuário autenticou, o outro perfil do Windows na mesma máquina possa
    /// reutilizar o token automaticamente sem travamentos de permissão.
    /// </summary>
    private void SyncAndMigrateUserCredentials()
    {
        if (_isCustomStorage) return;

        try
        {
            // Assegura permissões no repositório comum compartilhado
            AppSettingsService.EnsureDirectoryPermissions(_commonStoragePath);

            var candidateFolders = new List<string>();

            // 1. Repositório compartilhado em ProgramData
            if (!string.IsNullOrWhiteSpace(_commonStoragePath) && Directory.Exists(_commonStoragePath))
            {
                candidateFolders.Add(_commonStoragePath);
            }

            // 2. Perfis em C:\Users
            var usersRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (!string.IsNullOrWhiteSpace(usersRoot) && Directory.Exists(usersRoot))
            {
                foreach (var userDir in Directory.GetDirectories(usersRoot))
                {
                    var userAuthPath = Path.Combine(userDir, "AppData", "Roaming", "SteamSaveMigrator", "GoogleDriveAuth");
                    if (Directory.Exists(userAuthPath))
                    {
                        candidateFolders.Add(userAuthPath);
                    }
                }
            }

            // A) Copia arquivos mais recentes de qualquer candidato para a pasta local do usuário
            foreach (var srcFolder in candidateFolders)
            {
                if (string.Equals(Path.GetFullPath(srcFolder), Path.GetFullPath(_storageDataPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var file in Directory.GetFiles(srcFolder))
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(_storageDataPath, fileName);

                    if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destFile))
                    {
                        try
                        {
                            File.Copy(file, destFile, true);
                        }
                        catch { }
                    }
                }
            }

            // B) Copia os arquivos locais para o repositório comum compartilhado
            if (Directory.Exists(_storageDataPath) && !string.IsNullOrWhiteSpace(_commonStoragePath) && Directory.Exists(_commonStoragePath))
            {
                foreach (var file in Directory.GetFiles(_storageDataPath))
                {
                    var fileName = Path.GetFileName(file);
                    var commonDest = Path.Combine(_commonStoragePath, fileName);

                    if (!File.Exists(commonDest) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(commonDest))
                    {
                        try
                        {
                            File.Copy(file, commonDest, true);
                            AppSettingsService.EnsureFilePermissions(commonDest);
                        }
                        catch { }
                    }
                }
            }
        }
        catch
        {
            // Silencioso em caso de restrições de permissão
        }
    }

    /// <summary>
    /// Propaga um token ou arquivo de credencial para o repositório comum compartilhado.
    /// </summary>
    private void PropagateToCommonStorage()
    {
        try
        {
            if (Directory.Exists(_storageDataPath) && !string.IsNullOrWhiteSpace(_commonStoragePath))
            {
                if (!Directory.Exists(_commonStoragePath))
                {
                    Directory.CreateDirectory(_commonStoragePath);
                    AppSettingsService.EnsureDirectoryPermissions(_commonStoragePath);
                }

                foreach (var file in Directory.GetFiles(_storageDataPath))
                {
                    var dest = Path.Combine(_commonStoragePath, Path.GetFileName(file));
                    try
                    {
                        File.Copy(file, dest, true);
                        AppSettingsService.EnsureFilePermissions(dest);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public bool HasConfiguredCredentials =>
        !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    public string? CurrentClientId => _clientId;

    public void ConfigureCredentials(string clientId, string clientSecret)
    {
        _clientId = clientId.Trim();
        _clientSecret = clientSecret.Trim();
        _driveService?.Dispose();
        _driveService = null;

        // Salva para reutilização futura tanto no diretório compartilhado quanto no %APPDATA%
        SaveClientSecretsToDisk(_clientId, _clientSecret);
    }

    private void SaveClientSecretsToDisk(string clientId, string clientSecret)
    {
        var targetPaths = new List<string>
        {
            Path.Combine(_storageDataPath, "client_secrets.cfg")
        };

        if (!_isCustomStorage && !string.IsNullOrWhiteSpace(_commonStoragePath))
        {
            targetPaths.Add(Path.Combine(_commonStoragePath, "client_secrets.cfg"));
        }

        foreach (var path in targetPaths)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllLines(path, new[] { clientId, clientSecret });
            }
            catch { }
        }
    }

    public void ConfigureClientSecrets(string clientId, string clientSecret) =>
        ConfigureCredentials(clientId, clientSecret);

    public bool ConfigureCredentialsFromJson(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath)) return false;

        try
        {
            var content = File.ReadAllText(jsonFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? id = null;
            string? secret = null;

            if (root.TryGetProperty("installed", out var installed))
            {
                if (installed.TryGetProperty("client_id", out var pId)) id = pId.GetString();
                if (installed.TryGetProperty("client_secret", out var pSec)) secret = pSec.GetString();
            }
            else if (root.TryGetProperty("web", out var web))
            {
                if (web.TryGetProperty("client_id", out var pId)) id = pId.GetString();
                if (web.TryGetProperty("client_secret", out var pSec)) secret = pSec.GetString();
            }
            else
            {
                if (root.TryGetProperty("client_id", out var pId)) id = pId.GetString();
                if (root.TryGetProperty("client_secret", out var pSec)) secret = pSec.GetString();
            }

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret))
            {
                ConfigureCredentials(id, secret);

                var destPaths = new List<string>
                {
                    Path.Combine(_storageDataPath, "credentials.json")
                };

                if (!_isCustomStorage && !string.IsNullOrWhiteSpace(_commonStoragePath))
                {
                    destPaths.Add(Path.Combine(_commonStoragePath, "credentials.json"));
                }

                foreach (var dest in destPaths)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                        if (!string.Equals(Path.GetFullPath(jsonFilePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(jsonFilePath, dest, true);
                        }
                    }
                    catch { }
                }

                return true;
            }
        }
        catch
        {
            // Falha ao interpretar JSON
        }

        return false;
    }

    private void LoadSavedClientSecrets()
    {
        try
        {
            // 1. Procura em client_secrets.cfg salvo no repositório comum e nos locais de busca
            var searchFolders = new List<string> { _storageDataPath };

            var currentAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(currentAppData))
            {
                searchFolders.Add(Path.Combine(currentAppData, "SteamSaveMigrator", "GoogleDriveAuth"));
            }

            foreach (var folder in searchFolders)
            {
                var secretsFile = Path.Combine(folder, "client_secrets.cfg");
                if (File.Exists(secretsFile))
                {
                    var lines = File.ReadAllLines(secretsFile);
                    if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[0]) && !string.IsNullOrWhiteSpace(lines[1]))
                    {
                        _clientId = lines[0].Trim();
                        _clientSecret = lines[1].Trim();
                        return;
                    }
                }
            }

            // 2. Procura credentials.json ou client_secret.json nos diretórios locais
            var searchPaths = new List<string>();
            foreach (var folder in searchFolders)
            {
                searchPaths.Add(Path.Combine(folder, "credentials.json"));
                searchPaths.Add(Path.Combine(folder, "client_secret.json"));
            }
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "credentials.json"));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "client_secret.json"));
            searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "credentials.json"));
            searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "client_secret.json"));

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    if (ConfigureCredentialsFromJson(path))
                    {
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            {
                _clientId = DefaultBuiltInClientId;
                _clientSecret = DefaultBuiltInClientSecret;
            }
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            {
                _clientId = DefaultBuiltInClientId;
                _clientSecret = DefaultBuiltInClientSecret;
            }
        }
    }

    private ClientSecrets GetClientSecrets()
    {
        if (HasConfiguredCredentials)
        {
            return new ClientSecrets
            {
                ClientId = _clientId,
                ClientSecret = _clientSecret
            };
        }

        throw new InvalidOperationException(
            "Credenciais do Google Cloud não configuradas. Obtenha seu Client ID e Client Secret (ou credentials.json) no Google Cloud Console e configure no SteamSaveMigrator.");
    }

    private CancellationTokenSource? _activeAuthCts;

    public void CancelAuthentication()
    {
        try
        {
            _activeAuthCts?.Cancel();
            _activeAuthCts?.Dispose();
        }
        catch { }
        finally
        {
            _activeAuthCts = null;
        }
    }

    private async Task<DriveService> GetDriveServiceAsync(bool forceLogin = false, CancellationToken cancellationToken = default)
    {
        if (_driveService != null && !forceLogin)
        {
            return _driveService;
        }

        var secrets = GetClientSecrets();
        var dataStore = new FileDataStore(_storageDataPath, true);

        UserCredential credential;

        if (forceLogin)
        {
            CancelAuthentication();
            _activeAuthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    Scopes,
                    "user",
                    _activeAuthCts.Token,
                    dataStore);

                // Propaga o token recém-gerado para o armazenamento comum
                PropagateToCommonStorage();
            }
            finally
            {
                _activeAuthCts = null;
            }
        }
        else
        {
            // Tenta obter credencial existente sem abrir o navegador
            var token = await dataStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
            if (token == null)
            {
                throw new InvalidOperationException("Usuário não autenticado no Google Drive. Faça login primeiro.");
            }

            credential = new UserCredential(
                new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = secrets,
                    Scopes = Scopes,
                    DataStore = dataStore
                }),
                "user",
                token);
        }

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });

        return _driveService;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dataStore = new FileDataStore(_storageDataPath, true);
            var token = await dataStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
            return token != null && (!string.IsNullOrEmpty(token.AccessToken) || !string.IsNullOrEmpty(token.RefreshToken));
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (!HasConfiguredCredentials)
        {
            LoadSavedClientSecrets();
            if (!HasConfiguredCredentials)
            {
                return false;
            }
        }

        try
        {
            var service = await GetDriveServiceAsync(forceLogin: true, cancellationToken);
            return service != null;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        _driveService?.Dispose();
        _driveService = null;
        _cachedFolderId = null;

        try
        {
            var dataStore = new FileDataStore(_storageDataPath, true);
            await dataStore.ClearAsync();

            var pathsToClean = new List<string> { _storageDataPath };
            if (!string.IsNullOrWhiteSpace(_commonStoragePath) && Directory.Exists(_commonStoragePath))
            {
                pathsToClean.Add(_commonStoragePath);
            }

            foreach (var path in pathsToClean)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "Google.Apis.Auth.OAuth2.Responses.TokenResponse*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    public async Task<string?> GetUserEmailAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);
            var aboutReq = service.About.Get();
            aboutReq.Fields = "user(displayName,emailAddress)";
            var about = await aboutReq.ExecuteAsync(cancellationToken);
            return about.User?.EmailAddress ?? about.User?.DisplayName;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetOrCreateBackupFolderAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedFolderId))
        {
            return _cachedFolderId;
        }

        var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);

        try
        {
            // Busca se a pasta já existe
            var listReq = service.Files.List();
            listReq.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{BackupFolderName}' and trashed = false";
            listReq.Fields = "files(id, name)";
            listReq.Spaces = "drive";
            var listRes = await listReq.ExecuteAsync(cancellationToken);

            var existingFolder = listRes.Files?.FirstOrDefault();
            if (existingFolder != null)
            {
                _cachedFolderId = existingFolder.Id;
                return _cachedFolderId;
            }
        }
        catch (Google.GoogleApiException ex) when (ex.Message.Contains("insufficient") || ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            await SignOutAsync(cancellationToken);
            throw new InvalidOperationException("O token anterior do Google Drive não possuía permissões suficientes para criar ou listar pastas de backup. A sessão foi reiniciada. Clique em 'Conectar Google Drive' novamente e certifique-se de marcar a caixa de permissão do Drive na tela do Google.", ex);
        }

        // Se não existe, cria a pasta
        var folderMeta = new Google.Apis.Drive.v3.Data.File
        {
            Name = BackupFolderName,
            MimeType = "application/vnd.google-apps.folder",
            Description = "Pasta de backups automáticos de jogos do SteamSaveMigrator"
        };

        var createReq = service.Files.Create(folderMeta);
        createReq.Fields = "id";
        var created = await createReq.ExecuteAsync(cancellationToken);

        _cachedFolderId = created.Id;
        return _cachedFolderId;
    }

    public async Task<CloudBackupInfo> UploadBackupAsync(
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
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localZipPath))
        {
            throw new FileNotFoundException($"Arquivo de backup local não encontrado: {localZipPath}");
        }

        // Se contagem de conquistas não foi passada, inspeciona o manifesto do zip local
        if (achievementsCount <= 0)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(localZipPath);
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry != null)
                {
                    using var stream = manifestEntry.Open();
                    using var reader = new StreamReader(stream);
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd());
                    if (manifest?.Achievements != null && manifest.Achievements.Count > 0)
                    {
                        achievementsCount = manifest.Achievements.Count;
                        achievementsUnlockedCount = manifest.Achievements.Count(a => a.IsUnlocked);
                    }
                }
            }
            catch
            {
                // Silencioso se der erro na inspeção do zip
            }
        }

        var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);
        var folderId = await GetOrCreateBackupFolderAsync(cancellationToken);

        var fileInfo = new FileInfo(localZipPath);
        var fileName = Path.GetFileName(localZipPath);
        var effectiveUser = !string.IsNullOrWhiteSpace(sourceUsername) ? sourceUsername : Environment.UserName;

        var properties = new Dictionary<string, string>
        {
            { "GameName", gameName },
            { "AppId", appId.ToString() },
            { "CreatedBy", ApplicationName },
            { "MachineName", Environment.MachineName },
            { "SourceUser", effectiveUser }
        };

        if (achievementsCount > 0)
        {
            properties["AchievementsCount"] = achievementsCount.ToString();
            properties["AchievementsUnlockedCount"] = achievementsUnlockedCount.ToString();
        }

        if (!string.IsNullOrWhiteSpace(sourceSteamPersona))
        {
            properties["SourceSteamPersona"] = sourceSteamPersona;
        }

        if (sourceSteamId3.HasValue && sourceSteamId3.Value > 0)
        {
            properties["SourceSteamId3"] = sourceSteamId3.Value.ToString();
        }

        if (sourceSteamId64.HasValue && sourceSteamId64.Value > 0)
        {
            properties["SourceSteamId64"] = sourceSteamId64.Value.ToString();
        }

        var achTag = achievementsCount > 0 ? $" - Conquistas: {achievementsUnlockedCount}/{achievementsCount}" : string.Empty;
        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new List<string> { folderId },
            Description = $"Backup de saves: {gameName} (AppID: {appId}) - Perfil Steam: {sourceSteamPersona ?? "Geral"}{achTag} - Usuário: {effectiveUser}",
            Properties = properties
        };

        await using var fileStream = new FileStream(localZipPath, FileMode.Open, FileAccess.Read);
        var createRequest = service.Files.Create(fileMetadata, fileStream, "application/zip");
        createRequest.Fields = "id, name, size, createdTime, webViewLink, properties";

        if (progress != null)
        {
            createRequest.ProgressChanged += uploadProgress =>
            {
                if (fileInfo.Length > 0 && uploadProgress.BytesSent > 0)
                {
                    var pct = (double)uploadProgress.BytesSent / fileInfo.Length * 100.0;
                    progress.Report(Math.Min(100.0, pct));
                }
            };
        }

        var uploadResult = await createRequest.UploadAsync(cancellationToken);
        if (uploadResult.Status == Google.Apis.Upload.UploadStatus.Failed)
        {
            throw uploadResult.Exception ?? new Exception("Falha no upload do arquivo para o Google Drive.");
        }

        var uploadedFile = createRequest.ResponseBody;

        return new CloudBackupInfo
        {
            FileId = uploadedFile.Id,
            FileName = uploadedFile.Name,
            GameName = gameName,
            AppId = appId,
            FileSizeBytes = fileInfo.Length,
            CreatedTime = uploadedFile.CreatedTimeDateTimeOffset?.LocalDateTime ?? DateTime.Now,
            SourceUsername = effectiveUser,
            SourceSteamPersona = sourceSteamPersona ?? string.Empty,
            SourceSteamId3 = sourceSteamId3,
            SourceSteamId64 = sourceSteamId64,
            SourceMachineName = Environment.MachineName,
            WebViewLink = uploadedFile.WebViewLink,
            AchievementsCount = achievementsCount,
            AchievementsUnlockedCount = achievementsUnlockedCount
        };
    }

    public async Task<List<CloudBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);
        var folderId = await GetOrCreateBackupFolderAsync(cancellationToken);

        var request = service.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = "files(id, name, size, createdTime, webViewLink, description, properties)";
        request.OrderBy = "createdTime desc";
        request.PageSize = 100;

        var result = await request.ExecuteAsync(cancellationToken);
        var list = new List<CloudBackupInfo>();

        if (result.Files == null) return list;

        foreach (var file in result.Files)
        {
            string gameName = "Jogo Desconhecido";
            uint appId = 0;
            string sourceUser = string.Empty;
            string sourceSteamPersona = string.Empty;
            uint? sourceSteamId3 = null;
            ulong? sourceSteamId64 = null;
            string machineName = string.Empty;

            int achCount = 0;
            int achUnlocked = 0;

            if (file.Properties != null)
            {
                if (file.Properties.TryGetValue("GameName", out var gName)) gameName = gName;
                if (file.Properties.TryGetValue("AppId", out var aId) && uint.TryParse(aId, out var idVal)) appId = idVal;
                if (file.Properties.TryGetValue("SourceUser", out var sUser)) sourceUser = sUser;
                if (file.Properties.TryGetValue("SourceSteamPersona", out var sPersona)) sourceSteamPersona = sPersona;
                if (file.Properties.TryGetValue("SourceSteamId3", out var sId3Str) && uint.TryParse(sId3Str, out var sId3)) sourceSteamId3 = sId3;
                if (file.Properties.TryGetValue("SourceSteamId64", out var sId64Str) && ulong.TryParse(sId64Str, out var sId64)) sourceSteamId64 = sId64;
                if (file.Properties.TryGetValue("MachineName", out var mName)) machineName = mName;
                if (file.Properties.TryGetValue("AchievementsCount", out var aCountStr) && int.TryParse(aCountStr, out var parsedCount)) achCount = parsedCount;
                if (file.Properties.TryGetValue("AchievementsUnlockedCount", out var aUnlStr) && int.TryParse(aUnlStr, out var parsedUnl)) achUnlocked = parsedUnl;
            }

            // Fallback 1: se não tem propriedades, tenta extrair da Descrição (ex: "Conquistas: 12/15")
            if (achCount == 0 && !string.IsNullOrWhiteSpace(file.Description))
            {
                var match = System.Text.RegularExpressions.Regex.Match(file.Description, @"Conquistas:\s*(\d+)/(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var u) && int.TryParse(match.Groups[2].Value, out var t))
                {
                    achUnlocked = u;
                    achCount = t;
                }
            }

            // Fallback caso propriedades não venham: deduz pelo nome do arquivo (ex: Hades2_1145350_20261005_120000.zip)
            if (appId == 0)
            {
                var parts = Path.GetFileNameWithoutExtension(file.Name).Split('_');
                if (parts.Length >= 2 && uint.TryParse(parts[1], out var parsedAppId))
                {
                    appId = parsedAppId;
                    gameName = parts[0];
                }
            }

            // Fallback 2: verificar se já temos uma cópia local deste arquivo com manifesto de conquistas
            if (achCount == 0)
            {
                try
                {
                    var settings = AppSettingsService.LoadSettings();
                    var candidateDir = !string.IsNullOrWhiteSpace(settings.CustomBackupDirectory) && Directory.Exists(settings.CustomBackupDirectory)
                        ? settings.CustomBackupDirectory
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamSavesBackup");

                    var localCandidate = Path.Combine(candidateDir, file.Name);
                    if (File.Exists(localCandidate))
                    {
                        using var archive = System.IO.Compression.ZipFile.OpenRead(localCandidate);
                        var manifestEntry = archive.GetEntry("manifest.json");
                        if (manifestEntry != null)
                        {
                            using var s = manifestEntry.Open();
                            using var r = new StreamReader(s);
                            var manifest = JsonSerializer.Deserialize<BackupManifest>(r.ReadToEnd());
                            if (manifest?.Achievements != null && manifest.Achievements.Count > 0)
                            {
                                achCount = manifest.Achievements.Count;
                                achUnlocked = manifest.Achievements.Count(a => a.IsUnlocked);
                            }
                        }
                    }
                }
                catch { }
            }

            list.Add(new CloudBackupInfo
            {
                FileId = file.Id,
                FileName = file.Name,
                GameName = gameName,
                AppId = appId,
                FileSizeBytes = file.Size ?? 0,
                CreatedTime = file.CreatedTimeDateTimeOffset?.LocalDateTime,
                SourceUsername = sourceUser,
                SourceSteamPersona = sourceSteamPersona,
                SourceSteamId3 = sourceSteamId3,
                SourceSteamId64 = sourceSteamId64,
                SourceMachineName = machineName,
                WebViewLink = file.WebViewLink,
                AchievementsCount = achCount,
                AchievementsUnlockedCount = achUnlocked
            });
        }

        return list;
    }

    public async Task<string> DownloadBackupAsync(
        string fileId,
        string? targetLocalZipPath = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);

        // Obter informações do arquivo para saber o nome e tamanho
        var getReq = service.Files.Get(fileId);
        getReq.Fields = "id, name, size";
        var fileMeta = await getReq.ExecuteAsync(cancellationToken);

        var destination = targetLocalZipPath;
        if (string.IsNullOrWhiteSpace(destination))
        {
            var fileName = !string.IsNullOrWhiteSpace(fileMeta.Name) ? fileMeta.Name : $"backup_{fileId}.zip";
            destination = Path.Combine(_cacheDownloadPath, fileName);
        }

        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write);
        var downloadReq = service.Files.Get(fileId);

        long totalBytes = fileMeta.Size ?? 0;
        if (progress != null && totalBytes > 0)
        {
            downloadReq.MediaDownloader.ProgressChanged += dlProgress =>
            {
                if (dlProgress.BytesDownloaded > 0)
                {
                    var pct = (double)dlProgress.BytesDownloaded / totalBytes * 100.0;
                    progress.Report(Math.Min(100.0, pct));
                }
            };
        }

        await downloadReq.DownloadAsync(fileStream, cancellationToken);
        return destination;
    }

    public async Task<bool> DeleteBackupAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetDriveServiceAsync(forceLogin: false, cancellationToken);
            await service.Files.Delete(fileId).ExecuteAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
