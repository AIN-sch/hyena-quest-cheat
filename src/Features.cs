using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>鼠标按钮（InputSystem 鼠标上可绑的键）。</summary>
    public enum MBtn { Left, Right, Middle, Forward, Back }

    /// <summary>热键：键盘键 或 鼠标按钮 二选一。串行化格式 "K:F3" / "M:Left"。</summary>
    public struct Hotkey
    {
        public bool IsMouse;
        public Key Key;
        public MBtn MouseBtn;

        public static Hotkey FromKey(Key k) => new Hotkey { IsMouse = false, Key = k };
        public static Hotkey FromMouse(MBtn m) => new Hotkey { IsMouse = true, MouseBtn = m };

        public bool WasPressedThisFrame()
        {
            if (IsMouse)
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return false;
                switch (MouseBtn)
                {
                    case MBtn.Left: return m.leftButton.wasPressedThisFrame;
                    case MBtn.Right: return m.rightButton.wasPressedThisFrame;
                    case MBtn.Middle: return m.middleButton.wasPressedThisFrame;
                    case MBtn.Forward: return m.forwardButton.wasPressedThisFrame;
                    case MBtn.Back: return m.backButton.wasPressedThisFrame;
                }
                return false;
            }
            return Keyboard.current != null && Keyboard.current[Key].wasPressedThisFrame;
        }

        public string Label()
        {
            if (IsMouse)
            {
                switch (MouseBtn)
                {
                    case MBtn.Left: return "鼠标左键";
                    case MBtn.Right: return "鼠标右键";
                    case MBtn.Middle: return "鼠标中键";
                    case MBtn.Forward: return "鼠标侧键前";
                    case MBtn.Back: return "鼠标侧键后";
                }
                return "鼠标?";
            }
            return Key.ToString();
        }

        public string Serialize() => IsMouse ? "M:" + MouseBtn : "K:" + Key;

        public static Hotkey Deserialize(string s)
        {
            if (!string.IsNullOrEmpty(s) && s.StartsWith("M:"))
            {
                if (Enum.TryParse(s.Substring(2), out MBtn m)) return FromMouse(m);
            }
            else if (!string.IsNullOrEmpty(s) && s.StartsWith("K:"))
            {
                if (Enum.TryParse(s.Substring(2), out Key k)) return FromKey(k);
            }
            if (Enum.TryParse(s, out Key k2)) return FromKey(k2);   // 兼容旧格式
            return FromKey(Key.None);
        }
    }

    /// <summary>功能开关 / 菜单状态。全部是运行时变量，菜单里直接改。</summary>
    public static class Features
    {
        // ---- 热键（可在菜单里改，持久化到 BepInEx cfg）----
        public enum HotkeyAction { None, Dial, Deliver, Vacuum }

        public static Hotkey DialHotkey = Hotkey.FromKey(Key.F3);
        public static Hotkey DeliverHotkey = Hotkey.FromKey(Key.F4);
        public static Hotkey VacuumHotkey = Hotkey.FromKey(Key.F5);
        public static HotkeyAction Rebind = HotkeyAction.None;
        public static Action PersistHotkeys;   // 由 Plugin.Awake 接入：把当前热键写回 cfg
        // ---- UI ----
        public static bool MenuOpen = true;          // 默认打开面板
        public static bool AutoOrderRequested;       // 菜单按钮请求：一键配送
        public static bool AutoWinRequested;         // 菜单按钮请求：一键解放双手

        // ---- 功能开关 ----
        public static bool GodMode;                  // 无敌

        public static bool AAEnabled;                // 旋转AA
        public static bool AASpin;                   // AA：持续转圈
        public static float AAOffset = 180f;         // AA：静态偏移角度
        public static float AASpeed = 360f;          // AA：转圈速度 °/s（最高 36000 = 100圈/s）
        public static float AABow = 0f;              // AA：模型低头角度（转圈时头朝下），0=不低
        public static bool VacuumInstant = true;     // 吸废料：直接吸满（房主瞬间/客户端批量结算）

        public static bool SpeedBoost;               // 移动加速
        public static float SpeedTarget = 2f;        // 目标倍率（滑条 1-10）
        public static float SpeedMult = 1f;          // 当前倍率（向目标渐变，非瞬跳）
        private const float SpeedRampRate = 2.5f;    // 倍率爬升速度（每秒增加多少倍，约4秒到10x）

        public static bool Fly;                      // 飞行：3D移动，零重力（WASD+空格上/ctrl下）
        public static bool Noclip;                   // 穿墙：关碰撞（自动带飞行控制，不然会沉底）
        public const float FlySpeed = 15f;           // 飞行基础速度 m/s（再叠移动加速倍率）

        // ---- 状态提示 ----
        public static string LastMsg = "";
        private static float _msgTime;

        /// <summary>是否在对局内（本地玩家存在且状态 PLAYING）。功能启动前先查，避免在大厅/结算误跑。</summary>
        public static bool InRound()
        {
            var local = PlayerController.LOCAL;
            if (local == null) return false;
            var ig = NetController<IngameController>.Instance;
            if (ig == null) return false;
            return ig.Status() == INGAME_STATUS.PLAYING;
        }

        /// <summary>是否房主（本机即服务器）。解放双手的账本直写需要。</summary>
        public static bool IsHost
            => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>每帧调用：移动倍率向目标平滑渐变。</summary>
        public static void Tick()
        {
            float target = SpeedBoost ? SpeedTarget : 1f;
            SpeedMult = Mathf.MoveTowards(SpeedMult, target, SpeedRampRate * Time.deltaTime);
        }

        public static void Notify(string msg)
        {
            LastMsg = msg;
            _msgTime = Time.time;
            Debug.Log("[HQCheat] " + msg);
        }

        public static void OnMenuToggle()
        {
        }

        /// <summary>每帧调用：改键状态下捕获下一键（键盘/鼠标）并赋值。</summary>
        public static void CaptureHotkey()
        {
            if (Rebind == HotkeyAction.None) return;

            // Esc 取消
            if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame)
            {
                Rebind = HotkeyAction.None;
                Notify("已取消改键");
                return;
            }

            // 鼠标按钮
            if (Mouse.current != null)
            {
                MBtn? pressed = null;
                if (Mouse.current.leftButton.wasPressedThisFrame) pressed = MBtn.Left;
                else if (Mouse.current.rightButton.wasPressedThisFrame) pressed = MBtn.Right;
                else if (Mouse.current.middleButton.wasPressedThisFrame) pressed = MBtn.Middle;
                else if (Mouse.current.forwardButton.wasPressedThisFrame) pressed = MBtn.Forward;
                else if (Mouse.current.backButton.wasPressedThisFrame) pressed = MBtn.Back;
                if (pressed.HasValue) { AssignHotkey(Hotkey.FromMouse(pressed.Value)); return; }
            }

            // 键盘（原始按键状态，菜单开着也读得到）
            if (Keyboard.current != null)
            {
                foreach (var k in Keyboard.current.allKeys)
                {
                    if (k.wasPressedThisFrame && k.keyCode != Key.None && k.keyCode != Key.Escape)
                    {
                        AssignHotkey(Hotkey.FromKey(k.keyCode));
                        return;
                    }
                }
            }
        }

        private static void AssignHotkey(Hotkey h)
        {
            switch (Rebind)
            {
                case HotkeyAction.Dial: DialHotkey = h; break;
                case HotkeyAction.Deliver: DeliverHotkey = h; break;
                case HotkeyAction.Vacuum: VacuumHotkey = h; break;
            }
            Rebind = HotkeyAction.None;
            Notify("热键已设为 " + h.Label());
            try { PersistHotkeys?.Invoke(); } catch { }
        }

        /// <summary>IMGUI 面板。开游戏默认显示，INS 切换。</summary>
        public static class Menu
        {
            private static Rect _win = new Rect(48f, 40f, 540f, 600f);
            private static Vector2 _scroll;      // 内容超高时滚动，防按钮截断
            private static bool _resizing;       // 正在拖拽右下角缩放窗口
            private const float MinWinW = 340f;  // 窗口最小宽/高
            private const float MinWinH = 400f;
            private static Texture2D _gripTex;   // 右下角缩放提示图标
            private static Font _cjk;
            private static bool _fontTried;
            private static GUIStyle _redBtn;
            private static GUIStyle _titleStyle;
            private static GUIStyle _statusStyle;

            /// <summary>加载 CJK 字体（IMGUI 默认字体无中文，会显示成方框）。按 微软雅黑→黑体→宋体→等线 回退。</summary>
            private static void EnsureCjkFont()
            {
                if (_fontTried) return;
                _fontTried = true;
                try
                {
                    _cjk = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体", "DengXian" },
                        15);
                    if (_cjk != null) GUI.skin.font = _cjk;
                }
                catch { /* 加载失败退回默认字体 */ }
            }

            /// <summary>红色粗体按钮样式（一键解放双手）。</summary>
            private static GUIStyle RedBtn()
            {
                if (_redBtn == null)
                {
                    _redBtn = new GUIStyle(GUI.skin.button)
                    {
                        richText = true,
                        fontSize = 15,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return _redBtn;
            }

            /// <summary>辅助标题样式。</summary>
            private static GUIStyle TitleStyle()
            {
                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(GUI.skin.label)
                    {
                        richText = true,
                        fontSize = 13,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return _titleStyle;
            }

            /// <summary>对局状态样式。</summary>
            private static GUIStyle StatusStyle()
            {
                if (_statusStyle == null)
                {
                    _statusStyle = new GUIStyle(GUI.skin.label)
                    {
                        richText = true,
                        fontSize = 12,
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return _statusStyle;
            }

            public static void Draw()
            {
                // 面板打开期间强制显示鼠标、不锁定
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                EnsureCjkFont();

                // ---- 右下角拖拽缩放（在 GUI.Window 外处理，直接用全局鼠标坐标）----
                var e = Event.current;
                Rect gripHit = new Rect(_win.x + _win.width - 24f, _win.y + _win.height - 24f, 24f, 24f);
                if (e.type == EventType.MouseDown && gripHit.Contains(e.mousePosition))
                {
                    _resizing = true;
                    e.Use();
                }
                if (_resizing && e.type == EventType.MouseDrag)
                {
                    _win.width = Mathf.Max(MinWinW, e.mousePosition.x - _win.x + 8f);
                    _win.height = Mathf.Max(MinWinH, e.mousePosition.y - _win.y + 8f);
                    e.Use();
                }
                if (_resizing && (e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow))
                {
                    _resizing = false;
                    e.Use();
                }

                _win = GUI.Window(991001, _win, DoWindow, "Delivery & Beyond Cheat");
            }

            /// <summary>右下角缩放提示图标（三条向右下的小斜线）。</summary>
            private static Texture2D GripTex()
            {
                if (_gripTex != null) return _gripTex;
                const int s = 20;
                _gripTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                Color line = new Color(1f, 1f, 1f, 0.6f);
                Color empty = new Color(0f, 0f, 0f, 0f);
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        bool on = false;
                        for (int k = 0; k < 3; k++)
                        {
                            int o = 2 + k * 2;                 // 三条平行斜线，都在右下角区域
                            if (y >= 9 && y <= 14 && x == y + o) { on = true; break; }
                        }
                        _gripTex.SetPixel(x, y, on ? line : empty);
                    }
                }
                _gripTex.Apply();
                _gripTex.wrapMode = TextureWrapMode.Clamp;
                return _gripTex;
            }

            private static int _tab;   // 0=自动 1=操控 2=热键
            private static readonly Color SelNavColor = new Color(0.25f, 0.55f, 1f, 0.9f);   // 导航栏当前项高亮

            private static void DoWindow(int id)
            {
                GUILayout.BeginVertical();

                // 辅助标题
                GUILayout.Label("<b>AleOsh.独立开发</b>  QQ:3229546706", TitleStyle());
                GUILayout.Label("— Delivery & Beyond Cheat —", TitleStyle());

                // 对局状态（防在大厅/结算误触）
                bool inRound = Features.InRound();
                string state = inRound ? "<color=#00ff88>● 对局中</color>" : "<color=#ff5555>● 未在对局</color>";
                string host = Features.IsHost ? " · 房主" : " · 客户端";
                GUILayout.Label(state + host, StatusStyle());

                GUILayout.Space(4);

                // ---- 左导航栏固定，右侧功能区独立滚动 ----
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(56));
                if (_tab == 0) GUI.backgroundColor = SelNavColor;
                if (GUILayout.Button("自动", GUILayout.Height(30))) _tab = 0;
                GUI.backgroundColor = Color.white;
                if (_tab == 1) GUI.backgroundColor = SelNavColor;
                if (GUILayout.Button("操控", GUILayout.Height(30))) _tab = 1;
                GUI.backgroundColor = Color.white;
                if (_tab == 2) GUI.backgroundColor = SelNavColor;
                if (GUILayout.Button("热键", GUILayout.Height(30))) _tab = 2;
                GUI.backgroundColor = Color.white;
                GUILayout.EndVertical();

                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(_win.width - 120f), GUILayout.Height(_win.height - 132f));
                switch (_tab)
                {
                    case 1: DrawKillTab(); break;
                    case 2: DrawHotkeyTab(); break;
                    default: DrawAutoTab(); break;
                }
                GUILayout.EndScrollView();
                GUILayout.EndHorizontal();

                // 状态提示（始终在最下面可见）
                if (!string.IsNullOrEmpty(LastMsg) && Time.time - _msgTime < 8f)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("状态: " + LastMsg);
                }
                else if (!string.IsNullOrEmpty(LastMsg))
                {
                    LastMsg = "";
                }

                GUILayout.Space(4);
                if (GUILayout.Button("隐藏面板 (INS)")) MenuOpen = false;

                GUILayout.EndVertical();

                // 右下角缩放提示（拖这里可以自己调窗口大小）
                GUI.DrawTexture(new Rect(_win.width - 18f, _win.height - 18f, 18f, 18f), GripTex());

                GUI.DragWindow(new Rect(0f, 0f, _win.width, 24f));
            }

            /// <summary>自动标签页：解放双手 / 无敌 / 吸废料 / 配送 / AA / 移动加速。</summary>
            private static void DrawAutoTab()
            {
                // 红色一键解放双手
                string winLabel = AutoWin.IsActive ? "<color=#ff5555>一键解放双手 (进行中...) [点击停止]</color>" : "<color=#ff3333>一键解放双手</color>";
                var winContent = new GUIContent(
                    winLabel,
                    "建议与朋友玩就别开了，自己本地玩玩得了，不然太作弊了。\n功能用途：一键替你完成所有的废料吸收、回收、拨号、送货···所有任务，一键通关。\n（需房主/主机）");
                if (GUILayout.Button(winContent, RedBtn(), GUILayout.Height(32)))
                {
                    if (AutoWin.IsActive) AutoWin.Stop();
                    else AutoWinRequested = true;
                }

                // 悬停提示（IMGUI tooltip）
                if (!string.IsNullOrEmpty(GUI.tooltip))
                {
                    GUILayout.Box(GUI.tooltip, GUI.skin.box);
                }

                GUILayout.Space(4);

                GodMode = GUILayout.Toggle(GodMode, "无敌");

                if (GUILayout.Button(VacuumAll.IsActive ? "一键吸取废料 (吸取中...) [点击停止]" : "一键吸取废料"))
                {
                    if (VacuumAll.IsActive) VacuumAll.Stop();
                    else VacuumAll.Start();
                }

                // 直接吸满：房主服务端直写瞬间填袋；客户端全图标记并行结算（~1s）
                bool inst = VacuumInstant;
                bool newInst = GUILayout.Toggle(inst, "直接吸满 (房主瞬间/客户端~1s)");
                if (newInst != inst) VacuumInstant = newInst;

                // 一键配送：拨号+拿取+送达完整按钮，循环开关，全程弹开接近电话的玩家
                if (GUILayout.Button(AutoOrder.IsActive ? "一键配送 (循环中...) [点击停止]" : "一键配送"))
                {
                    if (AutoOrder.IsActive) { AutoOrder.Stop(); Features.Notify("已停止一键配送"); }
                    else AutoOrderRequested = true;
                }

                // 房主：袋满自动回收船上，吸废料取到底
                bool autoRecycle = VacuumAll.AutoRecycle;
                bool newAutoRecycle = GUILayout.Toggle(autoRecycle, "满袋自动回收船上[房主]");
                if (newAutoRecycle != autoRecycle) VacuumAll.AutoRecycle = newAutoRecycle;

                GUILayout.Space(6);
                GUILayout.Label("旋转AA (别人视角)");
                AAEnabled = GUILayout.Toggle(AAEnabled, "启用AA");
                AASpin = GUILayout.Toggle(AASpin, "转圈");
                if (AASpin)
                {
                    GUILayout.BeginHorizontal();
                    AASpeed = Mathf.Round(GUILayout.HorizontalSlider(AASpeed, 60f, 36000f));
                    if (GUILayout.Button("拉满", GUILayout.Width(40))) AASpeed = 36000f;
                    GUILayout.EndHorizontal();
                    GUILayout.Label("速度 " + AASpeed + "°/s (≈" + (AASpeed / 360f).ToString("0.#") + "圈/s)");
                    AABow = Mathf.Round(GUILayout.HorizontalSlider(AABow, 0f, 90f));
                    GUILayout.Label("低头 " + AABow + "° (转圈时模型头朝下，0=不低)");
                }
                else
                {
                    AAOffset = Mathf.Round(GUILayout.HorizontalSlider(AAOffset, -180f, 180f));
                    GUILayout.Label("偏移 " + AAOffset + "°");
                }

                GUILayout.Space(6);
                GUILayout.Label("移动加速");
                SpeedBoost = GUILayout.Toggle(SpeedBoost, "加速");
                SpeedTarget = Mathf.Round(GUILayout.HorizontalSlider(SpeedTarget, 1f, 10f) * 10f) / 10f;
                GUILayout.Label("倍率 " + SpeedTarget.ToString("0.0") + "x (最高10x)");

                GUILayout.Space(6);
                GUILayout.Label("飞行 / 穿墙");
                Fly = GUILayout.Toggle(Fly, "飞行 (WASD + 空格上 / Ctrl 下)");
                Noclip = GUILayout.Toggle(Noclip, "穿墙 (关碰撞，自动带飞行控制)");

                GUILayout.Space(8);
                GUILayout.Label("<color=#88ff88>— 公屏刷屏 —</color>", TitleStyle());
                ChatSpam.Message = GUILayout.TextField(ChatSpam.Message);
                GUILayout.BeginHorizontal();
                ChatSpam.Interval = Mathf.Clamp(GUILayout.HorizontalSlider(ChatSpam.Interval, 0.05f, 2f), 0.05f, 2f);
                GUILayout.Label("间隔 " + ChatSpam.Interval.ToString("0.00") + "s", GUILayout.Width(96));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("发送一次", GUILayout.Width(82))) ChatSpam.Send(ChatSpam.Message);
                if (GUILayout.Button(ChatSpam.Active ? "停止刷屏" : "开始刷屏", GUILayout.Width(90)))
                {
                    if (ChatSpam.Active) ChatSpam.Stop();
                    else ChatSpam.Active = true;
                }
                GUILayout.EndHorizontal();
            }

            /// <summary>操控标签页：秒杀/推飞/控血/抢物/丢包 + 玩家列表。</summary>
            private static void DrawKillTab()
            {
                GUILayout.Label("<color=#ff8844>— 秒杀 / 操控 (列表每秒自动刷新，不含自己) —</color>", TitleStyle());
                string killNote = Features.IsHost
                    ? "玩家 " + KillTarget.Players.Count + " 人 · 循环=持续生效 · 满袋/拉人=房主"
                    : "玩家 " + KillTarget.Players.Count + " 人 · 循环=持续生效 · 满袋/拉人[房主]=只有你当房主才能用";
                GUILayout.Label(killNote, StatusStyle());

                // 全局操作（对所有人，全远程点按钮）
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("全员推飞", GUILayout.Width(74))) KillTarget.ShoveAllOnce();
                if (GUILayout.Button("全员血1", GUILayout.Width(70))) KillTarget.SetBloodAll(1);
                if (GUILayout.Button("全员丢包", GUILayout.Width(74))) KillTarget.DropAllPlayers();
                if (GUILayout.Button("杀全部", GUILayout.Width(62))) KillTarget.KillAll();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("全员抢物到脚下", GUILayout.Width(132))) KillTarget.StealAll();
                if (GUILayout.Button("全员连推", GUILayout.Width(74))) KillTarget.ShoveLoopAll(true);
                if (GUILayout.Button("一键满袋(房主)", GUILayout.Width(112))) KillTarget.RefillBag();
                if (GUILayout.Button("碎全场玻璃", GUILayout.Width(88))) KillTarget.BreakAllGlass();
                GUILayout.EndHorizontal();
                // 复活：自己/全员（零成本，客户端也能用）
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("复活自己", GUILayout.Width(88))) KillTarget.ReviveSelf();
                if (GUILayout.Button("复活全员", GUILayout.Width(88))) KillTarget.ReviveAll();
                GUILayout.EndHorizontal();
                if (GUILayout.Button("自杀 (仅手动，任何功能都不会自动触发)", GUILayout.Height(22))) KillTarget.Suicide();

                // 每个玩家三行：循环开关（杀/推/满血/1血）+ 循环抢/丢 + 单次按钮
                foreach (var p in KillTarget.Players)
                {
                    if (p == null || !p.IsSpawned) continue;
                    string nm = p.GetPlayerName();
                    if (nm.Length > 6) nm = nm.Substring(0, 6) + "…";
                    string st = p.IsDead() ? "✝" : "●";
                    // 第一行：名字 + 循环杀/循环推/循环满血/循环1血
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format("{0} {1}血{2}", nm, st, p.GetHealth()), GUILayout.Width(88));
                    bool lp = KillTarget.IsLooping(p);
                    lp = GUILayout.Toggle(lp, "循环杀", GUILayout.Width(70));
                    KillTarget.SetLoop(p, lp);
                    bool sl = KillTarget.IsShoveLooping(p);
                    sl = GUILayout.Toggle(sl, "循环推", GUILayout.Width(70));
                    KillTarget.SetShoveLoop(p, sl);
                    byte bt = KillTarget.GetBloodLoopTarget(p);
                    bool bFull = KillTarget.IsBloodLooping(p) && bt == 100;
                    bool bFullN = GUILayout.Toggle(bFull, "循环满血", GUILayout.Width(84));
                    if (bFullN && !bFull) KillTarget.SetBloodLoop(p, 100);
                    else if (!bFullN && bFull) KillTarget.StopBloodLoop(p);
                    bool bOne = KillTarget.IsBloodLooping(p) && bt == 1;
                    bool bOneN = GUILayout.Toggle(bOne, "循环1血", GUILayout.Width(78));
                    if (bOneN && !bOne) KillTarget.SetBloodLoop(p, 1);
                    else if (!bOneN && bOne) KillTarget.StopBloodLoop(p);
                    GUILayout.EndHorizontal();
                    // 第二行：循环抢/循环丢 + 杀/推/血1
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(88);
                    bool sl2 = KillTarget.IsStealLooping(p);
                    sl2 = GUILayout.Toggle(sl2, "循环抢", GUILayout.Width(70));
                    KillTarget.SetStealLoop(p, sl2);
                    bool dl = KillTarget.IsDropLooping(p);
                    dl = GUILayout.Toggle(dl, "循环丢", GUILayout.Width(70));
                    KillTarget.SetDropLoop(p, dl);
                    if (GUILayout.Button("杀", GUILayout.Width(34))) KillTarget.KillOnce(p);
                    if (GUILayout.Button("推", GUILayout.Width(34))) KillTarget.ShoveOnce(p);
                    if (GUILayout.Button("血1", GUILayout.Width(42))) KillTarget.SetBlood(p, 1);
                    GUILayout.EndHorizontal();
                    // 第三行：死了显示绿色[复活]，活着显示[满血]，同一动作(SetHealthRPC 100)；再加 抢/丢包
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(88);
                    if (p.IsDead())
                    {
                        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f, 0.9f);   // 绿 = 复活
                        if (GUILayout.Button("复活", GUILayout.Width(48))) KillTarget.RevivePlayer(p);
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        if (GUILayout.Button("满血", GUILayout.Width(48))) KillTarget.SetBlood(p, 100);
                    }
                    if (GUILayout.Button("抢", GUILayout.Width(34))) KillTarget.StealItems(p);
                    if (GUILayout.Button("丢包", GUILayout.Width(48))) KillTarget.DropAllItems(p);
                    GUILayout.EndHorizontal();
                }
            }

            /// <summary>热键标签页：只剩吸废料一个热键（拨号/拿取已并入一键配送）。</summary>
            private static void DrawHotkeyTab()
            {
                GUILayout.Space(6);
                GUILayout.Label("热键 (点[改]再按新键)");
                HotkeyRow("吸废料", HotkeyAction.Vacuum, VacuumHotkey);
                GUILayout.Space(6);
                GUILayout.Label("拨号/拿取配送已并入「一键配送」按钮，无需单独热键");
            }

            private static void HotkeyRow(string label, HotkeyAction action, Hotkey hotkey)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(70));
                GUILayout.Label(action == Rebind ? "按新键..." : "[" + hotkey.Label() + "]", GUILayout.Width(100));
                if (GUILayout.Button("改", GUILayout.Width(38))) Rebind = action;
                GUILayout.EndHorizontal();
            }
        }
    }
}
