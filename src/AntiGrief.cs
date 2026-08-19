// AntiGrief.cs — 防整+锁物反抢：拦其他客户端攻击RPC、抢回被偷物品。
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;
using HarmonyLib;

namespace HyenaQuestCheat
{
    /// <summary>防整/锁物反抢总模块。</summary>
    public static class AntiGrief
    {
        // ============ 锁物：背包 ============
        private static entity_player_inventory _inv;          // 已订阅变化的背包
        private static readonly HashSet<ulong> _selfDrop = new HashSet<ulong>();   // 本地主动丢弃的物品(NetworkObjectId)
        private static readonly Dictionary<ulong, float> _selfDropAt = new Dictionary<ulong, float>();
        private const float SelfDropTtl = 2f;                 // 自丢标记保留时长

        // ============ 锁物：手里 ============
        private static entity_phys _lastHeld;                 // 上帧正抓着的物品
        private const float HeldStolenDist = 5f;              // 离开超5m判定被瞬移偷走（本地丢弃为渐进位移）

        private static float _lastPurge;

        // ============ 反整播报 ============
        private static readonly Dictionary<string, float> _reportAt = new Dictionary<string, float>();
        private const float ReportCooldown = 1.5f;     // 同人同动作去重，防刷屏

        /// <summary>每帧：手里被抢反抢 + 自丢标记清理 + 订阅背包变化。</summary>
        public static void Update()
        {
            if (!Features.AntiGrief) return;
            var local = PlayerController.LOCAL;
            if (local == null || !Features.InRound()) return;

            // 惰性订阅背包变化（背包实体可能随重生换对象）
            var inv = local.GetInventory();
            if (inv != null && !ReferenceEquals(inv, _inv))
            {
                if (_inv != null) { try { _inv.GetInventory().OnListChanged -= OnBagChanged; } catch { } }
                _inv = inv;
                try { inv.GetInventory().OnListChanged += OnBagChanged; } catch { }
            }

            // 手里物品被抢：被抓走/瞬移走 → 反抢（自动拿起）
            var pg = local.GetComponent<entity_player_physgun>();
            if (pg != null)
            {
                var held = pg.GetGrabbingObject();
                if (_lastHeld != null && !ReferenceEquals(held, _lastHeld))
                {
                    var owner = _lastHeld.GetGrabbingOwner();
                    bool stolen = (owner != null && !ReferenceEquals(owner, local))
                        || (owner == null && Vector3.Distance(_lastHeld.transform.position, local.transform.position) > HeldStolenDist);
                    if (stolen && !IsSelfDrop(_lastHeld.NetworkObjectId))
                    {
                        try { _lastHeld.SetGrabbing(true); } catch { }   // 抢回所有权，physgun 自动重新拿起
                        if (owner != null && !ReferenceEquals(owner, local)) Report(owner, "抢物品");
                        else Report(0UL, "抢物品");
                        Features.Notify("已抢回被偷物品");
                    }
                }
                _lastHeld = held;
            }

            // 自丢标记过期清理
            if (Time.time - _lastPurge > 0.5f)
            {
                _lastPurge = Time.time;
                if (_selfDropAt.Count > 0)
                {
                    var stale = new List<ulong>();
                    foreach (var kv in _selfDropAt)
                        if (Time.time - kv.Value > SelfDropTtl) stale.Add(kv.Key);
                    foreach (var k in stale) { _selfDrop.Remove(k); _selfDropAt.Remove(k); }
                }
            }
        }

        /// <summary>背包槽被清空且非本地主动丢弃 → 抢回装回背包。</summary>
        private static void OnBagChanged(NetworkListEvent<NetworkBehaviourReference> evt)
        {
            if (!Features.AntiGrief || _inv == null) return;
            var local = PlayerController.LOCAL;
            if (local == null || local.IsDead()) return;

            entity_item_pickable removed = null;
            int slot = -1;

            if (evt.Type == NetworkListEvent<NetworkBehaviourReference>.EventType.Value)
            {
                if (NETController.Get<entity_item_pickable>(evt.Value) != null) return;   // 是加回来，不是移除
                removed = NETController.Get<entity_item_pickable>(evt.PreviousValue);
                slot = evt.Index;
            }
            else if (evt.Type == NetworkListEvent<NetworkBehaviourReference>.EventType.Remove
                  || evt.Type == NetworkListEvent<NetworkBehaviourReference>.EventType.RemoveAt)
            {
                removed = NETController.Get<entity_item_pickable>(evt.Value);
                slot = -1;   // 列表收缩，回填到空槽
            }

            if (removed == null || !removed.IsSpawned) return;        // 被用掉/销毁的拿不回来
            if (IsSelfDrop(removed.NetworkObjectId)) return;          // 本地主动丢弃的不动

            var thief = removed.GetGrabbingOwner();                   // 窃取者正抓着物品 → 报名字
            if (thief != null && !ReferenceEquals(thief, local)) Report(thief, "偷背包");
            else Report(0UL, "偷背包");

            if (slot < 0) slot = FindFreeSlot();
            if (slot < 0) return;
            try { _inv.PickupItem((byte)slot, removed); } catch { }   // 直接装回背包（无距离校验）
        }

        private static int FindFreeSlot()
        {
            if (_inv == null) return -1;
            var list = _inv.GetInventory();
            for (byte b = 0; b < list.Count; b++)
                if (_inv.IsInventorySlotEmpty(b)) return b;
            return -1;
        }

        // ============ 本地主动丢弃标记 ============

        /// <summary>DropItemRPC 在本地客户端执行=本地丢弃 → 记标记，反抢时跳过。</summary>
        public static void MarkSelfDrop(NetworkBehaviourReference refObj)
        {
            if (refObj.TryGet<NetworkBehaviour>(out var nb) && nb != null)
            {
                _selfDrop.Add(nb.NetworkObjectId);
                _selfDropAt[nb.NetworkObjectId] = Time.time;
            }
        }

        private static bool IsSelfDrop(ulong netId) => _selfDrop.Contains(netId);

        // ============ 反整播报 ============

        /// <summary>按发送者ID播报：目标收到的操作[已拦截]。</summary>
        public static void Report(ulong sender, string action)
        {
            if (!Features.AntiGriefBroadcast) return;
            var name = NameByConnection(sender);
            if (string.IsNullOrEmpty(name)) name = "?";
            SendReport(name, action);
        }

        /// <summary>按攻击者实体播报（窃取者正抓着物品）。</summary>
        public static void Report(entity_player attacker, string action)
        {
            if (!Features.AntiGriefBroadcast || attacker == null) return;
            SendReport(attacker.GetPlayerName(), action);
        }

        private static void SendReport(string name, string action)
        {
            if (string.IsNullOrEmpty(name)) name = "?";
            var me = PlayerController.LOCAL;
            string myName = me != null ? me.GetPlayerName() : "?";
            if (name.Length > 16) name = name.Substring(0, 16);   // 长名截断，控制消息长度
            if (myName.Length > 16) myName = myName.Substring(0, 16);
            string key = name + "_" + action;
            float now = Time.time;
            if (_reportAt.TryGetValue(key, out float last) && now - last < ReportCooldown) return;
            _reportAt[key] = now;
            ChatSpam.Send(name + " 对 " + myName + " 执行了 " + action + " 操作 [已拦截]");
        }

        /// <summary>连接ID → 玩家名（OwnerClientId 标记所有者）。</summary>
        private static string NameByConnection(ulong conn)
        {
            foreach (var p in Object.FindObjectsByType<entity_player>())
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.NetworkObject.OwnerClientId == conn) return p.GetPlayerName();
            }
            return null;
        }
    }

    // ============ 防整：拦截其他客户端攻击 RPC ============

    /// <summary>通用拦截判定：目标=本地玩家 + 发送者=其他客户端 → 拦，并按开关播报。</summary>
    internal static class GriefBlock
    {
        public static bool Block(NetworkBehaviour target, __RpcParams rpcParams, string action)
        {
            if (!Features.AntiGrief) return false;
            var local = PlayerController.LOCAL;
            if (local == null || !ReferenceEquals(target, local)) return false;   // 非本地目标，放行
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            ulong sender = rpcParams.Ext.Receive.SenderClientId;
            if (sender == 0UL || sender == nm.LocalClientId) return false;        // 服务器/本地 → 放行
            AntiGrief.Report(sender, action);
            return true;                                                          // 其他客户端 → 拦截
        }
    }

    [HarmonyPatch(typeof(entity_player), "__rpc_handler_2188505251")]   // TakeHealthRPC（秒杀）
    public static class Patch_GriefTakeHealth
    {
        static bool Prefix(NetworkBehaviour target, __RpcParams rpcParams)
            => !GriefBlock.Block(target, rpcParams, "秒杀");
    }

    [HarmonyPatch(typeof(entity_player), "__rpc_handler_80031680")]     // SetHealthRPC（控血/钉血）
    public static class Patch_GriefSetHealth
    {
        static bool Prefix(NetworkBehaviour target, __RpcParams rpcParams)
            => !GriefBlock.Block(target, rpcParams, "钉血");
    }

    [HarmonyPatch(typeof(entity_player), "__rpc_handler_3680886454")]   // ShoveRPC（推飞/连推）
    public static class Patch_GriefShove
    {
        static bool Prefix(NetworkBehaviour target, __RpcParams rpcParams)
            => !GriefBlock.Block(target, rpcParams, "推飞");
    }

    [HarmonyPatch(typeof(entity_player), "__rpc_handler_2492987220")]   // AddHealthRPC（强加血/刷血）
    public static class Patch_GriefAddHealth
    {
        static bool Prefix(NetworkBehaviour target, __RpcParams rpcParams)
            => !GriefBlock.Block(target, rpcParams, "刷血");
    }

    // ============ 锁物：本地丢弃标记 + 房主拦其他客户端丢包 ============

    /// <summary>DropItemRPC 在本地客户端执行=本地丢弃（攻击者的在服务器执行）。</summary>
    [HarmonyPatch(typeof(entity_player_inventory), "DropItemRPC")]
    public static class Patch_MarkSelfDrop
    {
        static void Prefix(entity_player_inventory __instance, NetworkBehaviourReference refObj)
        {
            if (!Features.AntiGrief) return;
            var local = PlayerController.LOCAL;
            if (local == null || !ReferenceEquals(local.GetInventory(), __instance)) return;   // 只记本地背包
            AntiGrief.MarkSelfDrop(refObj);
        }
    }

    /// <summary>房主侧：其他客户端 DropItemRPC 打本地背包 → 直接拦（不执行丢包）。</summary>
    [HarmonyPatch(typeof(entity_player_inventory), "__rpc_handler_3078871644")]
    public static class Patch_BlockDropOnHost
    {
        static bool Prefix(NetworkBehaviour target, __RpcParams rpcParams)
        {
            if (!Features.AntiGrief) return true;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return true;                 // 只有房主进程生效
            var local = PlayerController.LOCAL;
            if (local == null || !ReferenceEquals(local.GetInventory(), target)) return true;   // 非本地背包
            ulong sender = rpcParams.Ext.Receive.SenderClientId;
            if (sender == 0UL || sender == nm.LocalClientId) return true;                        // 本地/服务器放行
            AntiGrief.Report(sender, "丢包");
            Features.Notify("已拦截其他玩家丢包");
            return false;
        }
    }
}
