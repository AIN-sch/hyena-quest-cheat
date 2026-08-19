// Esp.cs — ESP透视：玩家 8 角投影 3D 方框 + 头顶信息标签。
using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>ESP 透视：玩家 3D 方框 + 头顶信息标签。</summary>
    public static class Esp
    {
        private static Texture2D _white;
        private static GUIStyle _labelStyle;

        private const float BoxW = 0.8f;    // 框宽（米）
        private const float BoxH = 1.9f;    // 框高（米）

        private static Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            return _white;
        }

        private static GUIStyle LabelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = 11;
                _labelStyle.richText = true;
                _labelStyle.alignment = TextAnchor.UpperLeft;
            }
            return _labelStyle;
        }

        /// <summary>OnGUI 每帧绘制：开关开启才画，框所有玩家（不含本地）。</summary>
        public static void Draw()
        {
            if (!Features.EspEnabled) return;
            var cam = SDK.MainCamera;
            if (cam == null) return;
            var local = PlayerController.LOCAL;
            if (local == null || !Features.InRound()) return;

            var pc = MonoController<PlayerController>.Instance;
            var all = pc != null ? pc.GetAllPlayers() : null;
            if (all == null) return;

            int idx = 0;
            foreach (var p in all)
            {
                if (p == null || !p.IsSpawned) continue;
                if (ReferenceEquals(p, local)) continue;   // 不框本地
                // 彩虹流转变色：色相随时间循环，每个玩家相位错开好区分
                float hue = (Time.time * 0.35f + idx * 0.13f) % 1f;
                DrawPlayer(cam, p, Color.HSVToRGB(hue, 1f, 1f));
                idx++;
            }
        }

        private static void DrawPlayer(Camera cam, entity_player p, Color boxColor)
        {
            Vector3 pos = p.transform.position;
            bool dead = p.IsDead();

            // 8 个世界角：框底在脚，顶在脚+高，水平沿玩家右/前方向
            Vector3 center = pos + Vector3.up * (BoxH * 0.5f);
            Vector3 R = p.transform.right, F = p.transform.forward;
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;
                c[i] = center + R * (sx * BoxW * 0.5f) + Vector3.up * (sy * BoxH * 0.5f) + F * (sz * BoxW * 0.5f);
            }

            // 投影 8 角；任一角在相机背后(z<0)则跳过整框
            Vector2[] s = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(c[i]);
                if (sp.z < 0f) return;
                s[i] = new Vector2(sp.x, Screen.height - sp.y);
            }

            Color col = dead ? new Color(0.55f, 0.55f, 0.55f) : boxColor;
            DrawBoxLines(s, col);

            // 头顶标签
            Vector3 head = cam.WorldToScreenPoint(pos + Vector3.up * (BoxH + 0.12f));
            if (head.z < 0f) return;
            Vector2 lp = new Vector2(head.x, Screen.height - head.y);

            // 手持物品名字
            string held = "";
            try
            {
                var pg = p.GetPhysgun();
                var h = pg != null ? pg.GetGrabbingObject() : null;
                var it = h as entity_item;
                if (it != null) held = it.GetID();
                else if (h != null) held = h.GetType().Name;
            }
            catch { held = ""; }

            string text = p.GetPlayerName() + (dead ? "  ✝死亡" : "");
            text += "\n血 " + p.GetHealth();
            text += "\n废料 " + p.GetPlayerScrap();
            if (!string.IsNullOrEmpty(held)) text += "\n手持 " + held;

            DrawLabel(lp, text, dead ? Color.gray : Color.white);
        }

        // 立方体 12 条边
        private static readonly int[][] Edges = new int[][] {
            new int[] {0,1}, new int[] {1,3}, new int[] {3,2}, new int[] {2,0},
            new int[] {4,5}, new int[] {5,7}, new int[] {7,6}, new int[] {6,4},
            new int[] {0,4}, new int[] {1,5}, new int[] {2,6}, new int[] {3,7}
        };

        private static void DrawBoxLines(Vector2[] s, Color col)
        {
            for (int e = 0; e < Edges.Length; e++)
                Line(s[Edges[e][0]], s[Edges[e][1]], col, 2f);
        }

        private static void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return;
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), White());
            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        // 8 方向偏移做黑描边，白字压上面
        private static readonly Vector2[] Offsets = {
            new Vector2(1,0), new Vector2(-1,0), new Vector2(0,1), new Vector2(0,-1),
            new Vector2(1,1), new Vector2(-1,-1), new Vector2(1,-1), new Vector2(-1,1)
        };

        private static void DrawLabel(Vector2 pos, string text, Color c)
        {
            var style = LabelStyle();
            var rect = new Rect(pos.x, pos.y, 300f, 72f);
            var prevColor = GUI.color;
            GUI.color = Color.black;
            foreach (var off in Offsets)
                GUI.Label(new Rect(rect.x + off.x, rect.y + off.y, rect.width, rect.height), text, style);
            GUI.color = c;
            GUI.Label(rect, text, style);
            GUI.color = prevColor;
        }
    }
}
