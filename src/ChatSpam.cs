using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>公屏刷屏：反射调 ChatServerRPC，任意客户端可发、服务端广播。</summary>
    public static class ChatSpam
    {
        public static string Message = "大家好";
        public static float Interval = 0.1f;
        public static bool Active;

        private static float _next;

        private static readonly MethodInfo ChatServerRPC =
            typeof(ChatController).GetMethod("ChatServerRPC", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Stop() => Active = false;

        /// <summary>发一条到公屏。</summary>
        public static void Send(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var chat = NetController<ChatController>.Instance;
            if (chat == null || ChatServerRPC == null) { Features.Notify("当前不在可聊天的会话里"); return; }
            try
            {
                RpcParams rp = chat.RpcTarget.Server;   // BaseRpcTarget → RpcParams 隐式转换
                ChatServerRPC.Invoke(chat, new object[] { text, rp });
            }
            catch (System.Exception e)
            {
                Features.Notify("刷屏失败: " + e.Message);
            }
        }

        /// <summary>每帧调用：刷屏开关开启时按间隔连发。</summary>
        public static void Update()
        {
            if (!Active) return;
            if (Time.time < _next) return;
            _next = Time.time + Interval;
            Send(Message);
        }
    }
}
