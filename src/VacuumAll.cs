using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>
    /// 一键吸取废料（按钮/快捷键触发的一次性会话）：
    ///  - 直接吸满（默认开）：房主=服务端直写瞬间填袋；客户端=全图批量标记让服务端并行结算。
    ///  - 普通吸：每 0.4s 标记几件给服务端（SetVacuumingRPC），服务端按体积慢慢结算。
    ///  - 真空收集判定在客户端（锥形触发器+视线），服务端只认 RPC → 可全图吸。
    ///  - 袋满：房主+自动回收=入账船上继续；否则停止提示回收。
    ///
    /// 关键：服务端塞袋只认 AddScrap()，袋满直接 return → 那件被销毁且不进账。
    /// 因此绝不一口气全图标记，每轮限量 + 按剩余容量估算跳过放不下的。
    /// </summary>
    public static class VacuumAll
    {
        private static bool _active;
        private static float _nextScan;
        private static float _emptyWaitStart;        // 账本还有数但扫不到实体的等待起点
        private static bool _burstDone;              // 客户端开局只批量标记一次，防重复标记溢出
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
        /// <remarks>
        /// 游戏"世界资讯"的剩余废料 = 账本 _worldScrap（地图生成时统计、吸走一件扣一件）。
        /// 有些废料账本有数但暂时扫不到/吸不了（海胆没长好、房间没刷出、在别人手里），
        /// 单看实体会误报"全图吸完"。
        /// </remarks>
        public static bool IsWorldEmpty()
        {
            var sc = NetController<ScrapController>.Instance;
            if (sc != null && sc.GetWorldScrap(false) > 0) return false;   // 游戏账本还有 → 不算空
            return CountWorldScrap() <= 0;                                  // 实体也没有 → 空
        }

        /// <summary>直接吸满：瞬间把袋子填到上限。</summary>
        /// <remarks>
        /// 房主=服务端直写：把世界里的废料直接结算进袋（扣账本+销毁实体，瞬间完成）。
        /// 客户端=没服务端写权限，只能走游戏真空 RPC → 全图一起标记，让服务端并行结算。
        /// </remarks>
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
                    if (reward > need - taken) continue;   // 放不下的留着
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

            // 客户端：全图一起标记，服务端并行结算（估算留余量，防服务端加价把袋撑爆销毁废料）
            int remaining = maxScrap - bag;
            int margin = Mathf.Max(10, maxScrap / 5);
            int estimated = 0;
            int marked = 0;
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                if (marked >= MaxPerScan * 8 || estimated >= remaining - margin) break;
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;
                var scrap = nobj.GetComponent<entity_phys_prop_scrap>();
                if (scrap == null || !scrap.CanScrap(local)) continue;
                int reward = Mathf.Max(1, scrap.scrap);   // 客户端只能估
                try
                {
                    scrap.SetVacuumingRPC(local, true);
                    marked++;
                    estimated += reward;
                }
                catch { }
            }
            if (marked > 0)
            {
                Features.Notify("直接吸满：标记 " + marked + " 件，服务端结算中...");
                return true;
            }
            return false;
        }

        /// <summary>袋满/放不下：房主自动回收船账继续吸；其他停并提示回收。返回是否继续会话。</summary>
        private static bool RecycleOrStop()
        {
            if (Features.IsHost && AutoRecycle && TryRecycle())
            {
                _burstDone = false;   // 清袋后重新开始，客户端能再批量标记一次
                Features.Notify("袋满已自动回收船上，继续吸取");
                _nextScan = Time.time + 0.2f;
                return true;
            }
            _active = false;
            _burstDone = false;
            Features.Notify("废料已满，请回收！");
            return false;
        }

        /// <summary>手动/自动停止吸废料。</summary>
        public static void Stop()
        {
            _active = false;
            _burstDone = false;
        }

        /// <summary>房主：把袋里废料直接入账船上(③)，清袋继续吸（绕开物理倒袋，最稳）。</summary>
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

        /// <summary>按钮/快捷键：开始（或已在吸就忽略）。</summary>
        public static void Start()
        {
            if (_active) { Features.Notify("正在吸取中..."); return; }
            _active = true;
            _nextScan = 0f;                          // 立即开始
            _emptyWaitStart = 0f;
            _burstDone = false;
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

            // 直接吸满：房主每轮直写瞬间填袋（配合自动回收能瞬间榨干全图）；客户端开局批量标记一次等结算
            if (Features.VacuumInstant)
            {
                if (Features.IsHost)
                {
                    if (FillInstantly()) return;          // 填完交给下面的袋满逻辑
                }
                else if (!_burstDone)
                {
                    if (FillInstantly()) { _burstDone = true; _nextScan = Time.time + 5f; return; }   // 等服务端结算
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
                    // 游戏账本还有数（海胆没长好/房间没刷出/在别人手里）→ 挂着慢扫等，别误报"吸完"
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

            // 袋快满时少吸点，把最后一批溢出的浪费压到最小（绝不让废料凭空消失）
            int perScan = bag > maxScrap * 0.8f ? 1 : MaxPerScan;
            int remaining = maxScrap - bag;
            int marked = 0;
            bool nearFull = bag > maxScrap * 0.8f;
            foreach (var scrap in list)
            {
                if (marked >= perScan || remaining <= 0) break;
                int reward = Mathf.Max(1, scrap.scrap);   // 客户端估个大概，放不下的直接跳过
                // 快满时留余量：服务端实际奖励可能比客户端看到的略高，避免溢出时废料被销毁
                if (nearFull && reward > remaining - 6) continue;
                if (reward > remaining) continue;

                try
                {
                    scrap.SetVacuumingRPC(local, true);
                    marked++;
                    remaining -= reward;
                }
                catch { /* 个别异常忽略 */ }
            }

            // 一件都没吸上：说明剩下的都放不下（≈袋满）→ 房主自动回收船账继续，否则停
            if (marked == 0 && bag > maxScrap * 0.8f) { RecycleOrStop(); return; }
            if (marked > 0) Features.Notify("吸取废料 " + marked + " 件 (" + bag + "/" + maxScrap + ")");
        }
    }
}
