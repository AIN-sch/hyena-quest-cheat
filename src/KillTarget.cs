using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>秒杀/操控玩家：远程 RPC 免校验（杀/推/控血/抢物/满袋/碎玻璃）。</summary>
    public static class KillTarget
    {
        private static readonly List<entity_player> _players = new List<entity_player>();
        private static float _refreshAt;
        private const float RefreshInterval = 1f;

        // 循环杀：playerID -> 下次杀的时间
        private static readonly Dictionary<byte, float> _loopAt = new Dictionary<byte, float>();
        private const float LoopInterval = 0.5f;

        // 连推：playerID -> 下次推的时间
        private static readonly Dictionary<byte, float> _shoveLoopAt = new Dictionary<byte, float>();
        private const float ShoveInterval = 0.2f;
        private const float ShoveForce = 55f;

        // 控血循环：playerID -> (目标血量, 下次时间)。循环满血/循环1血共用，后开的覆盖先开的。
        private struct BloodLoop { public byte target; public float at; }
        private static readonly Dictionary<byte, BloodLoop> _bloodLoop = new Dictionary<byte, BloodLoop>();
        private const float BloodInterval = 0.5f;

        // 循环抢物：playerID -> 下次抢的时间
        private static readonly Dictionary<byte, float> _stealLoopAt = new Dictionary<byte, float>();
        private const float StealInterval = 1.2f;

        // 循环丢包：playerID -> 下次丢的时间
        private static readonly Dictionary<byte, float> _dropLoopAt = new Dictionary<byte, float>();
        private const float DropInterval = 1f;

        // 补刀队列（破 D-SAFE 保命道具）
        private struct ReKill { public entity_player target; public float at; }
        private static readonly List<ReKill> _reKills = new List<ReKill>();

        // 抢物队列：抢所有权 → 等 IsOwner → 传脚下 → 放下
        private struct StealOp { public entity_phys phys; public Vector3 to; public float at; public int tries; }
        private static readonly List<StealOp> _steals = new List<StealOp>();

        // 抢物(背包)：物品被丢出脱手后，逐个抢所有权传到本地脚下
        private struct PendingSteal { public List<NetworkBehaviourReference> refs; public Vector3 to; public float at; public int tries; }
        private static readonly List<PendingSteal> _pendingSteals = new List<PendingSteal>();

        /// <summary>当前列表（只读给 UI）。</summary>
        public static IReadOnlyList<entity_player> Players => _players;

        /// <summary>每帧调用：刷新列表 + 循环杀 + 连推 + 补刀 + 抢物。</summary>
        public static void Update()
        {
            // ---- 列表刷新（只换数据，不碰 UI 滚动位置）----
            if (Time.time >= _refreshAt)
            {
                _refreshAt = Time.time + RefreshInterval;
                _players.Clear();
                var pc = MonoController<PlayerController>.Instance;
                var local = PlayerController.LOCAL;
                if (pc != null)
                {
                    var all = pc.GetAllPlayers();
                    if (all != null)
                    {
                        foreach (var p in all)
                        {
                            if (p == null || !p.IsSpawned) continue;
                            if (local != null && p == local) continue;   // 排除本地
                            _players.Add(p);
                        }
                    }
                }
            }

            // ---- 补刀（杀一次后的第二次补刀，破 D-SAFE）----
            for (int i = _reKills.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _reKills[i].at)
                {
                    var t = _reKills[i].target;
                    if (t != null) ExecuteKill(t);
                    _reKills.RemoveAt(i);
                }
            }

            // ---- 抢物：等所有权到位后传送+放下 ----
            for (int i = _steals.Count - 1; i >= 0; i--)
            {
                var s = _steals[i];
                if (Time.time < s.at) continue;
                if (s.tries > 8 || s.phys == null) { _steals.RemoveAt(i); continue; }
                if (s.phys.IsOwner)
                {
                    s.phys.transform.position = s.to;      // Owner 写 NetworkTransform，无校验
                    Physics.SyncTransforms();
                    try { s.phys.SetGrabbing(false); } catch { }
                    _steals.RemoveAt(i);
                    continue;
                }
                s.at = Time.time + 0.3f;                   // 还没拿到所有权，再等
                s.tries++;
                _steals[i] = s;
            }

            // ---- 抢物(背包)：物品脱手后逐个抢所有权传到本地脚下 ----
            for (int i = _pendingSteals.Count - 1; i >= 0; i--)
            {
                var ps = _pendingSteals[i];
                if (Time.time < ps.at) continue;
                if (ps.tries > 10 || ps.refs.Count == 0) { _pendingSteals.RemoveAt(i); continue; }

                var remaining = new List<NetworkBehaviourReference>();
                foreach (var r in ps.refs)
                {
                    var item = NETController.Get<entity_item_pickable>(r);
                    if (item == null || !item.IsSpawned) continue;          // 已消失，跳过
                    if (item.IsBeingGrabbed() || item.HasOwner() || item.IsLocked())
                    {
                        remaining.Add(r);                                    // 还没脱手/被锁，下轮再试
                        continue;
                    }
                    try
                    {
                        item.SetGrabbing(true);                              // 抢所有权 → 走 _steals 队列传脚下
                        _steals.Add(new StealOp { phys = item, to = ps.to, at = Time.time + 0.3f, tries = 0 });
                    }
                    catch { remaining.Add(r); }
                }
                ps.refs = remaining;
                ps.at = Time.time + 0.4f;
                ps.tries++;
                _pendingSteals[i] = ps;
            }

            // ---- 循环杀 ----
            if (_loopAt.Count > 0) DriveLoopKill();

            // ---- 连推 ----
            if (_shoveLoopAt.Count > 0) DriveShoveLoop();

            // ---- 控血循环 ----
            if (_bloodLoop.Count > 0) DriveBloodLoop();

            // ---- 循环抢物 ----
            if (_stealLoopAt.Count > 0) DriveStealLoop();

            // ---- 循环丢包 ----
            if (_dropLoopAt.Count > 0) DriveDropLoop();
        }

        // ============ 秒杀 ============

        /// <summary>目标是否正被循环杀。</summary>
        public static bool IsLooping(entity_player p)
            => p != null && _loopAt.ContainsKey(p.GetPlayerID());

        /// <summary>开关对某玩家的循环杀。</summary>
        public static void SetLoop(entity_player p, bool on)
        {
            if (p == null) return;
            var id = p.GetPlayerID();
            if (on) { if (!_loopAt.ContainsKey(id)) _loopAt[id] = Time.time + 0.2f; }
            else _loopAt.Remove(id);
        }

        /// <summary>杀一次：立即一杀 + 0.2 秒后补刀（目标带 D-SAFE 时第一杀会被保活，补刀必死）。</summary>
        public static void KillOnce(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            ExecuteKill(p);
            _reKills.Add(new ReKill { target = p, at = Time.time + 0.2f });
            Features.Notify("已秒杀 " + p.GetPlayerName());
        }

        /// <summary>杀掉场上所有玩家。</summary>
        public static void KillAll()
        {
            int n = 0;
            foreach (var p in _players)
            {
                if (p == null || !p.IsSpawned) continue;
                ExecuteKill(p);
                _reKills.Add(new ReKill { target = p, at = Time.time + 0.2f });
                n++;
            }
            Features.Notify("已秒杀全部 " + n + " 名玩家");
        }

        // ============ 推飞 ============

        /// <summary>单次推飞目标（推离本地，带一点向上）。</summary>
        public static void ShoveOnce(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            var local = PlayerController.LOCAL;
            Vector3 away = local ? (p.transform.position - local.transform.position) : Vector3.forward;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            try { p.ShoveRPC(away + Vector3.up * 0.5f, ShoveForce); }
            catch (System.Exception e) { Features.Notify("推飞失败: " + e.Message); }
        }

        /// <summary>目标是否正被连推。</summary>
        public static bool IsShoveLooping(entity_player p)
            => p != null && _shoveLoopAt.ContainsKey(p.GetPlayerID());

        /// <summary>开关对某玩家的连推（持续推上天）。</summary>
        public static void SetShoveLoop(entity_player p, bool on)
        {
            if (p == null) return;
            var id = p.GetPlayerID();
            if (on) { if (!_shoveLoopAt.ContainsKey(id)) _shoveLoopAt[id] = Time.time + 0.1f; }
            else _shoveLoopAt.Remove(id);
        }

        // ============ 控血循环（循环满血 / 循环1血，共用一套）============

        /// <summary>目标是否正被控血循环。</summary>
        public static bool IsBloodLooping(entity_player p)
            => p != null && _bloodLoop.ContainsKey(p.GetPlayerID());

        /// <summary>当前控血循环的目标血量（满血=100 / 1血=1）。</summary>
        public static byte GetBloodLoopTarget(entity_player p)
        {
            if (p == null) return 0;
            _bloodLoop.TryGetValue(p.GetPlayerID(), out var b);
            return b.target;
        }

        /// <summary>开启控血循环（目标血量），已开则覆盖目标。</summary>
        public static void SetBloodLoop(entity_player p, byte target)
        {
            if (p == null) return;
            _bloodLoop[p.GetPlayerID()] = new BloodLoop { target = target, at = Time.time + 0.2f };
        }

        /// <summary>关闭控血循环。</summary>
        public static void StopBloodLoop(entity_player p)
        {
            if (p == null) return;
            _bloodLoop.Remove(p.GetPlayerID());
        }

        // ============ 循环抢物 ============

        /// <summary>目标是否正被循环抢物。</summary>
        public static bool IsStealLooping(entity_player p)
            => p != null && _stealLoopAt.ContainsKey(p.GetPlayerID());

        public static void SetStealLoop(entity_player p, bool on)
        {
            if (p == null) return;
            var id = p.GetPlayerID();
            if (on) { if (!_stealLoopAt.ContainsKey(id)) _stealLoopAt[id] = Time.time + 0.5f; }
            else _stealLoopAt.Remove(id);
        }

        // ============ 循环丢包 ============

        /// <summary>目标是否正被循环丢包。</summary>
        public static bool IsDropLooping(entity_player p)
            => p != null && _dropLoopAt.ContainsKey(p.GetPlayerID());

        public static void SetDropLoop(entity_player p, bool on)
        {
            if (p == null) return;
            var id = p.GetPlayerID();
            if (on) { if (!_dropLoopAt.ContainsKey(id)) _dropLoopAt[id] = Time.time + 0.5f; }
            else _dropLoopAt.Remove(id);
        }

        /// <summary>全员单次推飞。</summary>
        public static void ShoveAllOnce()
        {
            int n = 0;
            foreach (var p in _players) { if (p != null && p.IsSpawned) { ShoveOnce(p); n++; } }
            Features.Notify("已推飞全部 " + n + " 名玩家");
        }

        /// <summary>全员连推开关。</summary>
        public static void ShoveLoopAll(bool on)
        {
            foreach (var p in _players) SetShoveLoop(p, on);
            Features.Notify(on ? "已开启全员连推" : "已关闭全员连推");
        }

        // ============ 控血 ============

        /// <summary>把目标血量钉到指定值（1=1血，100=满血，0=秒杀）。0 会被 D-SAFE 挡一次。</summary>
        public static void SetBlood(entity_player p, byte val)
        {
            if (p == null || !p.IsSpawned) return;
            try { p.SetHealthRPC(val); }
            catch (System.Exception e) { Features.Notify("控血失败: " + e.Message); }
        }

        /// <summary>全员控血。</summary>
        public static void SetBloodAll(byte val)
        {
            foreach (var p in _players) SetBlood(p, val);
            Features.Notify("已把全场玩家血量设为 " + val);
        }

        // ============ 抢物到脚下 ============

        /// <summary>抢目标全部物资到本地脚下：手里物抢所有权；背包物丢出脱手后抢回。</summary>
        public static void StealItems(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            var local = PlayerController.LOCAL;
            Vector3 footPos = local ? (local.transform.position + local.transform.forward * 1.5f + Vector3.up * 0.3f) : Vector3.zero;
            int n = 0;

            // 目标手里正抓的物理物 → 抢所有权（CanGrab 只查锁定）→ 传脚下
            foreach (var ph in Object.FindObjectsByType<entity_phys>())
            {
                if (ph == null || !ph.IsSpawned) continue;
                entity_player grab = ph.GetGrabbingOwner();
                if (grab == null || grab != p) continue;
                try { ph.SetGrabbing(true); }      // 无距离校验，ChangeOwnership 到本地
                catch { continue; }
                _steals.Add(new StealOp { phys = ph, to = footPos, at = Time.time + 0.3f, tries = 0 });
                n++;
            }

            // 2. 目标背包物品 → 强制丢出 → 等脱手后抢到脚下
            var inv = p.GetInventory();
            if (inv != null)
            {
                var refs = SnapshotInventoryRefs(inv);
                if (refs.Count > 0)
                {
                    foreach (var r in refs) { try { inv.DropItemRPC(r); n++; } catch { } }
                    _pendingSteals.Add(new PendingSteal { refs = refs, to = footPos, at = Time.time + 0.5f, tries = 0 });
                }
            }
            Features.Notify("抢了 " + p.GetPlayerName() + " 的 " + n + " 件");
        }

        /// <summary>抢所有玩家的背包物品到本地脚下。</summary>
        public static void StealAll()
        {
            int n = 0;
            foreach (var p in _players) { if (p != null && p.IsSpawned) StealItems(p); n++; }
            Features.Notify("抢了 " + n + " 名玩家的货");
        }

        // ============ 丢包 ============

        /// <summary>强制目标丢出全部背包物品。</summary>
        public static void DropAllItems(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            var inv = p.GetInventory();
            if (inv == null) return;
            var refs = SnapshotInventoryRefs(inv);
            if (refs.Count == 0) { Features.Notify(p.GetPlayerName() + " 背包是空的"); return; }
            foreach (var r in refs)
            {
                try { inv.DropItemRPC(r); }
                catch { }
            }
            Features.Notify("清空 " + p.GetPlayerName() + " 的包 (" + refs.Count + " 件)");
        }

        /// <summary>读目标 NetworkList 背包解析有效物品引用（Everyone 可读、不去重）。</summary>
        private static List<NetworkBehaviourReference> SnapshotInventoryRefs(entity_player_inventory inv)
        {
            var list = new List<NetworkBehaviourReference>();
            try
            {
                foreach (var r in inv.GetInventory())
                {
                    if (NETController.Get<entity_item_pickable>(r) == null) continue;   // 空槽 / 已失效
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        /// <summary>强制所有玩家丢光背包。</summary>
        public static void DropAllPlayers()
        {
            foreach (var p in _players) { if (p != null && p.IsSpawned) DropAllItems(p); }
            Features.Notify("全场背包已清空");
        }

        // ============ 一键满袋 ============

        /// <summary>一键填满真空袋：房主 SetScrap 秒满；客户端仅提示。</summary>
        public static void RefillBag()
        {
            var local = PlayerController.LOCAL;
            if (local == null) return;
            var vacuum = local.GetVacuum();
            if (!vacuum) { Features.Notify("没拿到真空袋"); return; }
            var bag = vacuum.GetVacuumHolder();
            if (!bag) { Features.Notify("没拿到真空袋实体"); return; }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                try
                {
                    var sc = NetController<ScrapController>.Instance;
                    int max = sc != null ? sc.GetMaxContainerScrap() : 200;
                    bag.SetScrap(max);
                    Features.Notify("一键满袋：废料已满 (" + max + ")");
                }
                catch (System.Exception e) { Features.Notify("满袋失败: " + e.Message); }
            }
            else
            {
                Features.Notify("满袋需要房主(主机)。客户端只能隔空吸废料");
            }
        }

        // ============ 远程破坏 ============

        /// <summary>全场玻璃全碎。</summary>
        public static void BreakAllGlass()
        {
            var all = Object.FindObjectsByType<entity_glass>();
            int n = 0;
            foreach (var g in all)
            {
                if (g == null) continue;
                if (InvokePrivate(typeof(entity_glass), "OnBreakRPC", g, g.transform.position)) n++;
            }
            Features.Notify("已碎 " + n + " 块玻璃");
        }

        /// <summary>全场低重力板开关。</summary>
        public static void ToggleAllLowgrav()
        {
            var all = Object.FindObjectsByType<entity_item_lowgrav>();
            int n = 0;
            foreach (var lg in all)
            {
                if (lg == null) continue;
                if (InvokePrivate(typeof(entity_item_lowgrav), "ToggleRPC", lg)) n++;
            }
            Features.Notify("已切 " + n + " 块低重力板");
        }

        // ============ 拉人（房主） ============

        /// <summary>房主：把目标拉到本地玩家身边。</summary>
        public static void PullToSelf(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            { Features.Notify("拉人需要房主(主机)，当前为客户端"); return; }
            var local = PlayerController.LOCAL;
            if (local == null) return;
            try
            {
                p.SetPositionRPC(local.transform.position + local.transform.forward * 1.5f, local.transform.rotation);
                Features.Notify("已把 " + p.GetPlayerName() + " 拉到身边");
            }
            catch (System.Exception e) { Features.Notify("拉人失败: " + e.Message); }
        }

        /// <summary>房主：把场上所有玩家拉到本地身边。</summary>
        public static void PullAllToSelf()
        {
            foreach (var p in _players) PullToSelf(p);
        }

        // ============ 自杀（仅手动） ============

        /// <summary>自杀：仅手动触发，独立方法，不被任何自动流程调用。</summary>
        public static void Suicide()
        {
            var local = PlayerController.LOCAL;
            if (local == null) return;
            try { local.TakeHealth(byte.MaxValue, DamageType.GENERIC); }
            catch (System.Exception e) { Features.Notify("自杀失败: " + e.Message); }
        }

        // ============ 复活 ============

        /// <summary>复活目标（零成本）：SetHealthRPC 满血触发完整复活流程。</summary>
        public static void RevivePlayer(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            try { p.SetHealthRPC(entity_player.MAX_HEALTH); }
            catch (System.Exception e) { Features.Notify("复活失败: " + e.Message); return; }
            Features.Notify(p.IsDead()
                ? "已复活 " + p.GetPlayerName()
                : p.GetPlayerName() + " 已满血");
        }

        /// <summary>复活本地（死后直接回出生点）。</summary>
        public static void ReviveSelf()
        {
            var local = PlayerController.LOCAL;
            if (local == null || !local.IsSpawned) { Features.Notify("没找到本地玩家"); return; }
            RevivePlayer(local);
        }

        /// <summary>复活全部玩家（含本地）。</summary>
        public static void ReviveAll()
        {
            int n = 0;
            foreach (var p in _players)
            {
                if (p == null || !p.IsSpawned) continue;
                RevivePlayer(p);
                n++;
            }
            var local = PlayerController.LOCAL;
            if (local != null && local.IsSpawned) { RevivePlayer(local); n++; }
            Features.Notify("已复活全部 " + n + " 名玩家");
        }

        // ============ 内部 ============

        private static void DriveLoopKill()
        {
            // 遍历只记账、遍历后写字典，避免 Mono 抛 "Collection was modified"
            var gone = new List<byte>();
            var next = new List<KeyValuePair<byte, float>>();
            foreach (var kv in _loopAt)
            {
                if (Time.time >= kv.Value)
                {
                    var t = FindByID(kv.Key);
                    if (t != null)
                    {
                        ExecuteKill(t);
                        next.Add(new KeyValuePair<byte, float>(kv.Key, Time.time + LoopInterval));
                    }
                    else gone.Add(kv.Key);
                }
            }
            foreach (var kv in next) _loopAt[kv.Key] = kv.Value;
            foreach (var id in gone) _loopAt.Remove(id);
        }

        private static void DriveShoveLoop()
        {
            // 同 DriveLoopKill：遍历时只记账，遍历完再写。
            var gone = new List<byte>();
            var next = new List<KeyValuePair<byte, float>>();
            foreach (var kv in _shoveLoopAt)
            {
                if (Time.time >= kv.Value)
                {
                    var t = FindByID(kv.Key);
                    if (t != null && t.IsSpawned)
                    {
                        // 持续向上推（摔不死）；加水平随机避免原地打转
                        Vector3 dir = Vector3.up + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
                        try { t.ShoveRPC(dir, ShoveForce); } catch { }
                        next.Add(new KeyValuePair<byte, float>(kv.Key, Time.time + ShoveInterval));
                    }
                    else gone.Add(kv.Key);
                }
            }
            foreach (var kv in next) _shoveLoopAt[kv.Key] = kv.Value;
            foreach (var id in gone) _shoveLoopAt.Remove(id);
        }

        private static void DriveBloodLoop()
        {
            // 同 DriveLoopKill：遍历时只记账，遍历完再写。
            var gone = new List<byte>();
            var next = new List<KeyValuePair<byte, BloodLoop>>();
            foreach (var kv in _bloodLoop)
            {
                if (Time.time < kv.Value.at) continue;
                var t = FindByID(kv.Key);
                if (t != null && t.IsSpawned)
                {
                    SetBlood(t, kv.Value.target);
                    next.Add(new KeyValuePair<byte, BloodLoop>(kv.Key, new BloodLoop { target = kv.Value.target, at = Time.time + BloodInterval }));
                }
                else gone.Add(kv.Key);
            }
            foreach (var kv in next) _bloodLoop[kv.Key] = kv.Value;
            foreach (var id in gone) _bloodLoop.Remove(id);
        }

        private static void DriveStealLoop()
        {
            var gone = new List<byte>();
            var next = new List<KeyValuePair<byte, float>>();
            foreach (var kv in _stealLoopAt)
            {
                if (Time.time < kv.Value) continue;
                var t = FindByID(kv.Key);
                if (t != null && t.IsSpawned)
                {
                    StealItems(t);
                    next.Add(new KeyValuePair<byte, float>(kv.Key, Time.time + StealInterval));
                }
                else gone.Add(kv.Key);
            }
            foreach (var kv in next) _stealLoopAt[kv.Key] = kv.Value;
            foreach (var id in gone) _stealLoopAt.Remove(id);
        }

        private static void DriveDropLoop()
        {
            var gone = new List<byte>();
            var next = new List<KeyValuePair<byte, float>>();
            foreach (var kv in _dropLoopAt)
            {
                if (Time.time < kv.Value) continue;
                var t = FindByID(kv.Key);
                if (t != null && t.IsSpawned)
                {
                    DropAllItems(t);
                    next.Add(new KeyValuePair<byte, float>(kv.Key, Time.time + DropInterval));
                }
                else gone.Add(kv.Key);
            }
            foreach (var kv in next) _dropLoopAt[kv.Key] = kv.Value;
            foreach (var id in gone) _dropLoopAt.Remove(id);
        }

        private static entity_player FindByID(byte id)
        {
            var pc = MonoController<PlayerController>.Instance;
            return pc != null ? pc.GetPlayerEntityByID(id) : null;
        }

        /// <summary>反射调用 private 的 RPC 方法（反编译里 RPC 多是 private，直接调不了）。</summary>
        private static bool InvokePrivate(System.Type type, string method, object obj, params object[] args)
        {
            try
            {
                var m = type.GetMethod(method,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (m == null) return false;
                m.Invoke(obj, args);
                return true;
            }
            catch { return false; }
        }

        private static void ExecuteKill(entity_player p)
        {
            if (p == null || !p.IsSpawned) return;
            try { p.TakeHealthRPC(byte.MaxValue, DamageType.GENERIC); }
            catch (System.Exception e) { Features.Notify("秒杀失败: " + e.Message); }
        }
    }
}
