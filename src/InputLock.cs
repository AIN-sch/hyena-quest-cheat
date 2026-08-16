using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>
    /// 面板开关时的鼠标与输入管理。
    /// 鼠标交给游戏的光标请求栈（StartupController.RequestCursor/ReleaseCursor）：
    ///   面板开 → 请求光标（UI_CONTROL_BLOCK），游戏显示鼠标、停用 Gameplay 操作；
    ///   面板关 → 释放请求，游戏自行决定（对局中锁鼠标；暂停菜单开着则保持显示）。
    /// 不要直接改 Cursor.lockState 锁死鼠标，否则会和游戏的暂停菜单打架，导致菜单隐藏后鼠标消失。
    /// </summary>
    public static class InputLock
    {
        private const string CursorId = "HYENAQUEST_CHEAT";
        private static bool _requested;

        public static void Apply(bool menuOpen)
        {
            var start = MonoController<StartupController>.Instance;

            if (menuOpen)
            {
                // 首次打开：注册进光标请求栈（UI_CONTROL_BLOCK：显示鼠标 + 停用操作）
                if (!_requested)
                {
                    _requested = true;
                    if (start != null) start.RequestCursor(CursorId);
                }

                // 每帧兜底：防游戏把光标状态改回去 / 请求栈被清
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (start != null)
                {
                    var map = start.GetIngameActions();
                    if (map != null && map.enabled) map.Disable();
                }
            }
            else
            {
                // 关闭面板：释放请求，交还游戏决定
                if (_requested)
                {
                    _requested = false;
                    if (start != null) start.ReleaseCursor(CursorId);
                }
            }
        }
    }
}
