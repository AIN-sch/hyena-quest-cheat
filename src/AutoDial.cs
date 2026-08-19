using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using HyenaQuest;

namespace HyenaQuestCheat
{
    /// <summary>一键拨号：反射调用 OnUseRPC 隔空按电话，自动挑可下单任务逐位拨号。</summary>
    public static class AutoDial
    {
        private static List<string> _buttons;          // 待按的按钮字符队列
        private static float _nextPress;
        private static float _start;

        // 空号(INVALID_NUMBER)自动重拨：共享电话被其他玩家输入/占线会混成空号
        private static bool _watchInvalid;              // 正在监测上次拨号是否空号
        private static float _callSentAt;               // 按下 CALL 的时刻
        private static float _redialAt;                 // 空号回空闲后的自动重拨时刻
        private static int _redials;                    // 已自动重拨次数

        private static readonly MethodInfo OnUseRPC =
            typeof(entity_player).GetMethod("OnUseRPC", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo PhoneIndexField =
            typeof(PhoneController).GetField("PHONE_INDEX", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>是否正在拨号（按钮队列非空）。</summary>
        public static bool IsDialing => _buttons != null;

        /// <summary>手动/自动停止拨号。</summary>
        public static void Stop() => _buttons = null;

        public static void TryDial()
        {
            if (_buttons != null) { Features.Notify("正在拨号..."); return; }

            var pc = NetController<PhoneController>.Instance;
            var cc = NetController<ContractController>.Instance;
            var sc = NetController<ScrapController>.Instance;
            if (!pc || !cc || !sc) { Features.Notify("不在对局里"); return; }

            if (pc.Status() != PHONE_STATUS.IDLE) { Features.Notify("电话占线，稍后再试"); return; }

            var tasks = cc.GetAffordableTasks(sc.GetClaimedScrap(), false);
            Task? target = null;
            foreach (var t in tasks)
            {
                if (!t.HasDeliveryItem) { target = t; break; }
            }
            if (target == null) { Features.Notify("没有可拨号任务（废料不够或都在配送中）"); return; }

            var number = target.Value.Address.ToString();

            // 拨号前按 CLEAR 清空号码，避免混号/空号
            PressClear();

            _buttons = new List<string>();
            foreach (var ch in number) _buttons.Add(ch.ToString());
            _buttons.Add("CALL");

            _nextPress = 0f;
            _start = Time.time;
            _watchInvalid = false;
            _redialAt = 0f;
            Features.Notify("拨号 " + number + " ...");
        }

        public static void Update()
        {
            // 空队列立刻清成 null，否则 IsDialing 恒 true，解放双手会卡在等配送
            if (_buttons != null && _buttons.Count == 0) { _buttons = null; }

            // 没在拨号 → 监测上次拨号是否空号，必要时自动重拨
            if (_buttons == null)
            {
                WatchInvalid();
                return;
            }

            if (Time.time - _start > 40f) { _buttons = null; Features.Notify("拨号超时"); return; }
            if (Time.time < _nextPress) return;

            var pc = NetController<PhoneController>.Instance;
            var local = PlayerController.LOCAL;
            if (!pc || !local) { _buttons = null; return; }

            // 电话非空闲（响铃中/被其他玩家占用）就等
            if (pc.Status() != PHONE_STATUS.IDLE) { _nextPress = Time.time + 0.5f; return; }

            var ch = _buttons[0];
            var btn = GetButton(pc, ch);
            if (btn == null)
            {
                _buttons.RemoveAt(0);
                if (_buttons.Count == 0) _buttons = null;
                else _nextPress = Time.time + 0.1f;
                return;
            }

            // 电话按钮按一下锁约 2s（服务端锁），重复数字要等解锁
            if (btn.IsLocked()) { _nextPress = Time.time + 0.1f; return; }

            FirePress(local, btn);
            _buttons.RemoveAt(0);

            // 最后一个按钮按完 → 拨号结束（必须置 null）
            if (_buttons.Count == 0)
            {
                _buttons = null;
                _watchInvalid = true;          // 开始监测是否空号
                _callSentAt = Time.time;
                _redialAt = 0f;
                Features.Notify("拨号完成");
                return;
            }
            _nextPress = Time.time + 0.15f;
        }

        /// <summary>拨完后监测：打通则成功；空号则等回空闲自动重拨（最多3次）。</summary>
        private static void WatchInvalid()
        {
            if (!_watchInvalid && _redialAt <= 0f) return;
            if (Time.time - _callSentAt > 12f) { _watchInvalid = false; _redialAt = 0f; _redials = 0; return; }

            var pc = NetController<PhoneController>.Instance;
            if (pc == null) { _watchInvalid = false; _redialAt = 0f; return; }

            PHONE_STATUS st = pc.Status();
            if (st == PHONE_STATUS.TALKING || st == PHONE_STATUS.SPECIAL_MODE)
            {
                // 打通 → 成功，重置
                _watchInvalid = false;
                _redialAt = 0f;
                _redials = 0;
                return;
            }
            if (st == PHONE_STATUS.INVALID_NUMBER)
            {
                // 空号 → 等回空闲（约2秒）后自动重拨
                _watchInvalid = false;
                _redialAt = Time.time + 2.5f;
                return;
            }
            if (st == PHONE_STATUS.IDLE && _redialAt > 0f && Time.time >= _redialAt)
            {
                _redialAt = 0f;
                if (_redials < 3)
                {
                    _redials++;
                    Features.Notify("拨号空号，自动重拨 " + _redials + "/3");
                    TryDial();
                }
                else { _redials = 0; }
            }
        }

        /// <summary>按 CLEAR 清空电话号码（共享电话，防混号）。</summary>
        private static void PressClear()
        {
            var pc = NetController<PhoneController>.Instance;
            var local = PlayerController.LOCAL;
            if (!pc || !local) return;
            var btn = GetButton(pc, "CLEAR");
            if (btn == null) return;
            try
            {
                var nref = new NetworkBehaviourReference(btn);
                OnUseRPC.Invoke(local, new object[] { nref, true });
            }
            catch { /* 个别异常忽略 */ }
        }

        private static entity_button GetButton(PhoneController pc, string ch)
        {
            if (PhoneIndexField == null || pc.phoneButtons == null) return null;
            var dict = (Dictionary<int, string>)PhoneIndexField.GetValue(null);
            if (dict == null) return null;

            foreach (var kv in dict)
            {
                if (kv.Value == ch && kv.Key >= 0 && kv.Key < pc.phoneButtons.Count)
                {
                    var b = pc.phoneButtons[kv.Key];
                    if (b != null) return b;
                }
            }
            return null;
        }

        private static void FirePress(entity_player local, entity_button btn)
        {
            if (OnUseRPC == null) return;
            try
            {
                var nref = new NetworkBehaviourReference(btn);
                OnUseRPC.Invoke(local, new object[] { nref, true });
            }
            catch (Exception e)
            {
                Features.Notify("拨号失败: " + e.Message);
            }
        }
    }
}
