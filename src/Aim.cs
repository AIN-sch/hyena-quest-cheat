// Aim.cs — 瞄准判定：主相机投影玩家到屏幕，取中心阈值内最近者。
using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    public static class Aim
    {
        /// <summary>瞄准容差（像素）：投影点离屏幕中心超过该值即不判定。</summary>
        public const float MaxDistPx = 170f;

        private static readonly Vector2[] ProbeOffsets =
        {
            Vector2.zero,
            new Vector2(0f, 0.4f),  new Vector2(0f, -0.4f),   // 身体上下采样，头部以下也算
            new Vector2(0.4f, 0f),  new Vector2(-0.4f, 0f),
            new Vector2(0f, 0.9f),
        };

        /// <summary>返回镜头正对的玩家（不含本地）。多采样点取距中心最近者。</summary>
        public static entity_player GetAimedPlayer(float maxDistPx = MaxDistPx)
        {
            var cam = SDK.MainCamera;
            var local = PlayerController.LOCAL;
            if (cam == null || local == null) return null;
            var pc = MonoController<PlayerController>.Instance;
            if (pc == null) return null;
            var all = pc.GetAllPlayers();
            if (all == null) return null;

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float bestSq = maxDistPx * maxDistPx;
            entity_player best = null;

            foreach (var p in all)
            {
                if (p == null || !p.IsSpawned) continue;
                if (ReferenceEquals(p, local)) continue;

                // 多个采样点里取最近的那个（都在屏幕内才算）
                float localBest = bestSq;
                bool anyInFront = false;
                foreach (var off in ProbeOffsets)
                {
                    Vector3 wp = p.transform.position + new Vector3(off.x, off.y + 0.9f, 0f);
                    Vector3 sp = cam.WorldToScreenPoint(wp);
                    if (sp.z <= 0f) continue;   // 在相机背后
                    anyInFront = true;
                    Vector2 spos = new Vector2(sp.x, Screen.height - sp.y);
                    float d = (spos - center).sqrMagnitude;
                    if (d < localBest) localBest = d;
                }
                if (!anyInFront) continue;
                if (localBest < bestSq) { bestSq = localBest; best = p; }
            }
            return best;
        }

        /// <summary>瞄准目标的名字（无目标返回 null）。</summary>
        public static string AimedName(entity_player p)
            => p == null ? null : p.GetPlayerName();
    }
}
