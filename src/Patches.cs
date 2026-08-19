using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode.Components;
using ECM2;
using HyenaQuest;
using MetaVoiceChat;

namespace HyenaQuestCheat
{
    /// <summary>
    /// AA（假视角）数学辅助：给本地视角补偿和移动方向提供真实朝向。
    /// 实现细节不展开；当前版本存在已知缺陷，暂不修复。
    /// </summary>
    public static class AntiAim
    {
        public static float RealYaw;

        public static Vector3 GetForward()
        {
            float yaw = Features.AAEnabled ? RealYaw : (SDK.MainCamera ? SDK.MainCamera.transform.eulerAngles.y : 0f);
            float y = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y));
        }

        public static Vector3 GetRight()
        {
            float yaw = Features.AAEnabled ? RealYaw : (SDK.MainCamera ? SDK.MainCamera.transform.eulerAngles.y : 0f);
            float y = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
        }
    }

    public static class Patches
    {
        public static void Apply(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches).Assembly);
        }

        // ---------------- 无敌：血量永远 >= 1 ----------------
        [HarmonyPatch(typeof(entity_player), "SetHealth")]
        public static class Patch_GodMode
        {
            static void Prefix(ref byte newValue)
            {
                if (Features.GodMode && newValue == 0) newValue = 1;
            }
        }

        // ---------------- 移动加速：倍率作用于 GetMaxSpeed ----------------
        // GetMaxSpeed 为 virtual，覆盖全部移动模式。
        [HarmonyPatch(typeof(Character), "GetMaxSpeed")]
        public static class Patch_Speed
        {
            static void Postfix(ref float __result)
            {
                if (Features.SpeedMult > 1f) __result *= Features.SpeedMult;
            }
        }

        // ---------------- AA：禁止 ECM2 自动转向覆盖假yaw ----------------
        [HarmonyPatch(typeof(Character), "UpdateRotation")]
        public static class Patch_NoAutoRotate
        {
            static bool Prefix()
            {
                return !Features.AAEnabled;
            }
        }

        // ---------------- AA：伪造复制给其他玩家的根节点旋转 + 补偿本地相机 ----------------
        [HarmonyPatch(typeof(entity_player_camera), "UpdateView")]
        public static class Patch_UpdateView
        {
            private static readonly FieldInfo _yaw =
                AccessTools.Field(typeof(entity_player_camera), "_yawInput");
            private static readonly FieldInfo _pitch =
                AccessTools.Field(typeof(entity_player_camera), "_pitchInput");
            private static readonly FieldInfo _mov =
                AccessTools.Field(typeof(entity_player_camera), "_characterMovement");
            private static readonly FieldInfo _veh =
                AccessTools.Field(typeof(entity_player_camera), "_vehicle");
            private static readonly MethodInfo _forced =
                AccessTools.Method(typeof(entity_player_camera), "IsBeingForced");

            private static float _spinAccum;   // 转圈累积角（°），避免 Time.time*速度 大数浮点跳阶

            static void Postfix(entity_player_camera __instance)
            {
                if (!Features.AAEnabled) return;

                var local = PlayerController.LOCAL;
                if (!local || local.IsDead()) return;
                if (_forced != null && (bool)_forced.Invoke(__instance, null)) return;   // 过场/强制视角不动
                if (_veh != null && _veh.GetValue(__instance) != null) return;           // 开车不AA

                var yaw = (float)_yaw.GetValue(__instance);
                var pitch = (float)_pitch.GetValue(__instance);
                AntiAim.RealYaw = yaw;

                // 假yaw：转圈模式用累积器
                float fakeYaw;
                if (Features.AASpin)
                {
                    _spinAccum = (_spinAccum + Features.AASpeed * Time.deltaTime) % 360f;
                    fakeYaw = _spinAccum;
                }
                else
                {
                    fakeYaw = Mathf.Repeat(yaw + Features.AAOffset, 360f);
                }

                var mov = (entity_player_movement)_mov.GetValue(__instance);
                if (mov == null) return;

                // 根节点 = 先低头再转假yaw，同步 transform（否则滞后一帧）
                float tilt = Features.AABow;
                var rootRot = Quaternion.Euler(0f, fakeYaw, 0f) * Quaternion.Euler(-tilt, 0f, 0f);
                mov.SetRotation(rootRot);
                mov.transform.rotation = rootRot;

                // 本地相机补偿：世界朝向保持真实朝向，不受假yaw/低头影响
                var view = local.view;
                if (view != null)
                {
                    view.localRotation = Quaternion.Euler(tilt, 0f, 0f)
                                       * Quaternion.Euler(0f, yaw - fakeYaw, 0f)
                                       * Quaternion.Euler(-pitch, 0f, 0f);
                }

                // 相机位置：眼睛偏移取回本地常量后按根姿态放回，转圈/低头时原地稳定
                var head = local.head;
                if (head != null && view != null)
                {
                    Vector3 rootPos = mov.transform.position;
                    Vector3 eye = head.position + head.forward * 0.025f + head.up * 0.08f;   // 游戏原样的眼位
                    Vector3 localEye = Quaternion.Inverse(rootRot) * (eye - rootPos);
                    Vector3 stableEye = rootPos + localEye;
                    float t = Mathf.Max(18f, Vector3.Distance(view.position, stableEye) * 6f) * Time.deltaTime;
                    view.position = Vector3.Lerp(view.position, stableEye, t);
                }
            }
        }

        // ---------------- AA：移动方向跟随真实视角 ----------------
        // HandleMove 用假yaw方向会带偏 WASD；替换为真实朝向的 GetForward/GetRight。
        [HarmonyPatch(typeof(entity_player_movement), "HandleMove")]
        public static class Patch_HandleMove
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);
                var helperFwd = AccessTools.Method(typeof(AntiAim), "GetForward");
                var helperRight = AccessTools.Method(typeof(AntiAim), "GetRight");

                // 匹配 ldarg.0 + get_transform + get_forward/get_right
                for (int i = 0; i < list.Count - 2; i++)
                {
                    if (list[i].opcode == OpCodes.Ldarg_0
                        && IsNamedCall(list[i + 1], "get_transform")
                        && IsNamedCall(list[i + 2], out bool isRight, "get_forward", "get_right"))
                    {
                        list[i] = new CodeInstruction(OpCodes.Call, isRight ? helperRight : helperFwd);
                        list.RemoveRange(i + 1, 2);
                    }
                }
                return list;
            }

            private static bool IsNamedCall(CodeInstruction c, string name)
            {
                return (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt)
                       && (c.operand as MethodBase)?.Name == name;
            }

            private static bool IsNamedCall(CodeInstruction c, out bool isRight, string fwdName, string rightName)
            {
                isRight = false;
                if (c.opcode != OpCodes.Call && c.opcode != OpCodes.Callvirt) return false;
                var m = c.operand as MethodBase;
                if (m == null) return false;
                if (m.Name == fwdName) return true;
                if (m.Name == rightName) { isRight = true; return true; }
                return false;
            }
        }

        // ---------------- 飞行 / 穿墙 ----------------
        // 飞行走 ECM2 Flying 模式；穿墙关碰撞并加飞行控制防沉底；HandleMove 由 Prefix 接管。

        private static bool IsLocalMovement(entity_player_movement mov)
        {
            if (mov == null) return false;
            var local = PlayerController.LOCAL;
            return local != null && local.GetComponent<entity_player_movement>() == mov;
        }

        [HarmonyPatch(typeof(entity_player_movement), "HandleMove")]
        public static class Patch_FlyNoclip
        {
            private static bool _prevActive;

            static bool Prefix(entity_player_movement __instance)
            {
                bool active = Features.Fly || Features.Noclip;
                if (!IsLocalMovement(__instance)) return true;   // 非本地角色不动

                if (!active)
                {
                    // 关闭后复位碰撞与移动模式（重力由游戏自行设置）
                    if (_prevActive)
                    {
                        __instance.characterMovement.detectCollisions = true;
                        if (__instance.IsFlying()) __instance.SetMovementMode(Character.MovementMode.Walking, 0);
                        _prevActive = false;
                    }
                    return true;   // 交给游戏原逻辑
                }
                _prevActive = true;

                var local = PlayerController.LOCAL;
                if (local == null || local.IsDead() || !Features.InRound())
                {
                    // 死亡/不在对局：恢复碰撞，禁用飞行
                    __instance.characterMovement.detectCollisions = true;
                    if (__instance.IsFlying()) __instance.SetMovementMode(Character.MovementMode.Walking, 0);
                    return false;
                }

                // 穿墙：关碰撞；纯飞行：碰撞照常
                __instance.characterMovement.detectCollisions = !Features.Noclip;

                // 零重力 + 飞行模式
                __instance.gravity = Vector3.zero;
                if (!__instance.IsFlying()) __instance.SetMovementMode(Character.MovementMode.Flying, 0);

                // 移动方向：优先游戏移动输入，读不到退回键盘；空格上/ctrl下
                Vector3 dir = Vector3.zero;
                if (!Features.MenuOpen)   // 菜单开启时不操控（避免打字框按键误操作）
                {
                    var kbd = UnityEngine.InputSystem.Keyboard.current;
                    Vector2 input = Vector2.zero;
                    if (__instance.moveAction != null && __instance.moveAction.action != null)
                    {
                        try { input = __instance.moveAction.action.ReadValue<Vector2>(); }
                        catch { input = Vector2.zero; }
                    }
                    if (kbd != null && input.sqrMagnitude < 0.01f)
                    {
                        if (kbd[UnityEngine.InputSystem.Key.W].isPressed) input.y += 1f;
                        if (kbd[UnityEngine.InputSystem.Key.S].isPressed) input.y -= 1f;
                        if (kbd[UnityEngine.InputSystem.Key.D].isPressed) input.x += 1f;
                        if (kbd[UnityEngine.InputSystem.Key.A].isPressed) input.x -= 1f;
                    }
                    // 移动基准用真实视角方向（GetForward/GetRight），与 AA 补丁一致
                    Vector3 fwd = AntiAim.GetForward();
                    Vector3 right = AntiAim.GetRight();
                    dir = fwd * input.y + right * input.x;
                    if (kbd != null)
                    {
                        if (kbd[UnityEngine.InputSystem.Key.Space].isPressed) dir += Vector3.up;
                        if (kbd[UnityEngine.InputSystem.Key.LeftCtrl].isPressed
                            || kbd[UnityEngine.InputSystem.Key.C].isPressed) dir -= Vector3.up;
                    }
                }
                if (dir.sqrMagnitude > 0.001f) dir.Normalize();
                __instance.SetMovementDirection(dir);
                return false;   // 跳过游戏原 HandleMove
            }
        }

        // 飞行速度：Flying 模式用 maxFlySpeed（默认0），设下限
        [HarmonyPatch(typeof(entity_player_movement), "GetMaxSpeed")]
        public static class Patch_FlySpeed
        {
            static void Postfix(entity_player_movement __instance, ref float __result)
            {
                if (!(Features.Fly || Features.Noclip)) return;
                if (!IsLocalMovement(__instance)) return;
                __result = Mathf.Max(__result, Features.FlySpeed * Features.SpeedMult);
            }
        }

        // 飞行加速度上限提高，响应及时
        [HarmonyPatch(typeof(Character), "GetMaxAcceleration")]
        public static class Patch_FlyAccel
        {
            static void Postfix(Character __instance, ref float __result)
            {
                if (!(Features.Fly || Features.Noclip)) return;
                if (!IsLocalMovement(__instance as entity_player_movement)) return;
                __result = Mathf.Max(__result, 40f);
            }
        }

        // ---------------- 反观战(房主)：本地视为存活（对外 health 已为0） ----------------
        // 仅房主生效：房客真死了不能被跳掉死亡流程（会卡住不弹死亡界面）。
        [HarmonyPatch(typeof(entity_player), "IsDead")]
        public static class Patch_AntiSpec_IsDead
        {
            static bool Prefix(entity_player __instance, ref bool __result)
            {
                if (!Features.AntiSpectate || !Features.IsHost) return true;
                if (!ReferenceEquals(__instance, PlayerController.LOCAL)) return true;
                __result = false;   // 本地永远活着，照常玩
                return false;
            }
        }

        // ---------------- 反观战(房主)：跳过本地死亡处理（不冻结/不传天空/不丢包） ----------------
        [HarmonyPatch(typeof(entity_player), "HealthStatusUpdate")]
        public static class Patch_AntiSpec_Death
        {
            static bool Prefix(entity_player __instance)
            {
                if (!Features.AntiSpectate || !Features.IsHost) return true;
                if (!ReferenceEquals(__instance, PlayerController.LOCAL)) return true;
                return false;
            }
        }

        // ---------------- 反观战(房客)：复制位置钉地底 → 观战相机锚进地里=黑屏 ----------------
        // CheckForStateChange 是同步包读位置的唯一入口，塞假位置读完后立即还原本地真实位置。
        [HarmonyPatch(typeof(NetworkTransform), "CheckForStateChange")]
        public static class Patch_AntiSpec_Guest
        {
            private static Transform _staged;   // 本帧被塞假位置的变换
            private static Vector3 _realPos;    // 真实位置（塞完立即还原）

            static bool Prefix(NetworkTransform __instance)
            {
                if (!Features.AntiSpectate || Features.IsHost) return true;
                var local = PlayerController.LOCAL;
                if (local == null) return true;
                // 只伪造本地 owner 权威的 NetworkTransform
                if (!__instance.CanCommitToTransform || __instance.transform != local.transform) return true;
                _staged = __instance.transform;
                _realPos = _staged.position;
                _staged.position = _realPos + new Vector3(0f, -1.2f, 0f);
                return true;
            }

            static void Postfix(NetworkTransform __instance)
            {
                if (_staged != null) { _staged.position = _realPos; _staged = null; }
            }
        }

        // ---------------- 跑房杀：进房失败原始原因（本地化前） ----------------
        // OnConnectionEnd 本地化前截取原始 key（server-full/banned/version）。
        [HarmonyPatch(typeof(NETController), "OnConnectionEnd")]
        public static class Patch_JoinErr
        {
            public static string Raw;

            static void Prefix(string errorMessage)
            {
                Raw = errorMessage;
            }
        }

        // ---------------- 语音广播：覆盖麦克风采样再进编码器 ----------------
        // SendFrame 是本地采样进 Opus 前的最后一站，Prefix 改写 samples 数组即全房广播。
        [HarmonyPatch(typeof(MetaVc), "SendFrame")]
        public static class Patch_VoiceBroadcast
        {
            static void Prefix(MetaVc __instance, int index, float[] samples)
            {
                if (!VoiceBroadcast.Active) return;
                VoiceBroadcast.InjectSamples(samples);
                // 强制本地麦开启：按键说话/闭麦时不让发送被挡
                __instance.isInputMuted.Value = false;
                __instance.isDeafened.Value = false;
            }
        }
    }
}
