using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using ECM2;
using HyenaQuest;

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
        // GetMaxSpeed 是 virtual，走路/飞/游都走它 → 倍率对全部移动生效。
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

        // ---------------- AA：伪造复制给别人的根节点旋转 + 补偿本地相机 ----------------
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
        // HandleMove 的移动方向用假yaw的 transform.forward/right 会导致 WASD 跟着 AA 乱转，
        // 替换成按真实朝向计算的 GetForward/GetRight。
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
        // 飞行：切到 ECM2 原生 Flying 模式（不吃重力），每帧给 3D 移动方向，WASD 水平 + 空格上/ctrl 下。
        //        碰撞照常（飞不穿墙）。
        // 穿墙：characterMovement.detectCollisions = false → 胶囊碰撞器禁用，加飞行控制避免沉底。
        // HandleMove 是游戏"读输入→设方向→设重力"的地方，Prefix 拦下来换成我们的逻辑。

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
                if (!IsLocalMovement(__instance)) return true;   // 别人的角色不碰

                if (!active)
                {
                    // 关掉后把碰撞和移动模式复位（重力由游戏自己每帧设，不用管）
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
                    // 死了/不在对局：恢复碰撞，别穿墙着死；不飞
                    __instance.characterMovement.detectCollisions = true;
                    if (__instance.IsFlying()) __instance.SetMovementMode(Character.MovementMode.Walking, 0);
                    return false;
                }

                // 穿墙：关碰撞；纯飞行：碰撞照常
                __instance.characterMovement.detectCollisions = !Features.Noclip;

                // 零重力 + 飞行模式
                __instance.gravity = Vector3.zero;
                if (!__instance.IsFlying()) __instance.SetMovementMode(Character.MovementMode.Flying, 0);

                // 移动方向：WASD 优先读游戏自己的移动输入（尊重改键/手柄），读不到退回原始键盘兜底，
                // 防游戏输入被禁导致 moveAction 恒 0、WASD 失灵；空格上 / ctrl 下（原始键盘，一直可用）
                Vector3 dir = Vector3.zero;
                if (!Features.MenuOpen)   // 菜单开着不操控（避免打字框里按空格把自己顶飞）
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
                    // 移动基准用「真实视角」方向（AntiAim.GetForward/GetRight）：
                    // AA 开着时 transform 被假yaw转着，跟 transform.forward 走 WASD 会跟着AA乱转；
                    // 这俩永远返回真实yaw算的水平方向（AA关=相机朝向），和 AA 补丁一致。
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

        // 飞行速度：Flying 模式吃 maxFlySpeed（游戏从没用过，是 0），给它一个下限
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

        // 飞行手感：加速拉满，指哪打哪不肉
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
    }
}
