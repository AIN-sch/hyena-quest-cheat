using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>一键吸取废料：房主直写/客户端批量标记；每轮限量防服务端销毁溢出。</summary>
    public static class VacuumAll
    {
        private static bool _active;
        private static float _nextScan;
        private static float _emptyWaitStart;        // 账本还有数但扫不到实体的等待起点
        private static readonly HashSet<ulong> _markedIds = new HashSet<ulong>();  // 客户端已标记等结算的废料ID，防重复标记
        private const int MaxPerScan = 4;            // 袋不紧张时每轮标记几件
        private const float ScanInterval = 0.4f;     // 每轮间隔秒数

        /// <summary>房主：袋满自动入账船上(③)清袋继续吸，取到底不浪费；客户端无效。</summary>
        public static bool AutoRecycle = true;

        public static bool IsActive => _active;

        /// <summary>数全图还剩多少可吸废料（供解放双手循环判断）。</summary>
        public static int CountWorldScrap()
        {
            var local = PlayerController.LOCAL;
            if (!local || NetworkManager.Singleton == null) return 0;
            int count = 0;
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;
                var scrap = nobj.GetComponent<entity_phys_prop_scrap>();
                if (scrap == null || !scrap.CanScrap(local)) continue;
                count++;
            }
            return count;
        }

        /// <summary>全图是否真的没废料了：实体扫不到，且游戏账本也归 0。</summary>
        /// <remarks>世界废料以账本 _worldScrap 为准：部分废料暂不可扫，需结合账本判断。</remarks>
        public static bool IsWorldEmpty()
        {
            var sc = NetController<ScrapController>.Instance;
            if (sc != null && sc.GetWorldScrap(false) > 0) return false;   // 游戏账本还有 → 不算空
            return CountWorldScrap() <= 0;                                  // 实体也没有 → 空
        }

        /// <summary>直接吸满：瞬间把袋子填到上限。</summary>
        /// <remarks>房主直写结算；客户端走真空 RPC 全图标记并行结算。</remarks>
        private static bool FillInstantly()
        {
            var local = PlayerController.LOCAL;
            if (!local) return false;
            var vacuum = local.GetVacuum();
            if (!vacuum) return false;
            var holder = vacuum.GetVacuumHolder();
            if (!holder) return false;
            var sc = NetController<ScrapController>.Instance;
            if (sc == null) return false;

            int maxScrap = sc.GetMaxContainerScrap();
            int bag = holder.GetTotalScrap();
            if (bag >= maxScrap) return false;   // 已满，交给袋满逻辑

            if (Features.IsHost)
            {
                // 房主直写：把世界里的废料结算进袋（精确值）
                int need = maxScrap - bag;
                int taken = 0;
                foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    if (taken >= need) break;
                    var nobj = kv.Value;
                    if (nobj == null || !nobj.IsSpawned) continue;
                    var scrap = nobj.GetComponent<entity_phys_prop_scrap>();
                    if (scrap == null || !scrap.CanScrap(local)) continue;
                    int reward = Mathf.Max(1, scrap.GetReward());
                    if (reward > need - taken) continue;   // 放不下的跳过
                    try
                    {
                        holder.AddScrap(reward);
                        sc.RemoveWorldScrap(reward);
                        scrap.NetworkObject.Despawn(true);
                        taken += reward;
                    }
                    catch { }
                }
                if (taken > 0)
                {
                    Features.Notify("直接吸满 +" + taken + " (" + (bag + taken) + "/" + maxScrap + ")");
                    return true;
                }
                return false;
            }

            return false;
        }

        /// <summary>客户端直接吸满：全图流式标记，服务端并行结算（每轮只标新件，绝不溢出销毁废料）。</summary>
        /// <remarks>用"原值+5"作安全上界，上界&lt;剩余容量才标记，避免袋撑爆销毁废料。</remarks>
        private static bool FillInstantlyStream()
        {
            var local = PlayerController.LOCAL;
            if (!local) return false;
            var vacuum = local.GetVacuum();
            if (!vacuum) return false;
            var holder = vacuum.GetVacuumHolder();
            if (!holder) return false;
            var sc = NetController<ScrapController>.Instance;
            var nm = NetworkManager.Singleton;
            if (sc == null || nm == null) return false;

            int maxScrap = sc.GetMaxContainerScrap();
            int bag = holder.GetTotalScrap();
            if (bag >= maxScrap) return false;

            int remaining = maxScrap - bag;
            int margin = Mathf.Max(10, maxScrap / 5);   // 再留一层余量，防服务端处理顺序导致瞬时挤爆
            const int MaxPerFrame = 40;                  // 每帧最多发40条真空RPC，避免冲垮服务端
            int marked = 0;
            int estimated = 0;

            foreach (var kv in nm.SpawnManager.SpawnedObjects)
            {
                if (marked >= MaxPerFrame || estimated >= remaining - margin) break;
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;
                var scrap = nobj.GetComponent<entity_phys_prop_scrap>();
                if (scrap == null || !scrap.CanScrap(local)) continue;
                ulong id = nobj.NetworkObjectId;
                if (_markedIds.Contains(id)) continue;      // 已标记等结算，避免重复发
                int bound = scrap.scrap + 5;                // 安全上界（服务端实际 ≤ 原值+5）
                if (bound > remaining - margin) continue;   // 放不下(留余量)跳过，等袋空出再标
                try
                {
                    scrap.SetVacuumingRPC(local, true);
                    _markedIds.Add(id);
                    marked++;
                    estimated += bound;
                }
                catch { }
            }

            if (marked > 0)
            {
                Features.Notify("吸废料 +" + marked + " 件 (" + bag + "/" + maxScrap + ")");
                return true;
            }
            return false;
        }

        /// <summary>袋满/放不下：房主自动回收船账继续吸；其他停并提示回收。返回是否继续会话。</summary>
        private static bool RecycleOrStop()
        {
            if (Features.IsHost && AutoRecycle && TryRecycle())
            {
                _markedIds.Clear();   // 清袋后重标新一批
                Features.Notify("袋满已自动回收船上，继续吸取");
                _nextScan = Time.time + 0.2f;
                return true;
            }
            _active = false;
            _markedIds.Clear();
            Features.Notify("废料已满，请回收！");
            return false;
        }

        /// <summary>手动/自动停止吸废料。</summary>
        public static void Stop()
        {
            _active = false;
            _markedIds.Clear();
        }

        /// <summary>房主：袋里废料直写船上③并清袋（绕开物理倒袋）。</summary>
        private static bool TryRecycle()
        {
            var sc = NetController<ScrapController>.Instance;
            var local = PlayerController.LOCAL;
            if (!sc || !local) return false;
            var vacuum = local.GetVacuum();
            var holder = vacuum != null ? vacuum.GetVacuumHolder() : null;
            if (!holder) return false;
            int amount = holder.GetTotalScrap();
            if (amount <= 0) return false;
            try
            {
                sc.Add(amount);          // [Server] 房主可调，直接进船账
                holder.Clear();          // [Server] 清袋继续吸
                return true;
            }
            catch { return false; }
        }

        /// <summary>按钮/快捷键：开始（已运行则忽略）。</summary>
        public static void Start()
        {
            if (_active) { Features.Notify("正在吸取中..."); return; }
            _active = true;
            _nextScan = 0f;                          // 立即开始
            _emptyWaitStart = 0f;
            _markedIds.Clear();
            Features.Notify("开始吸取废料");
        }

        public static void Update()
        {
            if (!_active) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + ScanInterval;

            var local = PlayerController.LOCAL;
            if (!local) { _active = false; return; }
            var vacuum = local.GetVacuum();
            if (!vacuum) { _active = false; return; }
            var holder = vacuum.GetVacuumHolder();
            if (holder == null) { _active = false; Features.Notify("没拿真空容器"); return; }

            var sc = NetController<ScrapController>.Instance;
            if (sc == null) { _active = false; return; }

            int maxScrap = sc.GetMaxContainerScrap();
            int bag = holder.GetTotalScrap();

            // 直接吸满：房主每轮直写瞬间填袋；客户端全图流式标记，服务端并行结算（每0.4s补标新件）
            if (Features.VacuumInstant)
            {
                if (Features.IsHost)
                {
                    if (FillInstantly()) return;          // 填完交给下面的袋满逻辑
                }
                else
                {
                    if (FillInstantlyStream()) return;    // 标新件让服务端结算，_markedIds 防重复
                }
            }

            // 袋满：房主自动回收船账继续吸；客户端停，提示回收
            if (bag >= maxScrap) { RecycleOrStop(); return; }

            if (NetworkManager.Singleton == null) return;

            // 数全图还剩多少可吸废料
            int worldLeft = 0;
            var list = new List<entity_phys_prop_scrap>();
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;
                var scrap = nobj.GetComponent<entity_phys_prop_scrap>();
                if (scrap == null) continue;
                if (!scrap.CanScrap(local)) continue;
                worldLeft++;
                list.Add(scrap);
            }

            if (worldLeft > 0) _emptyWaitStart = 0f;   // 有实体可吸 → 清掉账本等待计时

            // 全图扫不到可吸废料
            if (worldLeft == 0)
            {
                var scCtrl = NetController<ScrapController>.Instance;
                if (scCtrl != null && scCtrl.GetWorldScrap(false) > 0)
                {
                    // 账本有数但暂不可扫 → 慢扫等待，避免误报"吸完"
                    if (_emptyWaitStart == 0f)
                    {
                        _emptyWaitStart = Time.time;
                        Features.Notify("废料还没吸完（游戏账本剩 " + scCtrl.GetWorldScrap(false) + "），继续找...");
                    }
                    if (Time.time - _emptyWaitStart > 40f)
                    {
                        _active = false;
                        _emptyWaitStart = 0f;
                        Features.Notify("全图找不到能吸的废料（账本还剩 " + scCtrl.GetWorldScrap(false) + "），已停止");
                        return;
                    }
                    _nextScan = Time.time + 2f;
                    return;
                }
                _emptyWaitStart = 0f;
                _active = false;
                Features.Notify("全图吸取完毕！");
                return;
            }

            // 袋快满时减量标记，控制最后批次溢出（避免废料凭空消失）
            int perScan = bag > maxScrap * 0.8f ? 1 : MaxPerScan;
            int remaining = maxScrap - bag;
            int marked = 0;
            bool nearFull = bag > maxScrap * 0.8f;
            foreach (var scrap in list)
            {
                if (marked >= perScan || remaining <= 0) break;
                ulong id = scrap.NetworkObjectId;
                if (_markedIds.Contains(id)) continue;    // 已标记等结算（直接吸满时），避免重复发
                int bound = scrap.scrap + 5;              // 安全上界：服务端实际 ≤ 原值+5，防止估低溢出销毁废料
                // 快满时留余量：避免并发结算把袋挤爆销毁废料
                if (nearFull && bound > remaining - 6) continue;
                if (bound > remaining) continue;

                try
                {
                    scrap.SetVacuumingRPC(local, true);
                    _markedIds.Add(id);
                    marked++;
                    remaining -= bound;
                }
                catch { /* 个别异常忽略 */ }
            }

            // 一件都没吸上：说明剩下的都放不下（≈袋满）→ 房主自动回收船账继续，否则停
            if (marked == 0 && bag > maxScrap * 0.8f) { RecycleOrStop(); return; }
            if (marked > 0) Features.Notify("吸取废料 " + marked + " 件 (" + bag + "/" + maxScrap + ")");
        }
    }
}
