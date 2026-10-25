using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>跑房杀：自动进房→杀全部→退出→下一间，无限循环；全程实时写日志到 cheat.log。</summary>
    public static class ServerHopper
    {
        public static bool Enabled;              // 菜单按钮开关
        public static bool ScanMode;             // false=固定列表 true=公开扫描
        public static string LobbyIds = "";      // 固定列表：逗号分隔 lobbyID
        public static string ScanPrefix = "";    // 公开扫描：只进名字含此前缀
        public static float JoinTimeout = 10f;   // 进房超时(秒)
        public static bool LogEnabled = true;    // 日志开关

        private enum State { Idle, Joining, InGame, Leaving }

        private static State _state = State.Idle;
        private static readonly List<ulong> _queue = new List<ulong>();
        private static int _idx = -1;
        private static float _stateAt;
        private static int _phase;               // 0首杀 1补刀 2写日志
        private static string _curRoom = "";
        private static float _lastScanAt;
        private static string _logPath = "";
        private static Callback<LobbyEnter_t> _lobbyCb;   // Steam 进房结果回调
        private static uint _lastEnterResp;                // 0未到 1成功 其它失败

        public static string Status
        {
            get
            {
                if (!Enabled) return "关";
                string tag = _state switch
                {
                    State.Joining => "进房",
                    State.InGame => "杀中",
                    State.Leaving => "退出",
                    _ => "待机"
                };
                return tag + " " + (_idx + 1) + "/" + _queue.Count + (_curRoom.Length > 0 ? "·" + _curRoom : "");
            }
        }

        public static void Start()
        {
            if (Enabled) return;
            if (!ScanMode)
            {
                _queue.Clear();
                foreach (var s in LobbyIds.Split(new[] { ',', '，', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ulong.TryParse(s.Trim(), out var id) && id != 0) _queue.Add(id);
                }
                if (_queue.Count == 0)
                {
                    ScanMode = true;      // 没配ID → 自动扫公开房
                    Log("未配置房间ID，改为自动扫描");
                }
            }
            if (_lobbyCb == null)
            {
                try { _lobbyCb = Callback<LobbyEnter_t>.Create(OnLobbyEnter); }
                catch { }
            }
            Enabled = true;
            _idx = -1;
            _state = State.Idle;
            Log("跑房杀开启");
            Features.Notify("跑房杀 开启");
        }

        public static void Stop()
        {
            Enabled = false;
            if (_state != State.Idle)
            {
                try { NETController.Instance?.Disconnect(null); } catch { }
            }
            _state = State.Idle;
            _queue.Clear();
            _idx = -1;
            Log("跑房杀关闭");
            Features.Notify("跑房杀 已关闭");
        }

        public static void Update()
        {
            if (!Enabled) return;

            switch (_state)
            {
                case State.Idle:
                    if (!BackAtMenu())
                    {
                        try { NETController.Instance?.Disconnect(null); } catch { }
                    }
                    else if (_idx < 0)
                    {
                        NextRoom();
                    }
                    break;

                case State.Joining:
                    if (Features.InRound() || FindTargets().Count > 0)
                    {
                        _state = State.InGame;
                        _stateAt = Time.time;
                        _phase = 0;
                        FetchRoomName();
                        Log("进入房间 " + (_curRoom.Length > 0 ? _curRoom : "?"));
                        break;
                    }
                    // 房满/封禁/被拒 → 立即下一间（大厅拒绝 / 服务端拒绝）
                    string why = LastFailReason();
                    if (why.Length > 0 || NETController.LAST_NETWORK_ERROR != null)
                    {
                        FailAndNext("被拒: " + (why.Length > 0 ? why : "网络"));
                        break;
                    }
                    if (Time.time - _stateAt > JoinTimeout) { FailAndNext("进房超时"); break; }
                    break;

                case State.InGame:
                    switch (_phase)
                    {
                        case 0:   // 首杀
                            KillAll();
                            _phase = 1;
                            _stateAt = Time.time;
                            break;
                        case 1:   // 0.4s 补刀破 D-SAFE
                            if (Time.time - _stateAt >= 0.4f)
                            {
                                KillAll();
                                _phase = 2;
                                _stateAt = Time.time;
                            }
                            break;
                        case 2:   // 等血量同步完再写记录并退出
                            if (Time.time - _stateAt >= 0.6f)
                            {
                                WriteRoomLog();
                                Leave();
                            }
                            break;
                    }
                    break;

                case State.Leaving:
                    if (BackAtMenu()) NextRoom();
                    else if (Time.time - _stateAt > 20f) NextRoom();   // 兜底
                    break;
            }
        }

        private static void NextRoom()
        {
            if (!Enabled) return;
            if (_queue.Count == 0)
            {
                if (!ScanMode) { Stop(); return; }
                if (Time.time - _lastScanAt < 5f) return;   // 扫描冷却
                _idx = -1;
                ScanAndFill();
                return;
            }
            if (_idx + 1 >= _queue.Count)
            {
                if (ScanMode) { _idx = -1; ScanAndFill(); return; }
                _idx = -1;                                   // 固定列表：一轮完回绕
            }
            _idx++;
            JoinRoom(_queue[_idx]);
        }

        private static void ScanAndFill()
        {
            var sw = MonoController<SteamworksController>.Instance;
            if (sw == null) { Stop(); Features.Notify("跑房杀: Steamworks 不可用"); return; }
            _lastScanAt = Time.time;
            sw.SearchLobbies(list =>
            {
                if (!Enabled) return;
                _queue.Clear();
                foreach (var lb in list)
                {
                    if (string.IsNullOrEmpty(ScanPrefix) || lb.name.IndexOf(ScanPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                        _queue.Add(lb.id.m_SteamID);
                }
                Log("扫到 " + _queue.Count + " 间");
                NextRoom();
            });
        }

        private static void JoinRoom(ulong lobbyId)
        {
            _curRoom = lobbyId.ToString();
            _state = State.Joining;
            _stateAt = Time.time;
            var net = NETController.Instance;
            if (net == null) { FailAndNext("NET 不可用"); return; }
            _lastEnterResp = 0;
            Patches.Patch_JoinErr.Raw = null;
            NETController.LAST_NETWORK_ERROR = null;
            NETController.LOBBY_CONNECT_ID = lobbyId;
            Log("加入房间 " + lobbyId);
            try { net.StartCoroutine(net.StartNetwork()); }
            catch { FailAndNext("连接启动失败"); }
        }

        // Steam 大厅进房回调：记录结果码（1成功，其它失败）
        private static void OnLobbyEnter(LobbyEnter_t p)
        {
            _lastEnterResp = p.m_EChatRoomEnterResponse;
        }

        // 进房失败原因：先看服务端拒绝原始 key，再看 Steam 大厅结果码
        private static string LastFailReason()
        {
            var raw = Patches.Patch_JoinErr.Raw;
            if (!string.IsNullOrEmpty(raw))
            {
                if (raw.Contains("server-full")) return "房满";
                if (raw.Contains("banned")) return "被禁";
                if (raw.Contains("version")) return "版本不符";
                if (raw.Contains("invalid-host")) return "房无效";
                if (raw.Contains("lobby")) return "大厅失败";
                if (raw.Contains("auth")) return "认证失败";
                if (raw.Contains("generic")) return "拒绝";
                return raw;
            }
            switch (_lastEnterResp)
            {
                case 2: return "房不存在";
                case 3: return "未授权";
                case 4: return "房满";
                case 6: return "被禁";
                case 7: return "受限";
                case 15: return "频控";
                default: return "";
            }
        }

        private static void KillAll()
        {
            var ts = FindTargets();
            if (ts.Count > 0) Log("击杀 " + ts.Count + " 名玩家");
            foreach (var p in ts)
            {
                try { p.TakeHealthRPC(byte.MaxValue, DamageType.GENERIC); } catch { }
            }
        }

        private static void Leave()
        {
            _state = State.Leaving;
            _stateAt = Time.time;
            Log("退出房间");
            try { NETController.Instance?.Disconnect(null); } catch { }
        }

        private static void FailAndNext(string why)
        {
            Log("失败: " + why);
            Features.Notify("跑房杀: " + why);
            try { NETController.Instance?.Disconnect(null); } catch { }
            _state = State.Leaving;
            _stateAt = Time.time;
        }

        private static void FetchRoomName()
        {
            try
            {
                var conn = NETController.LOBBY_CONNECT_ID;
                if (conn == null) return;
                string n = SteamMatchmaking.GetLobbyData(new CSteamID(conn.Value), "Name");
                if (!string.IsNullOrEmpty(n)) _curRoom = n;
            }
            catch { }
        }

        private static bool BackAtMenu()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsListening || nm.IsConnectedClient || nm.IsServer)) return false;
            if (NETController.LOBBY_CONNECT_ID != null) return false;
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MAINMENU";
        }

        private static List<entity_player> FindTargets()
        {
            var list = new List<entity_player>();
            var nm = NetworkManager.Singleton;
            if (nm == null) return list;
            foreach (var kv in nm.SpawnManager.SpawnedObjects)
            {
                var no = kv.Value;
                if (no == null || !no.IsSpawned) continue;
                if (no.IsOwner) continue;                  // 本地玩家
                var p = no.GetComponent<entity_player>();
                if (p != null) list.Add(p);
            }
            return list;
        }

        /// <summary>每间房的记录：房间名/玩家数量/所有玩家名/每人状态。</summary>
        private static void WriteRoomLog()
        {
            if (!LogEnabled) return;
            try
            {
                var local = PlayerController.LOCAL;
                var all = new List<entity_player>();
                var pc = MonoController<PlayerController>.Instance;
                if (pc != null)
                {
                    var a = pc.GetAllPlayers();
                    if (a != null) all.AddRange(a);
                }

                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine("]");
                sb.AppendLine("房间名：" + _curRoom);
                sb.AppendLine("玩家数量：" + all.Count);
                sb.Append("所有玩家名：");
                for (int i = 0; i < all.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    try { sb.Append(all[i].GetPlayerName()); } catch { sb.Append('?'); }
                }
                sb.AppendLine();
                sb.AppendLine("状态");
                foreach (var p in all)
                {
                    try
                    {
                        bool dead = p.IsDead();
                        bool me = local != null && p == local;
                        sb.AppendLine(p.GetPlayerName() + (dead ? " 已死亡" : " 存活") + (me ? " (本地)" : ""));
                    }
                    catch { }
                }
                sb.AppendLine("----------");
                File.AppendAllText(_logPath, sb.ToString());
            }
            catch { }
        }

        /// <summary>实时写一行到 cheat.log（游戏根目录）。</summary>
        private static void Log(string line)
        {
            if (!LogEnabled) return;
            try
            {
                if (_logPath.Length == 0) _logPath = BuildLogPath();
                File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine);
            }
            catch { }
        }

        private static string BuildLogPath()
        {
            var dir = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(dir ?? ".", "cheat.log");
        }
    }
}
