using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>
    /// 一键配送：拨号 + 拿取配送串成一步，循环开关。
    ///   开启 → 自动拨号 → 等送货车出件 → 隔空拿取送达 → 有下一单继续，送完为止。
    ///   全程不要求在电话/送货台旁边（隔空触发）。
    ///   开启期间接近电话的玩家会被轻轻弹开，防按键捣乱。
    /// </summary>
    public static class AutoOrder
    {
        private enum Stage { None, RetryDial, Dialing, WaitingSpawn, Delivering }
        private static Stage _stage;
        private static float _stageStart;
        private static float _retryAt;      // 电话被占用时下一次重试拨号的时间点
        private static float _defendAt;     // 防干扰：弹开接近电话玩家的下一次扫描时间
        private static int _delivered;      // 本次循环已送完的单数（防死循环保险）

        public static bool IsActive => _stage != Stage.None;

        public static void Start()
        {
            if (_stage != Stage.None) { Features.Notify("正在配送中..."); return; }
            if (!Features.InRound()) { Features.Notify("未在对局里，无法配送"); return; }

            _delivered = 0;
            if (AutoDeliver.HasDeliverable())
            {
                _stage = Stage.Delivering;
                AutoDeliver.TryDeliver();
            }
            else
            {
                AutoDial.TryDial();
                if (AutoDial.IsDialing)
                {
                    _stage = Stage.Dialing;
                    _stageStart = Time.time;
                    Features.Notify("一键配送：开始拨号");
                }
                else
                {
                    // 没拨上：多半电话被占用/占线 → 稍后自动重试
                    _stage = Stage.RetryDial;
                    _stageStart = Time.time;
                    _retryAt = Time.time + 1.5f;
                    Features.Notify("一键配送：电话忙，稍后自动重试...");
                }
            }
        }

        public static void Stop()
        {
            _stage = Stage.None;
            AutoDial.Stop();
            AutoDeliver.Stop();
        }

        public static void Update()
        {
            if (_stage == Stage.None) return;

            // 离开对局 → 自动停
            if (!Features.InRound()) { Stop(); return; }

            // 全程防干扰：别的玩家接近电话就轻轻弹开
            if (Time.time >= _defendAt)
            {
                _defendAt = Time.time + 0.4f;
                DefendPhone();
            }

            switch (_stage)
            {
                case Stage.RetryDial:
                    if (Time.time - _stageStart > 45f) { Stop(); Features.Notify("拨号失败：电话一直被占用"); break; }
                    if (Time.time < _retryAt) break;
                    AutoDial.TryDial();
                    if (AutoDial.IsDialing) { _stage = Stage.Dialing; _stageStart = Time.time; }
                    else _retryAt = Time.time + 2.5f;
                    break;

                case Stage.Dialing:
                    if (!AutoDial.IsDialing)
                    {
                        _stage = Stage.WaitingSpawn;
                        _stageStart = Time.time;
                        Features.Notify("拨号完成");
                    }
                    else if (Time.time - _stageStart > 45f) { Stop(); Features.Notify("拨号超时"); }
                    break;

                case Stage.WaitingSpawn:
                    if (AutoDeliver.HasDeliverable())
                    {
                        AutoDeliver.TryDeliver();
                        _stage = Stage.Delivering;
                    }
                    else if (Time.time - _stageStart > 25f)
                    {
                        // 件一直没出 → 多半空号/被打断，重拨下一单
                        AutoDial.Stop();
                        AutoDial.TryDial();
                        _stage = AutoDial.IsDialing ? Stage.Dialing : Stage.RetryDial;
                        _stageStart = Time.time;
                        _retryAt = Time.time + 2f;
                        if (!AutoDial.IsDialing) Features.Notify("配送件没出现，稍后重拨...");
                    }
                    break;

                case Stage.Delivering:
                    if (AutoDeliver.IsWorking) break;
                    // 这一单送完 → 还有下一单就继续拨号（循环），没有就停
                    _delivered++;
                    if (_delivered > 50) { Stop(); Features.Notify("一键配送：送得够多了，自动停止"); break; }
                    if (AutoDeliver.HasDeliverable())
                    {
                        AutoDeliver.TryDeliver();
                        _stage = Stage.Delivering;
                    }
                    else if (HasMoreTasks())
                    {
                        AutoDial.TryDial();
                        if (AutoDial.IsDialing) { _stage = Stage.Dialing; _stageStart = Time.time; }
                        else { _stage = Stage.RetryDial; _stageStart = Time.time; _retryAt = Time.time + 2f; }
                    }
                    else
                    {
                        Stop();
                        Features.Notify("一键配送：全部订单已送完");
                    }
                    break;
            }
        }

        /// <summary>是否还有可拨号的单子（废料够、还没下单的）。</summary>
        private static bool HasMoreTasks()
        {
            var cc = NetController<ContractController>.Instance;
            var sc = NetController<ScrapController>.Instance;
            if (!cc || !sc) return false;
            var tasks = cc.GetAffordableTasks(sc.GetClaimedScrap(), false);
            if (tasks == null) return false;
            foreach (var t in tasks) { if (!t.HasDeliveryItem) return true; }
            return false;
        }

        /// <summary>弹开接近电话的玩家，防他们按键搞乱号码 / 抢单。</summary>
        private static void DefendPhone()
        {
            var pc = NetController<PhoneController>.Instance;
            if (!pc) return;
            Vector3 phonePos = pc.transform.position;
            const float radius = 6f;
            foreach (var p in KillTarget.Players)
            {
                if (p == null || !p.IsSpawned || p.IsDead()) continue;
                Vector3 diff = p.transform.position - phonePos;
                float dist = diff.magnitude;
                if (dist > radius || dist < 0.01f) continue;
                Vector3 dir = diff / dist;
                try { p.ShoveRPC(dir + Vector3.up * 0.3f, 28f); }
                catch { }
            }
        }
    }
}
