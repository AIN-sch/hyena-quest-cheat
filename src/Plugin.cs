// Hyena Quest Cheat · BepInEx 5 插件入口
// INS 切换面板显隐；Update 逐帧驱动各功能模块 + 全功能快捷键。
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using HyenaQuest;

namespace HyenaQuestCheat
{
    [BepInPlugin("hyena.quest.cheat", "Hyena Quest Cheat", "1.3.2")]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly Dictionary<Features.HotkeyAction, ConfigEntry<string>> HkCfgs = new();

        public static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            // 快捷键绑定到 cfg（首次默认，之后读配置）
            foreach (var it in Features.Menu.HotkeyItems)
            {
                var cfg = Config.Bind("Hotkeys", it.Label,
                    Features.GetHotkey(it.Action).Serialize(), it.Label + " 热键 (K:键 / M:鼠标按钮)");
                Features.SetHotkey(it.Action, Hotkey.Deserialize(cfg.Value));
                HkCfgs[it.Action] = cfg;
            }

            // 菜单里改键后写回 cfg，下次启动保持
            Features.PersistHotkeys = () =>
            {
                foreach (var kv in HkCfgs)
                    kv.Value.Value = Features.GetHotkey(kv.Key).Serialize();
            };

            // 跑房杀参数（编辑 cfg 文件 ServerHopper 节）
            ServerHopper.LobbyIds = Config.Bind("ServerHopper", "LobbyIds", "", "跑房杀 固定房间ID列表（逗号分隔）").Value;
            ServerHopper.ScanMode = Config.Bind("ServerHopper", "ScanMode", true, "跑房杀 true=自动扫描公开房 false=固定ID列表").Value;
            ServerHopper.ScanPrefix = Config.Bind("ServerHopper", "ScanPrefix", "", "跑房杀 只进名字含此前缀的房").Value;
            ServerHopper.JoinTimeout = Config.Bind("ServerHopper", "JoinTimeout", 10f, "跑房杀 进房超时(秒)").Value;
            ServerHopper.LogEnabled = Config.Bind("ServerHopper", "LogEnabled", true, "跑房杀 写日志开关").Value;

            // 语音广播参数（编辑 cfg 文件 Voice 节）
            VoiceBroadcast.AudioPath = Config.Bind("Voice", "AudioPath", "", "语音广播 音频文件路径").Value;
            VoiceBroadcast.Loop = Config.Bind("Voice", "Loop", false, "语音广播 循环播放").Value;
            VoiceBroadcast.MonitorVolume = Config.Bind("Voice", "MonitorVolume", 0.15f, "语音广播 本地监听音量(0~1)").Value;

            Patches.Apply(new HarmonyLib.Harmony("hyena.quest.cheat"));

            Logger.LogInfo("Hyena Quest Cheat v1.3.2 loaded. INS=面板开关. 全功能热键可在菜单「热键」页改");
        }

        private void Update()
        {
            // INS 切换面板
            if (Keyboard.current != null && Keyboard.current[Key.Insert].wasPressedThisFrame)
            {
                Features.MenuOpen = !Features.MenuOpen;
                Features.OnMenuToggle();
            }

            // 改键状态下捕获下一键
            Features.CaptureHotkey();

            // 全功能快捷键
            Features.ProcessHotkeys();

            // 菜单按钮请求在 Update 里消费，避免在 OnGUI 内直接触发 RPC
            if (Features.AutoOrderRequested) { Features.AutoOrderRequested = false; AutoOrder.Start(); }
            if (Features.AutoWinRequested) { Features.AutoWinRequested = false; AutoWin.Start(); }

            // 面板开关时锁/放输入
            InputLock.Apply(Features.MenuOpen);

            Features.Tick();        // 移动倍率渐变
            AntiSpectateCtrl.Tick();// 反观战：周期性把 health 钉回0
            VacuumAll.Update();
            AutoDial.Update();
            AutoDeliver.Update();
            AutoOrder.Update();
            AutoWin.Update();
            KillTarget.Update();    // 秒杀/操控：刷新列表 + 循环杀 + 补刀
            ServerHopper.Update();  // 跑房杀：自动进房→杀全部→退出→下一个
            ChatSpam.Update();      // 公屏刷屏
            AntiGrief.Update();     // 防整·锁物反抢
            VoiceBroadcast.Update();// 语音广播：对局外自动停 + 监听音量跟随
        }

        private void OnGUI()
        {
            // 残留半透明 GUI.color 会导致菜单背景透明 → 强制不透明画完再还原
            var gColor = GUI.color;
            var gBg = GUI.backgroundColor;
            var gContent = GUI.contentColor;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
            try
            {
                Esp.Draw();             // ESP透视（菜单关着也画）
                if (!Features.MenuOpen) return;
                Features.Menu.Draw();
            }
            finally
            {
                GUI.color = gColor;
                GUI.backgroundColor = gBg;
                GUI.contentColor = gContent;
            }
        }
    }
}
