using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>鼠标按钮（InputSystem 鼠标上可绑的键）。</summary>
    public enum MBtn { Left, Right, Middle, Forward, Back }

    /// <summary>热键触发方式：切换(按一下翻转)/长按(按住开松开关)/单击(按一下执行一次)。</summary>
    public enum HotkeyMode { Toggle, Hold, Tap }

    /// <summary>热键：键盘键 或 鼠标按钮 二选一 + 触发方式。串行化 "K:F3|T" / "M:Left|H"（|后为模式）。</summary>
    public struct Hotkey
    {
        public bool IsMouse;
        public Key Key;
        public MBtn MouseBtn;
        public HotkeyMode Mode;

        public static Hotkey FromKey(Key k) => new Hotkey { IsMouse = false, Key = k };
        public static Hotkey FromMouse(MBtn m) => new Hotkey { IsMouse = true, MouseBtn = m };

        public bool IsBound => IsMouse || Key != Key.None;

        private bool IsDown()
        {
            if (IsMouse)
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return false;
                switch (MouseBtn)
                {
                    case MBtn.Left: return m.leftButton.isPressed;
                    case MBtn.Right: return m.rightButton.isPressed;
                    case MBtn.Middle: return m.middleButton.isPressed;
                    case MBtn.Forward: return m.forwardButton.isPressed;
                    case MBtn.Back: return m.backButton.isPressed;
                }
                return false;
            }
            return Keyboard.current != null && Keyboard.current[Key].isPressed;
        }

        /// <summary>本帧按下过（触发沿）。</summary>
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

        /// <summary>本帧松开过（松开沿，长按模式用）。</summary>
        public bool WasReleasedThisFrame()
        {
            if (IsMouse)
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return false;
                switch (MouseBtn)
                {
                    case MBtn.Left: return m.leftButton.wasReleasedThisFrame;
                    case MBtn.Right: return m.rightButton.wasReleasedThisFrame;
                    case MBtn.Middle: return m.middleButton.wasReleasedThisFrame;
                    case MBtn.Forward: return m.forwardButton.wasReleasedThisFrame;
                    case MBtn.Back: return m.backButton.wasReleasedThisFrame;
                }
                return false;
            }
            return Keyboard.current != null && Keyboard.current[Key].wasReleasedThisFrame;
        }

        /// <summary>当前是否按住（长按模式用）。</summary>
        public bool IsHeld() => IsDown();

        public string Label()
        {
            if (IsMouse)
            {
                switch (MouseBtn)
                {
                    case MBtn.Left: return "鼠标左键";
                    case MBtn.Right: return "鼠标右键";
                    case MBtn.Middle: return "鼠标中键";
                    case MBtn.Forward: return "侧键前";
                    case MBtn.Back: return "侧键后";
                }
                return "鼠标?";
            }
            return Key.ToString();
        }

        public string ModeName() => Mode switch
        {
            HotkeyMode.Hold => "长按",
            HotkeyMode.Tap => "单击",
            _ => "切换"
        };

        private char ModeChar() => Mode switch
        {
            HotkeyMode.Hold => 'H',
            HotkeyMode.Tap => 'P',
            _ => 'T'
        };

        /// <summary>循环切换模式（菜单点按钮用）。</summary>
        public void CycleMode() => Mode = (HotkeyMode)(((int)Mode + 1) % 3);

        public string Serialize() => (IsMouse ? "M:" + MouseBtn : "K:" + Key) + "|" + ModeChar();

        public static Hotkey Deserialize(string s)
        {
            Hotkey h = FromKey(Key.None);
            if (string.IsNullOrEmpty(s)) return h;
            int bar = s.IndexOf('|');
            string basePart = bar >= 0 ? s.Substring(0, bar) : s;
            if (basePart.StartsWith("M:") && Enum.TryParse(basePart.Substring(2), out MBtn m)) h = FromMouse(m);
            else if (basePart.StartsWith("K:") && Enum.TryParse(basePart.Substring(2), out Key k)) h = FromKey(k);
            else if (Enum.TryParse(basePart, out Key k2)) h = FromKey(k2);   // 兼容旧格式
            if (bar >= 0 && s.Length > bar + 1)
            {
                char mc = s[bar + 1];
                if (mc == 'H') h.Mode = HotkeyMode.Hold;
                else if (mc == 'P') h.Mode = HotkeyMode.Tap;
                else h.Mode = HotkeyMode.Toggle;
            }
            return h;
        }
    }

    /// <summary>功能开关 / 菜单状态。全部是运行时变量，菜单里直接改。</summary>
    public static class Features
    {
        // ==================== 快捷键 ====================
        public enum HotkeyAction
        {
            None,
            // 开关类
            ToggleGod, ToggleEsp, ToggleAA, ToggleSpeed, ToggleFly, ToggleNoclip,
            ToggleAntiGrief, ToggleBroadcast, ToggleAntiSpectate, ToggleVacuumInstant, ToggleAutoRecycle,
            // 动作类
            Vacuum, Deliver, Win, ChatSpam, Dial,
            // 瞄准类
            AimKill, AimRevive, AimBlood1, AimShove, AimSteal, AimDrop
        }

        public static Hotkey HkGod = Hotkey.FromKey(Key.F7);
        public static Hotkey HkEsp = Hotkey.FromKey(Key.F6);
        public static Hotkey HkAA = Hotkey.FromKey(Key.F11);
        public static Hotkey HkSpeed = Hotkey.FromKey(Key.F10);
        public static Hotkey HkFly = Hotkey.FromKey(Key.F8);
        public static Hotkey HkNoclip = Hotkey.FromKey(Key.F9);
        public static Hotkey HkAntiGrief;
        public static Hotkey HkBroadcast;
        public static Hotkey HkAntiSpectate = Hotkey.FromKey(Key.F12);
        public static Hotkey HkVacuumInstant;
        public static Hotkey HkAutoRecycle;
        public static Hotkey HkVacuum = Hotkey.FromKey(Key.F5);
        public static Hotkey HkDeliver = Hotkey.FromKey(Key.F4);
        public static Hotkey HkWin;
        public static Hotkey HkChatSpam;
        public static Hotkey HkDial = Hotkey.FromKey(Key.F3);
        public static Hotkey HkAimKill = Hotkey.FromMouse(MBtn.Forward);
        public static Hotkey HkAimRevive = Hotkey.FromMouse(MBtn.Back);
        public static Hotkey HkAimBlood1;
        public static Hotkey HkAimShove;
        public static Hotkey HkAimSteal;
        public static Hotkey HkAimDrop;

        public static HotkeyAction Rebind = HotkeyAction.None;
        public static Action PersistHotkeys;   // 由 Plugin.Awake 接入：把当前热键写回 cfg

        public static Hotkey GetHotkey(HotkeyAction a)
        {
            switch (a)
            {
                case HotkeyAction.ToggleGod: return HkGod;
                case HotkeyAction.ToggleEsp: return HkEsp;
                case HotkeyAction.ToggleAA: return HkAA;
                case HotkeyAction.ToggleSpeed: return HkSpeed;
                case HotkeyAction.ToggleFly: return HkFly;
                case HotkeyAction.ToggleNoclip: return HkNoclip;
                case HotkeyAction.ToggleAntiGrief: return HkAntiGrief;
                case HotkeyAction.ToggleBroadcast: return HkBroadcast;
                case HotkeyAction.ToggleAntiSpectate: return HkAntiSpectate;
                case HotkeyAction.ToggleVacuumInstant: return HkVacuumInstant;
                case HotkeyAction.ToggleAutoRecycle: return HkAutoRecycle;
                case HotkeyAction.Vacuum: return HkVacuum;
                case HotkeyAction.Deliver: return HkDeliver;
                case HotkeyAction.Win: return HkWin;
                case HotkeyAction.ChatSpam: return HkChatSpam;
                case HotkeyAction.Dial: return HkDial;
                case HotkeyAction.AimKill: return HkAimKill;
                case HotkeyAction.AimRevive: return HkAimRevive;
                case HotkeyAction.AimBlood1: return HkAimBlood1;
                case HotkeyAction.AimShove: return HkAimShove;
                case HotkeyAction.AimSteal: return HkAimSteal;
                case HotkeyAction.AimDrop: return HkAimDrop;
            }
            return default;
        }

        public static void SetHotkey(HotkeyAction a, Hotkey h)
        {
            switch (a)
            {
                case HotkeyAction.ToggleGod: HkGod = h; break;
                case HotkeyAction.ToggleEsp: HkEsp = h; break;
                case HotkeyAction.ToggleAA: HkAA = h; break;
                case HotkeyAction.ToggleSpeed: HkSpeed = h; break;
                case HotkeyAction.ToggleFly: HkFly = h; break;
                case HotkeyAction.ToggleNoclip: HkNoclip = h; break;
                case HotkeyAction.ToggleAntiGrief: HkAntiGrief = h; break;
                case HotkeyAction.ToggleBroadcast: HkBroadcast = h; break;
                case HotkeyAction.ToggleAntiSpectate: HkAntiSpectate = h; break;
                case HotkeyAction.ToggleVacuumInstant: HkVacuumInstant = h; break;
                case HotkeyAction.ToggleAutoRecycle: HkAutoRecycle = h; break;
                case HotkeyAction.Vacuum: HkVacuum = h; break;
                case HotkeyAction.Deliver: HkDeliver = h; break;
                case HotkeyAction.Win: HkWin = h; break;
                case HotkeyAction.ChatSpam: HkChatSpam = h; break;
                case HotkeyAction.Dial: HkDial = h; break;
                case HotkeyAction.AimKill: HkAimKill = h; break;
                case HotkeyAction.AimRevive: HkAimRevive = h; break;
                case HotkeyAction.AimBlood1: HkAimBlood1 = h; break;
                case HotkeyAction.AimShove: HkAimShove = h; break;
                case HotkeyAction.AimSteal: HkAimSteal = h; break;
                case HotkeyAction.AimDrop: HkAimDrop = h; break;
            }
        }

        // ==================== UI ====================
        public static bool MenuOpen = true;          // 默认打开面板
        public static bool AutoOrderRequested;       // 菜单按钮请求：一键配送
        public static bool AutoWinRequested;         // 菜单按钮请求：一键解放双手

        // ==================== 功能开关 ====================
        public static bool GodMode;                  // 无敌
        public static bool AntiGrief;                // 防整·锁物反抢
        public static bool AntiGriefBroadcast;       // 反整播报
        public static bool EspEnabled;               // ESP透视

        public static bool AAEnabled;                // 旋转AA
        public static bool AASpin;                   // AA：持续转圈
        public static float AAOffset = 180f;         // AA：静态偏移角度
        public static float AASpeed = 360f;          // AA：转圈速度 °/s
        public static float AABow = 0f;              // AA：模型低头角度
        public static bool VacuumInstant = true;     // 吸废料：直接吸满
        public static bool AutoRecycle;              // 满袋自动回收（房主）

        public static bool SpeedBoost;               // 移动加速
        public static float SpeedTarget = 2f;        // 目标倍率（滑条 1-10）
        public static float SpeedMult = 1f;          // 当前倍率（向目标渐变）
        private const float SpeedRampRate = 2.5f;

        public static bool Fly;                      // 飞行
        public static bool Noclip;                   // 穿墙
        public const float FlySpeed = 15f;

        public static bool AntiSpectate;             // 反观战（仅房主）

        // ==================== 状态提示 ====================
        public static string LastMsg = "";
        private static float _msgTime;

        /// <summary>是否在对局内（本地玩家存在且状态 PLAYING）。</summary>
        public static bool InRound()
        {
            var local = PlayerController.LOCAL;
            if (local == null) return false;
            var ig = NetController<IngameController>.Instance;
            if (ig == null) return false;
            return ig.Status() == INGAME_STATUS.PLAYING;
        }

        /// <summary>是否房主（本机即服务器）。</summary>
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

        public static void OnMenuToggle() { }

        /// <summary>每帧调用：处理所有已绑定热键（按各自触发模式）。</summary>
        public static void ProcessHotkeys()
        {
            if (Rebind != HotkeyAction.None) return;
            bool menuClosed = !MenuOpen;

            // 开关类：切换=按一下翻转；长按=按住开松开关；单击=按一下翻转
            ProcessToggle(HkGod, ref GodMode, null, "无敌 开", "无敌 关");
            ProcessToggle(HkEsp, ref EspEnabled, null, "ESP 开", "ESP 关");
            ProcessToggle(HkAA, ref AAEnabled, null, "AA 开", "AA 关");
            ProcessToggle(HkSpeed, ref SpeedBoost, null, "加速 开", "加速 关");
            ProcessToggle(HkFly, ref Fly, null, "飞行 开", "飞行 关");
            ProcessToggle(HkNoclip, ref Noclip, null, "穿墙 开", "穿墙 关");
            ProcessToggle(HkAntiGrief, ref AntiGrief, null, "防整 开", "防整 关");
            ProcessToggle(HkBroadcast, ref AntiGriefBroadcast, null, "反整播报 开", "反整播报 关");
            ProcessToggle(HkAntiSpectate, ref AntiSpectate, AntiSpectateCtrl.OnToggle);
            ProcessToggle(HkVacuumInstant, ref VacuumInstant, null, "直接吸满 开", "直接吸满 关");
            ProcessToggle(HkAutoRecycle, ref AutoRecycle, null, "自动回收 开", "自动回收 关");
            ProcessToggle(HkChatSpam, ref ChatSpam.Active, null, "刷屏 开", "刷屏 关");

            // 动作类：切换=运行中停/停了跑；长按=按住跑松开停；单击=启动一次
            ProcessRunStop(HkVacuum, () => VacuumAll.IsActive, VacuumAll.Start, () => VacuumAll.Stop());
            ProcessRunStop(HkDeliver, () => AutoOrder.IsActive,
                () => AutoOrderRequested = true, () => { AutoOrder.Stop(); Notify("已停止一键配送"); });
            ProcessRunStop(HkWin, () => AutoWin.IsActive, () => AutoWinRequested = true, () => AutoWin.Stop());
            ProcessRepeat(HkDial, () => AutoDial.TryDial());   // 拨号

            // 瞄准类（菜单开着不触发，避免误按鼠标；长按=按住持续对当前目标执行）
            if (menuClosed)
            {
                ProcessRepeat(HkAimKill, () => AimAction(HotkeyAction.AimKill));
                ProcessRepeat(HkAimRevive, () => AimAction(HotkeyAction.AimRevive));
                ProcessRepeat(HkAimBlood1, () => AimAction(HotkeyAction.AimBlood1));
                ProcessRepeat(HkAimShove, () => AimAction(HotkeyAction.AimShove));
                ProcessRepeat(HkAimSteal, () => AimAction(HotkeyAction.AimSteal));
                ProcessRepeat(HkAimDrop, () => AimAction(HotkeyAction.AimDrop));
            }
        }

        /// <summary>开关类热键按模式处理。</summary>
        private static void ProcessToggle(Hotkey hk, ref bool feature, Action onChanged = null, string onMsg = "", string offMsg = "")
        {
            if (!hk.IsBound) return;
            if (hk.Mode == HotkeyMode.Hold)
            {
                // 长按：跟着键走，只在变化时通知
                bool held = hk.IsHeld();
                if (held != feature)
                {
                    feature = held;
                    onChanged?.Invoke();
                    if (onMsg.Length > 0) Notify(feature ? onMsg : offMsg);
                }
            }
            else if (hk.WasPressedThisFrame())
            {
                // 切换/单击：按一下翻转
                feature = !feature;
                onChanged?.Invoke();
                if (onMsg.Length > 0) Notify(feature ? onMsg : offMsg);
            }
        }

        /// <summary>运行/停止类热键按模式处理。</summary>
        private static void ProcessRunStop(Hotkey hk, Func<bool> isActive, Action start, Action stop)
        {
            if (!hk.IsBound) return;
            switch (hk.Mode)
            {
                case HotkeyMode.Hold:
                    if (hk.WasPressedThisFrame()) start();
                    else if (hk.WasReleasedThisFrame()) stop();
                    break;
                case HotkeyMode.Tap:
                    if (hk.WasPressedThisFrame() && !isActive()) start();   // 单击：启动一次
                    break;
                default:
                    if (hk.WasPressedThisFrame()) { if (isActive()) stop(); else start(); }
                    break;
            }
        }

        /// <summary>重复动作热键：长按=按住每 0.35s 执行一次，其余=按一下执行一次。</summary>
        private static float _holdRepeat;
        private static void ProcessRepeat(Hotkey hk, Action act)
        {
            if (!hk.IsBound) return;
            if (hk.Mode == HotkeyMode.Hold)
            {
                if (hk.WasPressedThisFrame()) { _holdRepeat = Time.time; act(); }
                else if (hk.IsHeld() && Time.time - _holdRepeat >= 0.35f) { _holdRepeat = Time.time; act(); }
            }
            else if (hk.WasPressedThisFrame()) act();
        }

        /// <summary>菜单热键页点模式按钮：切换/长按/单击循环。</summary>
        public static void CycleMode(HotkeyAction a)
        {
            var hk = GetHotkey(a);
            hk.CycleMode();
            SetHotkey(a, hk);
            Notify(GetHotkeyName(a) + " 改为" + hk.ModeName() + "模式");
            try { PersistHotkeys?.Invoke(); } catch { }
        }

        /// <summary>瞄准热键：镜头指向谁就对谁执行。</summary>
        private static void AimAction(HotkeyAction action)
        {
            var p = Aim.GetAimedPlayer();
            if (p == null) { Notify("没对准任何玩家"); return; }
            switch (action)
            {
                case HotkeyAction.AimKill: KillTarget.KillOnce(p); break;
                case HotkeyAction.AimRevive: KillTarget.RevivePlayer(p); break;
                case HotkeyAction.AimBlood1: KillTarget.SetBlood(p, 1); break;
                case HotkeyAction.AimShove: KillTarget.ShoveOnce(p); break;
                case HotkeyAction.AimSteal: KillTarget.StealItems(p); break;
                case HotkeyAction.AimDrop: KillTarget.DropAllItems(p); break;
            }
        }

        /// <summary>每帧调用：改键状态下捕获下一键并赋值。</summary>
        public static void CaptureHotkey()
        {
            if (Rebind == HotkeyAction.None) return;

            if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame)
            {
                Rebind = HotkeyAction.None;
                Notify("已取消改键");
                return;
            }

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
            SetHotkey(Rebind, h);
            Notify(GetHotkeyName(Rebind) + " 已设为 " + h.Label());
            Rebind = HotkeyAction.None;
            try { PersistHotkeys?.Invoke(); } catch { }
        }

        private static string GetHotkeyName(HotkeyAction a)
        {
            foreach (var kv in Menu.HotkeyItems)
                if (kv.Action == a) return kv.Label;
            return a.ToString();
        }

        /// <summary>IMGUI 面板。INS 切换。暗色 STUDYUI 风格：左标签栏 + 分组卡片。</summary>
        public static class Menu
        {
            private static Rect _win = new Rect(48f, 40f, 620f, 560f);
            private static Vector2 _scroll;
            private static bool _resizing;
            private const float MinWinW = 400f;
            private const float MinWinH = 400f;
            private static Texture2D _gripTex;
            private static Font _cjk;
            private static bool _fontTried;
            private static GUIStyle _winStyle, _navStyle, _navSel, _groupTitle, _status, _hint, _redBtn;
            private static GUIStyle _btn, _btnAccent, _toggleStyle;
            // Ready or Not 外挂同款配色（全实底，不透明）
            private static readonly Color Accent = new Color(0.00f, 0.55f, 0.85f);        // 强调 青蓝
            private static readonly Color AccentHi = new Color(0.00f, 0.70f, 0.90f);      // 高亮 亮青
            private static readonly Color BgWin = new Color(0.01f, 0.00f, 0.02f, 1f);     // 窗口底 近黑
            private static readonly Color BgPanel = new Color(0.04f, 0.00f, 0.08f, 1f);   // 面板
            private static readonly Color BgNav = new Color(0.04f, 0.00f, 0.08f, 1f);     // 左侧栏
            private static readonly Color BtnBg = new Color(0.05f, 0.00f, 0.10f);         // 按钮底 深紫黑
            private static readonly Color BtnBgHover = new Color(0.12f, 0.02f, 0.25f);
            private static readonly Color BtnBgPress = new Color(0.25f, 0.00f, 0.40f);
            private static readonly Color TextMain = new Color(0.90f, 0.80f, 1.00f);      // 主文字 淡紫白
            private static readonly Color TextDim = new Color(0.62f, 0.64f, 0.72f);
            private static readonly Color Border = new Color(0.40f, 0.00f, 0.80f);        // 强紫边框

            // 热键清单（名字 + 动作），菜单改键与提示共用
            public static readonly (HotkeyAction Action, string Label)[] HotkeyItems =
            {
                (HotkeyAction.ToggleGod, "无敌"),
                (HotkeyAction.ToggleEsp, "ESP"),
                (HotkeyAction.ToggleAA, "AA"),
                (HotkeyAction.ToggleSpeed, "加速"),
                (HotkeyAction.ToggleFly, "飞行"),
                (HotkeyAction.ToggleNoclip, "穿墙"),
                (HotkeyAction.ToggleAntiGrief, "防整"),
                (HotkeyAction.ToggleBroadcast, "反整播报"),
                (HotkeyAction.ToggleAntiSpectate, "反观战"),
                (HotkeyAction.ToggleVacuumInstant, "直接吸满"),
                (HotkeyAction.ToggleAutoRecycle, "自动回收"),
                (HotkeyAction.Vacuum, "一键吸废料"),
                (HotkeyAction.Deliver, "一键配送"),
                (HotkeyAction.Win, "一键解放双手"),
                (HotkeyAction.ChatSpam, "刷屏"),
                (HotkeyAction.Dial, "一键拨号"),
                (HotkeyAction.AimKill, "瞄准·杀"),
                (HotkeyAction.AimRevive, "瞄准·复活/满血"),
                (HotkeyAction.AimBlood1, "瞄准·1血"),
                (HotkeyAction.AimShove, "瞄准·推飞"),
                (HotkeyAction.AimSteal, "瞄准·抢物"),
                (HotkeyAction.AimDrop, "瞄准·丢包"),
            };

            private static void EnsureFonts()
            {
                if (_cjk == null) _fontTried = false;   // 切场景字体被销毁则重建
                if (_fontTried) return;
                _fontTried = true;
                try
                {
                    _cjk = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体", "DengXian" }, 14);
                    if (_cjk != null)
                    {
                        _cjk.hideFlags = HideFlags.DontSave;
                        GUI.skin.font = _cjk;
                    }
                }
                catch { }
            }

            // 样式懒加载
            private static GUIStyle WinStyle()
            {
                if (_winStyle != null) return _winStyle;
                _winStyle = new GUIStyle(GUI.skin.window);
                _winStyle.normal.background = Solid(BgWin);
                _winStyle.normal.textColor = TextMain;
                _winStyle.padding = new RectOffset(0, 0, 0, 0);
                _winStyle.border = new RectOffset(8, 8, 8, 8);
                _winStyle.onNormal.background = _winStyle.normal.background;
                return _winStyle;
            }

            private static GUIStyle NavStyle()
            {
                if (_navStyle != null) return _navStyle;
                _navStyle = new GUIStyle(GUI.skin.button);
                _navStyle.normal.background = Solid(BgNav);
                _navStyle.normal.textColor = TextDim;
                _navStyle.hover.background = Solid(BtnBgHover);
                _navStyle.hover.textColor = TextMain;
                _navStyle.alignment = TextAnchor.MiddleLeft;
                _navStyle.padding = new RectOffset(10, 4, 0, 0);
                _navStyle.fontSize = 13;
                _navStyle.border = new RectOffset(2, 2, 2, 2);
                return _navStyle;
            }

            private static GUIStyle NavSelStyle()
            {
                if (_navSel != null) return _navSel;
                _navSel = new GUIStyle(NavStyle());
                _navSel.normal.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.20f));
                _navSel.normal.textColor = AccentHi;
                _navSel.hover.background = _navSel.normal.background;
                _navSel.hover.textColor = AccentHi;
                _navSel.fontStyle = FontStyle.Bold;
                return _navSel;
            }

            private static GUIStyle GroupTitleStyle()
            {
                if (_groupTitle != null) return _groupTitle;
                _groupTitle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _groupTitle.normal.textColor = AccentHi;
                return _groupTitle;
            }

            private static GUIStyle StatusStyle()
            {
                if (_status != null) return _status;
                _status = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };
                _status.normal.textColor = TextDim;
                return _status;
            }

            private static GUIStyle HintStyle()
            {
                if (_hint != null) return _hint;
                _hint = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleRight
                };
                _hint.normal.textColor = TextDim;
                return _hint;
            }

            private static GUIStyle RedBtn()
            {
                if (_redBtn != null) return _redBtn;
                _redBtn = new GUIStyle(GUI.skin.button)
                {
                    richText = true,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _redBtn.normal.background = Solid(new Color(0.62f, 0.16f, 0.20f, 0.9f));
                _redBtn.normal.textColor = Color.white;
                _redBtn.hover.background = Solid(new Color(0.72f, 0.22f, 0.26f));
                _redBtn.hover.textColor = Color.white;
                return _redBtn;
            }

            private static GUIStyle BtnStyle()
            {
                if (_btn != null) return _btn;
                _btn = new GUIStyle(GUI.skin.button);
                _btn.normal.background = Solid(BtnBg);
                _btn.normal.textColor = TextMain;
                _btn.hover.background = Solid(BtnBgHover);
                _btn.hover.textColor = Color.white;
                _btn.active.background = Solid(BtnBgPress);
                _btn.active.textColor = Color.white;
                _btn.fontSize = 12;
                return _btn;
            }

            private static GUIStyle AccentBtn()
            {
                if (_btnAccent != null) return _btnAccent;
                _btnAccent = new GUIStyle(BtnStyle());
                _btnAccent.normal.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.22f));
                _btnAccent.normal.textColor = AccentHi;
                _btnAccent.hover.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.35f));
                _btnAccent.hover.textColor = AccentHi;
                _btnAccent.fontStyle = FontStyle.Bold;
                return _btnAccent;
            }

            private static GUIStyle ToggleStyle()
            {
                if (_toggleStyle != null) return _toggleStyle;
                _toggleStyle = new GUIStyle(GUI.skin.toggle)
                {
                    richText = true,
                    fontSize = 13
                };
                _toggleStyle.normal.textColor = new Color(0.82f, 0.85f, 0.9f);
                _toggleStyle.onNormal.textColor = new Color(0.82f, 0.85f, 0.9f);
                _toggleStyle.hover.textColor = Color.white;
                _toggleStyle.onHover.textColor = Color.white;
                return _toggleStyle;
            }

            private static Texture2D Solid(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                t.hideFlags = HideFlags.DontSave;   // 切场景不销毁，避免进大厅后背景贴图失效变透明
                return t;
            }

            private static Texture2D _lineTex;

            /// <summary>右下角缩放提示（三条斜线）。</summary>
            private static Texture2D GripTex()
            {
                if (_gripTex != null) return _gripTex;
                const int s = 20;
                _gripTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                Color line = new Color(1f, 1f, 1f, 0.5f);
                Color empty = new Color(0f, 0f, 0f, 0f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        bool on = false;
                        for (int k = 0; k < 3; k++)
                        {
                            int o = 2 + k * 2;
                            if (y >= 9 && y <= 14 && x == y + o) { on = true; break; }
                        }
                        _gripTex.SetPixel(x, y, on ? line : empty);
                    }
                _gripTex.Apply();
                _gripTex.wrapMode = TextureWrapMode.Clamp;
                _gripTex.hideFlags = HideFlags.DontSave;
                return _gripTex;
            }

            /// <summary>分组标题：主题色文字 + 分隔线。</summary>
            private static void GroupTitle(string text)
            {
                GUILayout.Space(4);
                GUILayout.Label(text, GroupTitleStyle());
                if (_lineTex == null) { _lineTex = Solid(Color.white); }
                var r = GUILayoutUtility.GetRect(10f, 1f);
                var old = GUI.color;
                GUI.color = Border;
                GUI.DrawTexture(r, _lineTex);
                GUI.color = old;
                GUILayout.Space(2);
            }

            /// <summary>开关行：标签 + 开关，右边显示热键。</summary>
            private static void ToggleRow(string label, ref bool val, Hotkey hk)
            {
                GUILayout.BeginHorizontal();
                string text = val
                    ? "<color=#00b3e6>●</color> " + label
                    : "<color=#5a5f6b>○</color> " + label;
                bool nv = GUILayout.Toggle(val, text, ToggleStyle());
                if (nv != val) val = nv;
                if (hk.IsBound) GUILayout.Label("<color=#666b78>[" + hk.Label() + "]</color>", HintStyle(), GUILayout.Width(84));
                GUILayout.EndHorizontal();
            }

            /// <summary>按钮行：一行两个小按钮。</summary>
            private static void BtnRow(string a, System.Action actA, string b, System.Action actB)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(a, BtnStyle(), GUILayout.Height(24))) actA();
                if (!string.IsNullOrEmpty(b) && GUILayout.Button(b, BtnStyle(), GUILayout.Height(24))) actB();
                GUILayout.EndHorizontal();
            }

            // RGB 流动灯边框：绕窗口一圈分段着色，颜色沿周长随时间流动
            private static void DrawRGBBorder(Rect r, float thick)
            {
                if (_lineTex == null) { _lineTex = Solid(Color.white); }
                float flow = Time.realtimeSinceStartup * 0.4f;   // 流动速度
                const int n = 72;                                  // 分段数
                float perim = 2f * (r.width + r.height);
                float seg = perim / n;
                var old = GUI.color;
                for (int k = 0; k < n; k++)
                {
                    float s0 = k * seg;
                    Vector2 a = PerimeterPoint(r, s0);
                    Vector2 b = PerimeterPoint(r, Mathf.Min(s0 + seg * 0.6f, perim));
                    GUI.color = Color.HSVToRGB(Mathf.Repeat(flow + (float)k / n, 1f), 0.9f, 1f);
                    GUI.DrawTexture(EdgeRect(a, b, thick), _lineTex);
                }
                GUI.color = old;
            }

            // 沿矩形周长(上→右→下→左)取点
            private static Vector2 PerimeterPoint(Rect r, float s)
            {
                float w = r.width, h = r.height;
                if (s < w) return new Vector2(r.x + s, r.y);
                if (s < w + h) return new Vector2(r.xMax, r.y + s - w);
                if (s < 2f * w + h) return new Vector2(r.xMax - (s - w - h), r.yMax);
                return new Vector2(r.x, r.yMax - (s - 2f * w - h));
            }

            // 横/竖线段矩形
            private static Rect EdgeRect(Vector2 a, Vector2 b, float thick)
            {
                if (Mathf.Abs(a.y - b.y) < 0.5f)
                {
                    float x = Mathf.Min(a.x, b.x);
                    return new Rect(x, a.y - thick * 0.5f, Mathf.Max(0.01f, Mathf.Abs(a.x - b.x)), thick);
                }
                float y = Mathf.Min(a.y, b.y);
                return new Rect(a.x - thick * 0.5f, y, thick, Mathf.Max(0.01f, Mathf.Abs(a.y - b.y)));
            }

            public static void Draw()
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                EnsureFonts();

                var e = Event.current;
                Rect gripHit = new Rect(_win.x + _win.width - 24f, _win.y + _win.height - 24f, 24f, 24f);
                if (e.type == EventType.MouseDown && gripHit.Contains(e.mousePosition)) { _resizing = true; e.Use(); }
                if (_resizing && e.type == EventType.MouseDrag)
                {
                    _win.width = Mathf.Max(MinWinW, e.mousePosition.x - _win.x + 8f);
                    _win.height = Mathf.Max(MinWinH, e.mousePosition.y - _win.y + 8f);
                    e.Use();
                }
                if (_resizing && (e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow)) { _resizing = false; e.Use(); }

                _win = GUI.Window(991001, _win, DoWindow, "", WinStyle());
                DrawRGBBorder(_win, 3f);   // RGB 流动灯边框
            }

            private static int _tab;   // 0自动 1操控 2玩家 3视觉 4语音 5热键
            private static readonly string[] TabNames = { "自动", "操控", "玩家", "视觉", "语音", "热键" };

            private static void DoWindow(int id)
            {
                GUILayout.BeginVertical();

                // 标题栏
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b><color=#00b3e6>Delivery & Beyond</color></b>  Cheat", TitleStyle());
                GUILayout.FlexibleSpace();
                bool inRound = Features.InRound();
                GUILayout.Label(inRound ? "<color=#00ff88>● 对局中</color>" : "<color=#ff5555>● 未在对局</color>", StatusStyle());
                GUILayout.Label(Features.IsHost ? "<color=#00b3e6>房主</color>" : "客户端", StatusStyle());
                GUILayout.EndHorizontal();

                // 左标签栏 + 内容区
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(86));
                for (int i = 0; i < TabNames.Length; i++)
                {
                    if (GUILayout.Button(TabNames[i], _tab == i ? NavSelStyle() : NavStyle(), GUILayout.Height(30)))
                        _tab = i;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("关", NavStyle(), GUILayout.Height(24))) { MenuOpen = false; }
                GUILayout.EndVertical();

                GUILayout.BeginVertical();
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(_win.width - 118f), GUILayout.Height(_win.height - 96f));
                switch (_tab)
                {
                    case 0: DrawAutoTab(); break;
                    case 1: DrawKillTab(); break;
                    case 2: DrawPlayerTab(); break;
                    case 3: DrawVisualTab(); break;
                    case 4: DrawVoiceTab(); break;
                    default: DrawHotkeyTab(); break;
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                // 状态提示
                if (!string.IsNullOrEmpty(LastMsg) && Time.time - _msgTime < 8f)
                {
                    GUILayout.Space(3);
                    GUILayout.Label("状态: " + LastMsg, StatusStyle());
                }
                else if (!string.IsNullOrEmpty(LastMsg)) LastMsg = "";

                GUILayout.Space(2);
                if (GUILayout.Button("隐藏面板 (INS)", BtnStyle(), GUILayout.Height(22))) MenuOpen = false;

                GUILayout.EndVertical();
                GUI.DrawTexture(new Rect(_win.width - 18f, _win.height - 18f, 18f, 18f), GripTex());
                GUI.DragWindow(new Rect(0f, 0f, _win.width, 30f));
            }

            private static GUIStyle TitleStyle()
            {
                if (_title != null) return _title;
                _title = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _title.normal.textColor = new Color(0.85f, 0.87f, 0.92f);
                return _title;
            }
            private static GUIStyle _title;

            // ---------- 自动 ----------
            private static void DrawAutoTab()
            {
                GroupTitle("— 跑房杀 —");
                string hpLabel = ServerHopper.Enabled
                    ? "<color=#ff5555>跑房杀 (进行中)</color>"
                    : "<color=#ff3333>跑房杀</color>";
                if (GUILayout.Button(hpLabel, RedBtn(), GUILayout.Height(32)))
                {
                    if (ServerHopper.Enabled) ServerHopper.Stop();
                    else ServerHopper.Start();
                }
                GUILayout.Label("状态: " + ServerHopper.Status, StatusStyle());

                GroupTitle("— 一键系列 —");
                string winLabel = AutoWin.IsActive
                    ? "<color=#ff5555>一键解放双手 (进行中...) [点击停止]</color>"
                    : "<color=#ff3333>一键解放双手</color>";
                if (GUILayout.Button(winLabel, RedBtn(), GUILayout.Height(32)))
                {
                    if (AutoWin.IsActive) AutoWin.Stop();
                    else AutoWinRequested = true;
                }
                BtnRow("一键吸废料", () =>
                {
                    if (VacuumAll.IsActive) VacuumAll.Stop();
                    else VacuumAll.Start();
                }, "一键配送", () =>
                {
                    if (AutoOrder.IsActive) { AutoOrder.Stop(); Notify("已停止一键配送"); }
                    else AutoOrderRequested = true;
                });
                BtnRow("一键拨号", () => AutoDial.TryDial(), null, null);

                GroupTitle("— 吸废料 —");
                ToggleRow("直接吸满 (房主瞬间/客户端~1s)", ref VacuumInstant, HkVacuumInstant);
                ToggleRow("满袋自动回收船上[房主]", ref AutoRecycle, HkAutoRecycle);

                GroupTitle("— 公屏 —");
                ChatSpam.Message = GUILayout.TextField(ChatSpam.Message);
                GUILayout.BeginHorizontal();
                ChatSpam.Interval = Mathf.Clamp(GUILayout.HorizontalSlider(ChatSpam.Interval, 0.05f, 2f), 0.05f, 2f);
                GUILayout.Label("间隔 " + ChatSpam.Interval.ToString("0.00") + "s", StatusStyle(), GUILayout.Width(96));
                GUILayout.EndHorizontal();
                BtnRow("发送一次", () => ChatSpam.Send(ChatSpam.Message), ChatSpam.Active ? "停止刷屏" : "开始刷屏", () => ChatSpam.Active = !ChatSpam.Active);
            }

            // ---------- 操控 ----------
            private static void DrawKillTab()
            {
                GroupTitle("— 瞄准快捷键 (镜头对准生效) —");
                GUILayout.BeginHorizontal();
                GUILayout.Label("杀 " + (HkAimKill.IsBound ? "[" + HkAimKill.Label() + "]" : "未绑"), StatusStyle());
                GUILayout.Label("复活/满血 " + (HkAimRevive.IsBound ? "[" + HkAimRevive.Label() + "]" : "未绑"), StatusStyle());
                GUILayout.EndHorizontal();
                if (GUILayout.Button("去热键页绑定 →", AccentBtn(), GUILayout.Height(24))) _tab = 4;
                var aimed = Aim.GetAimedPlayer();
                GUILayout.Label(aimed != null ? "当前准星: " + aimed.GetPlayerName() + " 血" + aimed.GetHealth() : "当前准星: 无人", StatusStyle());

                GroupTitle("— 全员操作 —");
                BtnRow("全员推飞", KillTarget.ShoveAllOnce, "全员血1", () => KillTarget.SetBloodAll(1));
                BtnRow("全员丢包", KillTarget.DropAllPlayers, "杀全部", KillTarget.KillAll);
                BtnRow("全员抢物到脚下", KillTarget.StealAll, "全员连推", () => KillTarget.ShoveLoopAll(true));
                BtnRow("一键满袋(房主)", KillTarget.RefillBag, "碎全场玻璃", KillTarget.BreakAllGlass);
                BtnRow("复活自身", KillTarget.ReviveSelf, "复活全员", KillTarget.ReviveAll);
                if (GUILayout.Button("自杀 (仅手动)", BtnStyle(), GUILayout.Height(24))) KillTarget.Suicide();

                GroupTitle("— 玩家列表 (" + KillTarget.Players.Count + " 人) —");
                foreach (var p in KillTarget.Players)
                {
                    if (p == null || !p.IsSpawned) continue;
                    string nm = p.GetPlayerName();
                    if (nm.Length > 6) nm = nm.Substring(0, 6) + "…";
                    string st = p.IsDead() ? "✝" : "●";
                    GUILayout.Label(string.Format("{0} {1}血{2}", nm, st, p.GetHealth()), StatusStyle());
                    GUILayout.BeginHorizontal();
                    bool lp = KillTarget.IsLooping(p);
                    lp = GUILayout.Toggle(lp, "循环杀", GUILayout.Width(66));
                    KillTarget.SetLoop(p, lp);
                    bool sl = KillTarget.IsShoveLooping(p);
                    sl = GUILayout.Toggle(sl, "循环推", GUILayout.Width(66));
                    KillTarget.SetShoveLoop(p, sl);
                    if (GUILayout.Button("杀", BtnStyle(), GUILayout.Width(40))) KillTarget.KillOnce(p);
                    if (GUILayout.Button("推", BtnStyle(), GUILayout.Width(40))) KillTarget.ShoveOnce(p);
                    if (GUILayout.Button("血1", BtnStyle(), GUILayout.Width(44))) KillTarget.SetBlood(p, 1);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    bool hl = KillTarget.IsHealLooping(p);
                    hl = GUILayout.Toggle(hl, "循环满血", GUILayout.Width(80));
                    KillTarget.SetHealLoop(p, hl);
                    if (p.IsDead())
                    {
                        if (GUILayout.Button("复活", BtnStyle(), GUILayout.Width(60))) KillTarget.RevivePlayer(p);
                    }
                    else
                    {
                        if (GUILayout.Button("满血", BtnStyle(), GUILayout.Width(60))) KillTarget.SetBlood(p, 100);
                    }
                    if (GUILayout.Button("抢", BtnStyle(), GUILayout.Width(40))) KillTarget.StealItems(p);
                    if (GUILayout.Button("丢包", BtnStyle(), GUILayout.Width(52))) KillTarget.DropAllItems(p);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(2);
                }
            }

            // ---------- 玩家 ----------
            private static void DrawPlayerTab()
            {
                GroupTitle("— 生存 —");
                ToggleRow("无敌", ref GodMode, HkGod);
                ToggleRow("防整·锁物反抢", ref AntiGrief, HkAntiGrief);
                ToggleRow("反整播报", ref AntiGriefBroadcast, HkBroadcast);

                GroupTitle("— 移动 —");
                ToggleRow("移动加速", ref SpeedBoost, HkSpeed);
                SpeedTarget = Mathf.Round(GUILayout.HorizontalSlider(SpeedTarget, 1f, 10f) * 10f) / 10f;
                GUILayout.Label("倍率 " + SpeedTarget.ToString("0.0") + "x (最高10x)", StatusStyle());
                ToggleRow("飞行 (WASD+空格/Ctrl)", ref Fly, HkFly);
                ToggleRow("穿墙 (自动带飞行控制)", ref Noclip, HkNoclip);

                GroupTitle("— 角度AA (伪造朝向) —");
                ToggleRow("启用AA", ref AAEnabled, HkAA);
                ToggleRow("AA转圈", ref AASpin, default);
                if (AASpin)
                {
                    GUILayout.BeginHorizontal();
                    AASpeed = Mathf.Round(GUILayout.HorizontalSlider(AASpeed, 60f, 36000f));
                    if (GUILayout.Button("最大", BtnStyle(), GUILayout.Width(40))) AASpeed = 36000f;
                    GUILayout.EndHorizontal();
                    GUILayout.Label("速度 " + AASpeed + "°/s (≈" + (AASpeed / 360f).ToString("0.#") + "圈/s)", StatusStyle());
                    AABow = Mathf.Round(GUILayout.HorizontalSlider(AABow, 0f, 90f));
                    GUILayout.Label("低头 " + AABow + "°", StatusStyle());
                }
                else
                {
                    AAOffset = Mathf.Round(GUILayout.HorizontalSlider(AAOffset, -180f, 180f));
                    GUILayout.Label("偏移 " + AAOffset + "°", StatusStyle());
                }
            }

            // ---------- 视觉 ----------
            private static void DrawVisualTab()
            {
                GroupTitle("— ESP —");
                ToggleRow("ESP透视 (3D方框+信息)", ref EspEnabled, HkEsp);

                GroupTitle("— 反观战 —");
                GUILayout.BeginHorizontal();
                string astxt = AntiSpectate
                    ? "<color=#00b3e6>●</color> 反观战"
                    : "<color=#5a5f6b>○</color> 反观战";
                bool asNew = GUILayout.Toggle(AntiSpectate, astxt, ToggleStyle());
                if (asNew != AntiSpectate) { AntiSpectate = asNew; AntiSpectateCtrl.OnToggle(); }
                if (HkAntiSpectate.IsBound) GUILayout.Label("<color=#666b78>[" + HkAntiSpectate.Label() + "]</color>", HintStyle(), GUILayout.Width(84));
                GUILayout.EndHorizontal();
                if (AntiSpectate)
                    GUILayout.Label(Features.IsHost
                        ? "<color=#88ff88>生效中: 对外假死，观战列表不可见。</color>"
                        : "<color=#88ff88>生效中: 位置钉地底，观战为黑屏。</color>", StatusStyle());
                else
                    GUILayout.Label(Features.IsHost
                        ? "<color=#888>房主: 开→对外假死，观战列表不可见。</color>"
                        : "<color=#888>客户端: 开→位置钉地底，观战为黑屏。</color>", StatusStyle());
            }

            // ---------- 语音 ----------
            private static void DrawVoiceTab()
            {
                GroupTitle("— 语音广播 (本地音频播给全房) —");
                GUILayout.Label("音频路径 (本地文件 或 http 地址)", StatusStyle());
                string np = GUILayout.TextField(VoiceBroadcast.AudioPath, GUILayout.Height(22));
                if (np != VoiceBroadcast.AudioPath) VoiceBroadcast.AudioPath = np;

                GUILayout.BeginHorizontal();
                string ptxt = VoiceBroadcast.Active
                    ? "<color=#ff5555>■ 停止广播</color>"
                    : "<color=#00b3e6>▶ 播放广播</color>";
                if (GUILayout.Button(ptxt, RedBtn(), GUILayout.Height(28))) VoiceBroadcast.Toggle();
                if (GUILayout.Button("重载", BtnStyle(), GUILayout.Height(28))) VoiceBroadcast.Reload();
                GUILayout.EndHorizontal();

                bool loop = GUILayout.Toggle(VoiceBroadcast.Loop, "循环播放", ToggleStyle());
                if (loop != VoiceBroadcast.Loop) VoiceBroadcast.Loop = loop;

                GUILayout.Label("本地监听音量: " + Mathf.RoundToInt(VoiceBroadcast.MonitorVolume * 100f) + "%", StatusStyle());
                VoiceBroadcast.MonitorVolume = Mathf.Clamp(GUILayout.HorizontalSlider(VoiceBroadcast.MonitorVolume, 0f, 1f), 0f, 1f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("静音", BtnStyle(), GUILayout.Height(22))) VoiceBroadcast.MonitorVolume = 0f;
                if (GUILayout.Button("满音", BtnStyle(), GUILayout.Height(22))) VoiceBroadcast.MonitorVolume = 1f;
                GUILayout.EndHorizontal();

                GUILayout.Label("状态: " + VoiceBroadcast.Status, StatusStyle());
                GUILayout.Label("<color=#888>支持 WAV/MP3/OGG；队友按各自音量正常听到；需进对局、语音通道在线</color>", StatusStyle());
            }

            // ---------- 热键 ----------
            private static void DrawHotkeyTab()
            {
                GroupTitle("— 快捷键 (改=按键，模式=切换/长按/单击) —");
                foreach (var it in HotkeyItems)
                {
                    var hk = GetHotkey(it.Action);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(it.Label, GUILayout.Width(112));
                    string cur = it.Action == Rebind ? "按新键..." : (hk.IsBound ? "[" + hk.Label() + "]" : "未绑定");
                    GUILayout.Label(cur, StatusStyle(), GUILayout.Width(88));
                    if (GUILayout.Button(hk.ModeName(), BtnStyle(), GUILayout.Width(42))) CycleMode(it.Action);
                    if (GUILayout.Button("改", BtnStyle(), GUILayout.Width(38))) Rebind = it.Action;
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(6);
                GUILayout.Label("拨号/拿取配送已并入「一键配送」", StatusStyle());
            }
        }
    }
}
