// Hyena Quest Cheat · BepInEx 5 插件入口
// INS 切换面板显隐；Update 逐帧驱动各功能模块。
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using HyenaQuest;

namespace HyenaQuestCheat
{
    [BepInPlugin("hyena.quest.cheat", "Hyena Quest Cheat", "1.2.6")]
    public class Plugin : BaseUnityPlugin
    {
        private static ConfigEntry<string> CfgDial;
        private static ConfigEntry<string> CfgDeliver;
        private static ConfigEntry<string> CfgVacuum;

        private void Awake()
        {
            CfgDial = Config.Bind("Hotkeys", "DialPhone", "K:F3", "一键打电话热键（K:键 / M:鼠标按钮）");
            CfgDeliver = Config.Bind("Hotkeys", "Deliver", "K:F4", "一键拿取配送热键");
            CfgVacuum = Config.Bind("Hotkeys", "VacuumToggle", "K:F5", "一键吸废料热键");

            // 读配置初始化热键
            Features.DialHotkey = Hotkey.Deserialize(CfgDial.Value);
            Features.DeliverHotkey = Hotkey.Deserialize(CfgDeliver.Value);
            Features.VacuumHotkey = Hotkey.Deserialize(CfgVacuum.Value);

            // 菜单里改键后写回 cfg，下次启动保持
            Features.PersistHotkeys = () =>
            {
                CfgDial.Value = Features.DialHotkey.Serialize();
                CfgDeliver.Value = Features.DeliverHotkey.Serialize();
                CfgVacuum.Value = Features.VacuumHotkey.Serialize();
            };

            Patches.Apply(new HarmonyLib.Harmony("hyena.quest.cheat"));

            Logger.LogInfo("Hyena Quest Cheat v1.2.6 loaded. INS=面板开关(默认开启). 热键可在菜单里改");
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

            // 菜单按钮请求在 Update 里消费，避免在 OnGUI 内直接触发 RPC
            if (Features.AutoOrderRequested) { Features.AutoOrderRequested = false; AutoOrder.Start(); }
            if (Features.AutoWinRequested) { Features.AutoWinRequested = false; AutoWin.Start(); }

            // 吸废料热键（拨号/配送已并入一键配送，无独立热键）
            if (Features.Rebind == Features.HotkeyAction.None)
            {
                if (Features.VacuumHotkey.WasPressedThisFrame())
                {
                    if (VacuumAll.IsActive) VacuumAll.Stop();
                    else VacuumAll.Start();
                }
            }

            // 面板开关时锁/放输入（走游戏光标请求栈，不自己锁死鼠标）
            InputLock.Apply(Features.MenuOpen);

            Features.Tick();        // 移动倍率渐变
            VacuumAll.Update();
            AutoDial.Update();
            AutoDeliver.Update();   // 等所有权到位后传送配送
            AutoOrder.Update();     // 一键配送状态机
            AutoWin.Update();       // 一键解放双手状态机
            KillTarget.Update();    // 秒杀/操控：刷新列表 + 循环杀 + 补刀
            ChatSpam.Update();      // 公屏刷屏
        }

        private void OnGUI()
        {
            if (!Features.MenuOpen) return;
            Features.Menu.Draw();
        }
    }
}
