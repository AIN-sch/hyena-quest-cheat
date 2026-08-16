using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>
    /// 一键配送到：隔空抓起已下单的配送件，传送至匹配送货台，松手触发服务端结算。
    ///
    /// 漏洞点：
    ///  - prop.SetGrabbing(true) 是公开方法，内部发 SetGrabbingRPC 给服务端 → 所有权转本地（ChangeOwnership），
    ///    不校验距离/朝向。
    ///  - 拿到所有权后本地写 transform.position（NetworkTransform 从 owner 复制）→ 件传到送货台。
    ///  - 配送判定在服务端：送货台是服务端触发器，件进去且地址匹配即结算。
    ///
    /// 两段状态机：先抓（所有权异步转移），等 IsOwner 后再传送+松手。
    /// 不用 physgun（_grabbingObject），物理枪会把件往玩家面前拉，反而拽走。
    /// </summary>
    public static class AutoDeliver
    {
        private static entity_prop_delivery _target;
        private static DeliveryController _dc;
        private static float _timeout;

        // 刚投递过的件：服务端要求松手后静止满 1 秒才 CompleteTask + 上锁。
        // 投完立刻又抓同一个会反复打断结算 → 死循环。按对象 ID 记一笔，3 秒内不碰。
        private static ulong _recentId;
        private static float _recentAt;

        /// <summary>是否刚投递完、还在结算窗口内的这个件。</summary>
        private static bool RecentlyReleased(ulong id)
            => id == _recentId && Time.time - _recentAt < 3f;

        /// <summary>是否正在配送中。</summary>
        public static bool IsWorking => _target != null;

        /// <summary>手动/自动停止配送。</summary>
        public static void Stop() { try { if (_target != null) _target.SetGrabbing(false); } catch { } _target = null; _dc = null; }

        /// <summary>是否已有一个可拿取的配送件（已生成、未锁定、地址有送货台）。</summary>
        public static bool HasDeliverable()
        {
            if (NetworkManager.Singleton == null) return false;
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;
                var p = nobj.GetComponent<entity_prop_delivery>();
                if (p == null) continue;
                if (p.IsLocked()) continue;
                if (RecentlyReleased(p.NetworkObjectId)) continue;   // 刚送完的这件，等它结算完
                var dc = NetController<DeliveryController>.Instance;
                if (dc && dc.GetDeliverySpotByAddress(p.GetAddress()) != null) return true;
            }
            return false;
        }

        public static void TryDeliver()
        {
            if (_target != null) { Features.Notify("正在配送中..."); return; }

            var local = PlayerController.LOCAL;
            if (!local) { Features.Notify("不在对局里"); return; }

            var dc = NetController<DeliveryController>.Instance;
            if (!dc) { Features.Notify("配送控制器不可用"); return; }

            if (NetworkManager.Singleton == null) return;

            // 找一个已生成、未锁定、且有匹配送货台的配送件
            entity_prop_delivery prop = null;
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var nobj = kv.Value;
                if (nobj == null || !nobj.IsSpawned) continue;

                var p = nobj.GetComponent<entity_prop_delivery>();
                if (p == null) continue;
                if (p.IsLocked()) continue;                                        // 还没解锁，跳过
                if (RecentlyReleased(p.NetworkObjectId)) continue;                 // 刚送完的这件，等结算
                if (dc.GetDeliverySpotByAddress(p.GetAddress()) == null) continue; // 地址没送货台

                prop = p;
                break;
            }
            if (!prop) { Features.Notify("没有可拿取的配送件（先拨号下单）"); return; }

            // 隔空抓取（所有权异步转移）
            prop.SetGrabbing(true);

            _target = prop;
            _dc = dc;
            _timeout = Time.time + 3f;
            Features.Notify("正在拿取配送件...");
        }

        /// <summary>每帧轮询：所有权到位后传送+松手。</summary>
        public static void Update()
        {
            if (_target == null) return;

            if (Time.time > _timeout) { Cancel("拿取超时"); return; }

            // 等所有权转移到本地（ChangeOwnership 是网络往返，要几帧）
            if (!_target.IsOwner) return;

            var spot = _dc != null ? _dc.GetDeliverySpotByAddress(_target.GetAddress()) : null;
            if (!spot) { Cancel("找不到送货台"); return; }

            // 传送到送货台上方一点，松手 → 服务端触发器结算
            _target.transform.position = spot.transform.position + Vector3.up * 0.6f;
            Physics.SyncTransforms();
            _target.SetGrabbing(false);

            // 记下刚送完的件：结算要 1 秒静止窗口，下一轮别马上又抓同一个
            _recentId = _target.NetworkObjectId;
            _recentAt = Time.time;

            var addr = _target.GetAddress();
            _target = null;
            Features.Notify("已送达送货台 " + addr);
        }

        private static void Cancel(string why)
        {
            try { if (_target != null) _target.SetGrabbing(false); } catch { }
            _target = null;
            _dc = null;
            Features.Notify(why);
        }
    }
}
