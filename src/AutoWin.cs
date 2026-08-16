using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>
    /// 一键解放双手（自动通关）：
    ///   吸废料 → 袋满入账船上(③) → 拨号下单 → 等配送件 → 隔空拿取送达 → 债务还清即通关，否则循环到废料耗尽。
    ///
    /// 自动回收走「房主直写账本」：sc.Add() 把袋里废料直接加进 ③（claimedScrap），再 bag.Clear() 清袋继续吸，
    /// 绕开物理倒袋，最稳。因此本功能要求房主（主机）。
    /// </summary>
    public static class AutoWin
    {
        private enum Phase { None, Absorb, Dump, Dial, WaitSpawn, Deliver }
        private static Phase _phase;
        private static float _phaseStart;
        private static int _dialRetries;       // 拨号失败重试次数
        private static float _dialRetryAt;     // 下次重试拨号的时间点
        private static float _ledgerWaitStart; // 账本有数但吸不到实体的等待起点

        public static bool IsActive => _phase != Phase.None;

        /// <summary>开始/切换：解放双手。</summary>
        public static void Start()
        {
            if (_phase != Phase.None) { Features.Notify("一键解放双手已在运行"); return; }
            if (PlayerController.LOCAL == null) { Features.Notify("请先进对局再开启一键解放双手"); return; }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            { Features.Notify("自动回收需要房主(主机)，你现在是客户端"); return; }

            _phase = Phase.Absorb;
            _dialRetries = 0;
            _dialRetryAt = 0f;
            VacuumAll.Start();
            Features.Notify(Features.InRound()
                ? "一键解放双手：开始吸废料"
                : "一键解放双手：先吸废料，对局开始后自动配送");
        }

        /// <summary>停止循环。</summary>
        public static void Stop()
        {
            _phase = Phase.None;
            VacuumAll.Stop();
            AutoDial.Stop();
            AutoDeliver.Stop();
            Features.Notify("已停止一键解放双手");
        }

        /// <summary>每帧推进状态机。</summary>
        public static void Update()
        {
            if (_phase == Phase.None) return;

            // 本地玩家没了 / 失去主机 → 自动停
            if (PlayerController.LOCAL == null) { Stop(); return; }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) { Stop(); return; }

            // 拨号/配送要求对局真正开始(PLAYING)；没开始就退回吸废料，对局一开自动往下走。
            // 只在船里/大厅开着：吸废料+入账船上照跑，不碰电话。
            bool inRound = Features.InRound();
            if (_phase >= Phase.Dial && !inRound) { _phase = Phase.Absorb; }

            switch (_phase)
            {
                // ---- 吸废料（袋满或全图空会自动停）----
                case Phase.Absorb:
                    if (VacuumAll.IsActive) return;
                    if (BagScrap() > 0) { _phase = Phase.Dump; _ledgerWaitStart = 0f; break; }
                    if (!VacuumAll.IsWorldEmpty())
                    {
                        // 有实际可吸的实体 → 直接开吸；只剩账本有数 → 挂着等废料长出来/房间刷出来
                        if (VacuumAll.CountWorldScrap() > 0) { VacuumAll.Start(); _ledgerWaitStart = 0f; break; }
                        if (!inRound) break;
                        if (_ledgerWaitStart == 0f) _ledgerWaitStart = Time.time;
                        if (Time.time - _ledgerWaitStart > 45f)   // 账本一直有数但吸不到：放弃等，往下走
                        { _ledgerWaitStart = 0f; _phase = Phase.Dial; }
                        break;
                    }
                    _ledgerWaitStart = 0f;
                    if (inRound) _phase = Phase.Dial;   // 全图空且对局中 → 用现有③直接拨
                    // 对局没开始又没废料：原地等，不空转
                    break;

                // ---- 袋里废料入账船上(③)，清袋继续 ----
                case Phase.Dump:
                    DoDump();
                    if (!VacuumAll.IsWorldEmpty()) { _phase = Phase.Absorb; VacuumAll.Start(); }
                    else { _phase = Phase.Dial; }
                    break;

                // ---- 拨号下单 ----
                case Phase.Dial:
                    if (Time.time < _dialRetryAt) break;   // 上次失败后隔几秒再试
                    AutoDial.TryDial();
                    if (AutoDial.IsDialing)
                    {
                        _phase = Phase.WaitSpawn;
                        _phaseStart = Time.time;
                        _dialRetries = 0;
                        break;
                    }
                    // 拨号没开始：没可拨任务（废料不够 或 都在配送中）
                    if (PaidDebt()) { NotifyWin(); return; }
                    if (!VacuumAll.IsWorldEmpty()) { _phase = Phase.Absorb; VacuumAll.Start(); break; }
                    // 全图吸空还拨不上 → 隔几秒重试几次，别一次就放弃
                    _dialRetries++;
                    if (_dialRetries >= 8)
                    {
                        _phase = Phase.None;
                        Features.Notify("拨号失败：废料不足（地图已吸空）");
                        break;
                    }
                    _dialRetryAt = Time.time + 3f;
                    Features.Notify("拨号没成功，3秒后重试 " + _dialRetries + "/8");
                    break;

                // ---- 等拨号结束 + 配送件出来 ----
                case Phase.WaitSpawn:
                    // 配送件可拿就立刻拿（不依赖拨号标志，防拨号标志卡住）
                    if (AutoDeliver.HasDeliverable())
                    {
                        AutoDeliver.TryDeliver();
                        _phase = Phase.Deliver;
                        break;
                    }
                    // 拨号还在进行 → 等；卡太久就重新拨
                    if (AutoDial.IsDialing)
                    {
                        if (Time.time - _phaseStart > 30f)
                        {
                            AutoDial.Stop();
                            _phase = Phase.Dial;
                            Features.Notify("拨号卡住，重新拨号");
                        }
                        break;
                    }
                    // 拨号结束了但件一直没出 → 超时重拨
                    if (Time.time - _phaseStart > 25f)
                    {
                        if (PaidDebt()) { NotifyWin(); return; }
                        if (!VacuumAll.IsWorldEmpty()) { _phase = Phase.Absorb; VacuumAll.Start(); }
                        else { _phase = Phase.Dial; _dialRetryAt = 0f; Features.Notify("配送件没出现，重新拨号"); }
                    }
                    break;

                // ---- 配送中 / 送完判定 ----
                case Phase.Deliver:
                    if (AutoDeliver.IsWorking) return;
                    if (PaidDebt()) { NotifyWin(); return; }
                    if (!VacuumAll.IsWorldEmpty()) { _phase = Phase.Absorb; VacuumAll.Start(); }
                    else { _phase = Phase.Dial; }
                    break;
            }
        }

        // ---------- 工具 ----------

        private static entity_item_vacuum GetBag()
        {
            var local = PlayerController.LOCAL;
            if (!local) return null;
            var vacuum = local.GetVacuum();
            if (!vacuum) return null;
            return vacuum.GetVacuumHolder();
        }

        private static int BagScrap()
        {
            var b = GetBag();
            return b ? b.GetTotalScrap() : 0;
        }

        /// <summary>房主直写账本：袋里废料 → 船上③，清袋。</summary>
        private static void DoDump()
        {
            var sc = NetController<ScrapController>.Instance;
            var bag = GetBag();
            if (!sc || !bag) return;
            int amount = bag.GetTotalScrap();
            if (amount <= 0) return;
            try
            {
                sc.Add(amount);          // [Server] 房主可调
                bag.Clear();             // [Server] 清袋，可继续吸
                Features.Notify("自动回收 +" + amount + " 废料入账船上");
            }
            catch (System.Exception e)
            {
                Features.Notify("回收失败: " + e.Message);
                _phase = Phase.None;
            }
        }

        private static bool PaidDebt()
        {
            var cur = NetController<CurrencyController>.Instance;
            return cur != null && cur.PaidDebt();
        }

        private static void NotifyWin()
        {
            _phase = Phase.None;
            VacuumAll.Stop(); AutoDial.Stop(); AutoDeliver.Stop();
            Features.Notify("债务已还清，通关成功！");
        }
    }
}
