// AntiSpectate.cs — 反观战：房主钉health为0，房客钉位置地底(黑屏)。
using System.Reflection;
using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    public static class AntiSpectateCtrl
    {
        private static readonly FieldInfo HealthField =
            typeof(entity_player).GetField("_health", BindingFlags.Instance | BindingFlags.NonPublic);

        private static float _lastForce;

        /// <summary>每帧调用：房主且开启时周期性把 health 钉回 0（防被伤害/回血顶掉）。</summary>
        public static void Tick()
        {
            if (!Features.AntiSpectate) return;
            if (!Features.IsHost) return;
            if (Time.time - _lastForce < 0.5f) return;
            _lastForce = Time.time;
            ForceHealth(0);
        }

        /// <summary>菜单/热键切换时调用：房主→对外假死；房客→复制位置钉地底(观战=黑屏)。</summary>
        public static void OnToggle()
        {
            var local = PlayerController.LOCAL;
            if (local == null) { Features.AntiSpectate = false; Features.Notify("未进入对局，反观战已关闭"); return; }

            if (Features.AntiSpectate)
            {
                if (Features.IsHost)
                {
                    if (HealthField == null) { Features.AntiSpectate = false; Features.Notify("反观战: 找不到 _health 字段"); return; }
                    ForceHealth(0);
                    Features.Notify("反观战 开 — 对外假死，观战列表不可见");
                }
                else
                {
                    Features.Notify("反观战 开 — 位置钉地底，观战为黑屏");
                }
            }
            else
            {
                if (Features.IsHost && HealthField != null) ForceHealth((byte)entity_player.MAX_HEALTH);
                Features.Notify("反观战 关");
            }
        }

        private static void ForceHealth(byte value)
        {
            var local = PlayerController.LOCAL;
            if (local == null || HealthField == null) return;
            try
            {
                var healthVar = (NetVar<HEALTH>)HealthField.GetValue(local);
                if (healthVar.Value.health != value)
                    healthVar.SetSpawnValue(new HEALTH { health = value });
            }
            catch (System.Exception e) { Features.Notify("反观战写血失败: " + e.Message); }
        }
    }
}
