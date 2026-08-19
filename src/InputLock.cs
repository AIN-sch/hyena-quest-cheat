using UnityEngine;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>面板开关的鼠标与输入管理：开→请求光标并停用操作，关→释放并交还游戏。</summary>
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
