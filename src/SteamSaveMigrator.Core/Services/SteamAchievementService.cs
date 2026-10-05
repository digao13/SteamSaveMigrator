using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SteamSaveMigrator.Core.Models;

namespace SteamSaveMigrator.Core.Services;

/// <summary>
/// Serviço responsável pela detecção, backup e sincronização de conquistas da Steam (Achievements).
/// </summary>
public class SteamAchievementService
{
    private static readonly Regex AchievementTokenRegex = new(@"NEW_ACHIEVEMENT_(\d+)_(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Lê as conquistas de um jogo a partir dos schemas de estatísticas e do perfil da Steam.
    /// </summary>
    public List<GameAchievementInfo> GetGameAchievements(string? steamPath, uint appId, uint? steamId3)
    {
        var result = new List<GameAchievementInfo>();
        var baseSteam = !string.IsNullOrWhiteSpace(steamPath)
            ? steamPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

        var statsDir = Path.Combine(baseSteam, "appcache", "stats");
        if (!Directory.Exists(statsDir))
        {
            return result;
        }

        var schemaFile = Path.Combine(statsDir, $"UserGameStatsSchema_{appId}.bin");
        if (File.Exists(schemaFile))
        {
            try
            {
                result = ParseSchemaFile(schemaFile);
            }
            catch { }
        }

        // Se encontrou as conquistas no schema e temos o SteamId3, lê o status de desbloqueio
        if (steamId3.HasValue && steamId3.Value > 0)
        {
            var userStatsFile = Path.Combine(statsDir, $"UserGameStats_{steamId3.Value}_{appId}.bin");
            if (File.Exists(userStatsFile))
            {
                try
                {
                    var unlockedData = ParseUserStatsFile(userStatsFile);
                    for (int i = 0; i < result.Count; i++)
                    {
                        var ach = result[i];
                        bool isUnlocked = false;
                        DateTime timeUtc = default;

                        // 1. Chave precisa de grupo e bit: ex "2_7", "3_13", "1_1"
                        if (ach.BitId.HasValue)
                        {
                            var groupKey = $"{ach.GroupId ?? 1}_{ach.BitId.Value}";
                            if (unlockedData.TryGetValue(groupKey, out timeUtc))
                            {
                                isUnlocked = true;
                            }
                        }

                        // 2. Chave direta por ID da conquista: ex "ACH_MOSS2_WINDOW_ONE" ou "GetWristband"
                        if (!isUnlocked && !string.IsNullOrWhiteSpace(ach.Id) && unlockedData.TryGetValue(ach.Id, out timeUtc))
                        {
                            isUnlocked = true;
                        }

                        // 3. Chave baseada em StatToken caso GroupId não tenha sido resolvido
                        if (!isUnlocked && !string.IsNullOrWhiteSpace(ach.StatToken))
                        {
                            var match = AchievementTokenRegex.Match(ach.StatToken);
                            if (match.Success)
                            {
                                var tokenKey = $"{match.Groups[1].Value}_{match.Groups[2].Value}";
                                if (unlockedData.TryGetValue(tokenKey, out timeUtc))
                                {
                                    isUnlocked = true;
                                }
                            }
                        }

                        // 4. Fallback por BitId
                        if (!isUnlocked && ach.BitId.HasValue && !ach.GroupId.HasValue && unlockedData.TryGetValue(ach.BitId.Value.ToString(), out timeUtc))
                        {
                            isUnlocked = true;
                        }

                        if (isUnlocked)
                        {
                            result[i] = ach with
                            {
                                IsUnlocked = true,
                                UnlockTimeUtc = timeUtc
                            };
                        }
                    }
                }
                catch { }
            }
        }

        return result;
    }

    /// <summary>
    /// Sincroniza o estado de conquistas de um backup para a conta Steam de destino.
    /// Atualiza ou cria o arquivo UserGameStats_<targetSteamId3>_<appId>.bin com PendingChanges = 1
    /// para que a Steam envie a sincronização para os servidores da Valve.
    /// </summary>
    /// <summary>
    /// Sincroniza o estado de conquistas de um backup para a conta Steam de destino.
    /// Utiliza a Steamworks API oficial para registrar em tempo real na conta Steam ativa na Valve,
    /// e também atualiza o cache local UserGameStats_<targetSteamId3>_<appId>.bin como fallback.
    /// </summary>
    public AchievementSyncResult SyncAchievements(
        string? steamPath,
        uint appId,
        uint targetSteamId3,
        List<GameAchievementInfo> achievements,
        uint? sourceSteamId3 = null)
    {
        if (achievements == null || achievements.Count == 0)
        {
            return new AchievementSyncResult
            {
                IsSuccess = false,
                SyncedCount = 0,
                Message = "Nenhuma conquista disponível no backup para sincronizar."
            };
        }

        int targetUnlockedCount = achievements.Count(a => a.IsUnlocked);
        if (targetUnlockedCount == 0)
        {
            return new AchievementSyncResult
            {
                IsSuccess = true,
                SyncedCount = 0,
                Message = "O backup não possui conquistas desbloqueadas para este jogo."
            };
        }

        // 1. Sempre atualiza o cache local .bin em appcache/stats como garantia e consistência de disco
        WriteLocalStatsBinCache(steamPath, appId, targetSteamId3, achievements, sourceSteamId3);

        // 2. Sincronização oficial em tempo real através da Steamworks API / Steam Client
        try
        {
            var liveResult = TrySyncViaSteamworks(appId, targetSteamId3, achievements);
            if (liveResult != null)
            {
                return liveResult;
            }
        }
        catch { }

        // 3. Fallback se a Steam estiver fechada ou indisponível
        bool isSteamRunning = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
        string fallbackMsg = isSteamRunning
            ? $"Cache local atualizado ({targetUnlockedCount} conquistas). Certifique-se de estar conectado na conta correta na Steam e clique novamente em 'Sincronizar Conquistas'."
            : $"Cache local gerado ({targetUnlockedCount} conquistas). A Steam precisa estar aberta e logada na conta para registrar as conquistas nos servidores da Valve. Abra a Steam e clique em 'Sincronizar Conquistas'!";

        return new AchievementSyncResult
        {
            IsSuccess = true,
            SyncedCount = targetUnlockedCount,
            LiveSyncedViaSteamworks = false,
            Message = fallbackMsg
        };
    }

    /// <summary>
    /// Método de compatibilidade que retorna o número de conquistas sincronizadas.
    /// </summary>
    public int SyncAchievementsToTargetAccount(
        string? steamPath,
        uint appId,
        uint targetSteamId3,
        List<GameAchievementInfo> achievements,
        uint? sourceSteamId3 = null)
    {
        var result = SyncAchievements(steamPath, appId, targetSteamId3, achievements, sourceSteamId3);
        return result.SyncedCount;
    }

    private static AchievementSyncResult? TrySyncViaSteamworks(uint appId, uint targetSteamId3, List<GameAchievementInfo> achievements)
    {
        var unlockedList = achievements.Where(a => a.IsUnlocked).ToList();
        if (unlockedList.Count == 0) return null;

        // Tenta executar em processo filho dedicado para que a Steam libere o estado de "jogo em execução" imediatamente ao terminar
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath) && !exePath.Contains("testhost", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var tempInput = Path.Combine(Path.GetTempPath(), $"steam_ach_in_{Guid.NewGuid():N}.json");
                var tempOutput = Path.Combine(Path.GetTempPath(), $"steam_ach_out_{Guid.NewGuid():N}.json");

                File.WriteAllText(tempInput, System.Text.Json.JsonSerializer.Serialize(unlockedList));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--sync-achievements {appId} {targetSteamId3} \"{tempInput}\" \"{tempOutput}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    bool exited = proc.WaitForExit(12000);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                    }

                    if (File.Exists(tempOutput))
                    {
                        var outJson = File.ReadAllText(tempOutput);
                        var result = System.Text.Json.JsonSerializer.Deserialize<AchievementSyncResult>(outJson);
                        try { File.Delete(tempInput); } catch { }
                        try { File.Delete(tempOutput); } catch { }
                        if (result != null) return result;
                    }
                }
            }
            catch
            {
                // Fallback para execução direta in-process se não for possível iniciar o processo isolado
            }
        }

        return ExecuteDirectSteamworksSync(appId, targetSteamId3, achievements);
    }

    /// <summary>
    /// Executa a chamada à API oficial Steamworks para registrar as conquistas e descarrega as conexões.
    /// </summary>
    public static AchievementSyncResult? ExecuteDirectSteamworksSync(uint appId, uint targetSteamId3, List<GameAchievementInfo> achievements)
    {
        var unlockedList = achievements.Where(a => a.IsUnlocked).ToList();
        if (unlockedList.Count == 0) return null;

        string appidFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt");
        try { File.WriteAllText(appidFile, appId.ToString()); } catch { }

        try
        {
            if (Steamworks.SteamClient.IsValid)
            {
                try { Steamworks.SteamClient.Shutdown(); } catch { }
            }

            Steamworks.SteamClient.Init(appId);
            if (!Steamworks.SteamClient.IsValid)
            {
                return null;
            }

            var currentSteamId3 = Steamworks.SteamClient.SteamId.AccountId;
            var currentPersona = Steamworks.SteamClient.Name;

            // Se o usuário selecionou uma conta de destino específica e a conta logada na Steam for diferente:
            if (targetSteamId3 > 0 && currentSteamId3 != targetSteamId3)
            {
                try { Steamworks.SteamClient.Shutdown(); } catch { }
                return new AchievementSyncResult
                {
                    IsSuccess = false,
                    SyncedCount = 0,
                    LiveSyncedViaSteamworks = false,
                    ActiveConnectedPersona = currentPersona,
                    ActiveConnectedSteamId3 = currentSteamId3,
                    Message = $"Aviso: A conta atualmente aberta no aplicativo Steam é '{currentPersona}' (SteamID3: {currentSteamId3}), enquanto a conta de destino escolhida no migrador é {targetSteamId3}. Faça login na conta de destino na Steam e clique novamente em Sincronizar Conquistas!"
                };
            }

            Steamworks.SteamUserStats.RequestCurrentStats();

            // Bomba callbacks para receber as estatísticas da Steam
            for (int i = 0; i < 10; i++)
            {
                Steamworks.SteamClient.RunCallbacks();
                System.Threading.Thread.Sleep(30);
            }

            var targetIds = new HashSet<string>(unlockedList.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            int liveUnlocked = 0;

            foreach (var ach in Steamworks.SteamUserStats.Achievements)
            {
                if (targetIds.Contains(ach.Identifier))
                {
                    bool ok = ach.Trigger();
                    if (ok) liveUnlocked++;
                }
            }

            if (liveUnlocked > 0)
            {
                Steamworks.SteamUserStats.StoreStats();

                // Bomba callbacks para confirmar o salvamento nos servidores da Steam
                for (int i = 0; i < 15; i++)
                {
                    Steamworks.SteamClient.RunCallbacks();
                    System.Threading.Thread.Sleep(30);
                }
            }

            try { Steamworks.SteamClient.Shutdown(); } catch { }

            return new AchievementSyncResult
            {
                IsSuccess = true,
                SyncedCount = liveUnlocked > 0 ? liveUnlocked : unlockedList.Count,
                LiveSyncedViaSteamworks = true,
                ActiveConnectedPersona = currentPersona,
                ActiveConnectedSteamId3 = currentSteamId3,
                Message = $"🏆 Sucesso total! {liveUnlocked} conquista(s) desbloqueadas e registradas oficialmente na sua conta Steam '{currentPersona}' via Steamworks API! Verifique na sua biblioteca da Steam!"
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(appidFile)) File.Delete(appidFile); } catch { }
        }
    }

    private static void WriteLocalStatsBinCache(
        string? steamPath,
        uint appId,
        uint targetSteamId3,
        List<GameAchievementInfo> achievements,
        uint? sourceSteamId3 = null)
    {
        if (targetSteamId3 == 0) return;

        var baseSteam = !string.IsNullOrWhiteSpace(steamPath)
            ? steamPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

        var statsDir = Path.Combine(baseSteam, "appcache", "stats");
        if (!Directory.Exists(statsDir))
        {
            try { Directory.CreateDirectory(statsDir); } catch { return; }
        }

        var targetStatsFile = Path.Combine(statsDir, $"UserGameStats_{targetSteamId3}_{appId}.bin");

        try
        {
            if (sourceSteamId3.HasValue && sourceSteamId3.Value > 0)
            {
                var sourceStatsFile = Path.Combine(statsDir, $"UserGameStats_{sourceSteamId3.Value}_{appId}.bin");
                if (File.Exists(sourceStatsFile))
                {
                    File.Copy(sourceStatsFile, targetStatsFile, true);
                    SetPendingChangesInVdf(targetStatsFile);
                    File.SetLastWriteTimeUtc(targetStatsFile, DateTime.UtcNow);
                    return;
                }
            }

            if (File.Exists(targetStatsFile))
            {
                SetPendingChangesInVdf(targetStatsFile);
                File.SetLastWriteTimeUtc(targetStatsFile, DateTime.UtcNow);
                return;
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((byte)0);
            WriteNullTerminatedString(bw, "cache");

            bw.Write((byte)2);
            WriteNullTerminatedString(bw, "crc");
            bw.Write(0);

            bw.Write((byte)2);
            WriteNullTerminatedString(bw, "PendingChanges");
            bw.Write(1);

            var groups = achievements
                .Where(a => a.IsUnlocked)
                .GroupBy(a => a.GroupId ?? 1)
                .OrderBy(g => g.Key);

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var group in groups)
            {
                bw.Write((byte)0);
                WriteNullTerminatedString(bw, group.Key.ToString());

                bw.Write((byte)2);
                WriteNullTerminatedString(bw, "data");
                bw.Write(1);

                bw.Write((byte)2);
                WriteNullTerminatedString(bw, "state");
                bw.Write(2);

                bw.Write((byte)0);
                WriteNullTerminatedString(bw, "AchievementTimes");

                foreach (var ach in group)
                {
                    long timeVal = ach.UnlockTimeUtc.HasValue
                        ? new DateTimeOffset(ach.UnlockTimeUtc.Value).ToUnixTimeSeconds()
                        : nowUnix;

                    var keyName = ach.BitId.HasValue
                        ? ach.BitId.Value.ToString()
                        : (!string.IsNullOrWhiteSpace(ach.Id) ? ach.Id : "0");

                    bw.Write((byte)2);
                    WriteNullTerminatedString(bw, keyName);
                    bw.Write((int)timeVal);
                }

                bw.Write((byte)8);
                bw.Write((byte)8);
            }

            bw.Write((byte)8);

            File.WriteAllBytes(targetStatsFile, ms.ToArray());
            File.SetLastWriteTimeUtc(targetStatsFile, DateTime.UtcNow);
        }
        catch { }
    }

    private static void SetPendingChangesInVdf(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var token = Encoding.ASCII.GetBytes("PendingChanges\0");
            for (int i = 0; i < bytes.Length - token.Length - 4; i++)
            {
                bool match = true;
                for (int j = 0; j < token.Length; j++)
                {
                    if (bytes[i + j] != token[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    int valOffset = i + token.Length;
                    bytes[valOffset] = 1;
                    bytes[valOffset + 1] = 0;
                    bytes[valOffset + 2] = 0;
                    bytes[valOffset + 3] = 0;
                    break;
                }
            }
            File.WriteAllBytes(filePath, bytes);
        }
        catch { }
    }

    private static List<GameAchievementInfo> ParseSchemaFile(string schemaPath)
    {
        var list = new List<GameAchievementInfo>();
        var bytes = File.ReadAllBytes(schemaPath);
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        var stack = new Stack<string>();
        string? curName = null;
        string? curToken = null;
        string? curEnglish = null;
        string? curPtBr = null;
        string? curDesc = null;
        string? curDescPtBr = null;
        int? curGroupId = null;
        int? curBitId = null;
        bool curHidden = false;

        void CommitCurrent()
        {
            if (!string.IsNullOrWhiteSpace(curName) || curBitId.HasValue)
            {
                var display = !string.IsNullOrWhiteSpace(curPtBr) ? curPtBr : (!string.IsNullOrWhiteSpace(curEnglish) ? curEnglish : (curName ?? $"Conquista #{curBitId}"));
                var desc = !string.IsNullOrWhiteSpace(curDescPtBr) ? curDescPtBr : (curDesc ?? string.Empty);

                int? gId = curGroupId;
                int? bId = curBitId;

                if (!gId.HasValue || !bId.HasValue)
                {
                    var tokenToTest = curToken ?? curName;
                    if (!string.IsNullOrWhiteSpace(tokenToTest))
                    {
                        var match = AchievementTokenRegex.Match(tokenToTest);
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var g) && int.TryParse(match.Groups[2].Value, out var b))
                        {
                            gId ??= g;
                            bId ??= b;
                        }
                    }
                }

                list.Add(new GameAchievementInfo
                {
                    Id = curName ?? (bId.HasValue ? bId.Value.ToString() : string.Empty),
                    DisplayName = display,
                    Description = desc,
                    IsHidden = curHidden,
                    IsUnlocked = false,
                    StatToken = curToken,
                    GroupId = gId,
                    BitId = bId
                });
            }

            curName = null;
            curToken = null;
            curEnglish = null;
            curPtBr = null;
            curDesc = null;
            curDescPtBr = null;
            curGroupId = null;
            curBitId = null;
            curHidden = false;
        }

        while (ms.Position < ms.Length)
        {
            byte type = br.ReadByte();
            if (type == 8) // EndBlock
            {
                if (stack.Count > 0)
                {
                    var popped = stack.Pop();
                    var arr = stack.ToArray();
                    if (arr.Length >= 1 && arr[0].Equals("bits", StringComparison.OrdinalIgnoreCase) && int.TryParse(popped, out _))
                    {
                        CommitCurrent();
                    }
                }
                continue;
            }

            string key = ReadNullTerminatedString(br);
            if (type == 0) // SubBlock
            {
                stack.Push(key);
                var arr = stack.ToArray();
                if (arr.Length >= 2 && arr[1].Equals("bits", StringComparison.OrdinalIgnoreCase) && int.TryParse(key, out var bId))
                {
                    curBitId = bId;
                    if (arr.Length >= 3 && int.TryParse(arr[2], out var gId))
                    {
                        curGroupId = gId;
                    }
                    else
                    {
                        curGroupId = 1;
                    }
                }
            }
            else if (type == 1) // String
            {
                string val = ReadNullTerminatedString(br);
                var arr = stack.ToArray();

                if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    if (arr.Length == 0)
                    {
                        // Formato plano
                        CommitCurrent();
                        curName = val;
                    }
                    else if (arr.Contains("bits"))
                    {
                        if (curName == null) curName = val;
                    }
                }
                else if (key.Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    curToken = val;
                }
                else if (key.Equals("english", StringComparison.OrdinalIgnoreCase))
                {
                    if (arr.Length > 0 && arr[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                        curEnglish = val;
                    else if (arr.Length > 0 && arr[0].Equals("desc", StringComparison.OrdinalIgnoreCase))
                        curDesc = val;
                    else if (curEnglish == null)
                        curEnglish = val;
                    else
                        curDesc = val;
                }
                else if (key.Equals("brazilian", StringComparison.OrdinalIgnoreCase) || key.Equals("portuguese", StringComparison.OrdinalIgnoreCase))
                {
                    if (arr.Length > 0 && arr[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                        curPtBr = val;
                    else if (arr.Length > 0 && arr[0].Equals("desc", StringComparison.OrdinalIgnoreCase))
                        curDescPtBr = val;
                    else if (curPtBr == null)
                        curPtBr = val;
                    else
                        curDescPtBr = val;
                }
            }
            else if (type == 2) // Int32
            {
                int val = br.ReadInt32();
                if (key.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                {
                    curHidden = val == 1;
                }
            }
            else if (type == 7) // Int64
            {
                br.ReadInt64();
            }
        }

        CommitCurrent();
        return list;
    }

    private static Dictionary<string, DateTime> ParseUserStatsFile(string userStatsPath)
    {
        var dict = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var bytes = File.ReadAllBytes(userStatsPath);
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        var blockStack = new Stack<string>();

        while (ms.Position < ms.Length)
        {
            byte type = br.ReadByte();
            if (type == 8) // EndBlock
            {
                if (blockStack.Count > 0)
                {
                    blockStack.Pop();
                }
                continue;
            }

            var key = ReadNullTerminatedString(br);
            if (type == 0) // SubBlock
            {
                blockStack.Push(key);
            }
            else if (type == 1) // String
            {
                ReadNullTerminatedString(br);
            }
            else if (type == 2) // Int32
            {
                int val = br.ReadInt32();
                if (val > 0 && blockStack.Count >= 2)
                {
                    var stackArr = blockStack.ToArray();
                    if (string.Equals(stackArr[0], "AchievementTimes", StringComparison.OrdinalIgnoreCase))
                    {
                        var parentGroup = stackArr[1];
                        var timeUtc = DateTimeOffset.FromUnixTimeSeconds(val).UtcDateTime;

                        dict[$"{parentGroup}_{key}"] = timeUtc;
                        dict[key] = timeUtc;
                    }
                }
            }
            else if (type == 7) // Int64
            {
                br.ReadInt64();
            }
        }

        return dict;
    }

    private static string ReadNullTerminatedString(BinaryReader br)
    {
        var sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static void WriteNullTerminatedString(BinaryWriter bw, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        bw.Write(bytes);
        bw.Write((byte)0);
    }
}
