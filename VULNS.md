# 漏洞与接口一览

所有功能用的都是「客户端权威 + 服务端不校验」的空子，服务端权威字段（金币、废料总量）改不动。
能利用的接口能列的都列在这了，二创照着翻代码就行。

## 1. 远程 RPC —— 可调、服务端不校验

| 接口 | 漏洞 | 用处 |
|---|---|---|
| `entity_player.OnUseRPC(ref, true)` | `[Rpc(SendTo.Server)]`，服务端只校验目标存在，**不校验距离/朝向** | 隔空按电话按钮 / 任意交互，一键拨号 |
| `entity_prop_delivery.SetGrabbing(true)` | 公开方法 → 内部发 `SetGrabbingRPC` → 服务端 `ChangeOwnership` 转所有权，**不校验距离** | 隔空抓起配送件 |
| `entity_phys.SetGrabbing(true)` | 同上；`CanGrab` 只查锁定，不查被谁抓 | 抢目标手里的物理物 |
| `entity_player.TakeHealthRPC(255)` | `[Rpc(SendTo.Owner, Everyone)]` 无校验 | 秒杀；D-SAFE 挡第一下，0.2s 后补刀破 |
| `entity_player.ShoveRPC(dir, force)` | `[Rpc(SendTo.Owner, Everyone)]`，**力无上限** | 推飞 / 连推 / 弹开接近电话的玩家 |
| `entity_player.SetHealthRPC(0~100)` | `[Rpc(SendTo.Owner, Everyone)]` | 钉血；拉满可触发完整复活流程（绕开复活终端收钱） |
| `entity_player_inventory.DropItemRPC(ref)` | 强制目标丢出背包物品 | 丢包 |
| `entity_phys_prop_scrap.SetVacuumingRPC(local, true)` | 真空判定在客户端（锥形+视线），服务端只认 RPC | 全图吸废料 |
| `ChatController.ChatServerRPC(text, rp)` | `[Rpc(SendTo.SpecifiedInParams, InvokePermission=Everyone)]` | 刷屏 |
| `entity_glass.OnBreakRPC(pos)` | private，反射调 | 碎全场玻璃 |
| `entity_item_lowgrav.ToggleRPC()` | private，反射调 | 低重力板开关（代码有，菜单没放按钮） |
| `entity_player.SetPositionRPC(pos, rot)` | 房主可调 | 拉人（代码有，菜单没放按钮） |

## 2. 房主直写（Server 权威接口）

| 接口 | 用处 |
|---|---|
| `ScrapController.Add(amount)` | 直写废料账本 ③（claimedScrap） |
| `ScrapController.RemoveWorldScrap(reward)` | 扣世界账本 |
| `bag.AddScrap(reward)` / `bag.Clear()` / `bag.SetScrap(max)` | 直写真空袋 |
| `scrap.NetworkObject.Despawn(true)` | 直接销毁废料实体 |

> 关键坑：服务端塞袋只认 `AddScrap()`，**袋满直接 return → 那件废料被销毁且不进账**。
> 所以全图吸绝不一口气全标记，每轮限量 + 按剩余容量估算跳过放不下的。

## 3. 客户端权威字段（本地写、服务端信任）

| 字段 / 机制 | 漏洞 | 用处 |
|---|---|---|
| `entity_player.SetHealth(byte)` | 伤害在本地结算，服务端信任客户端 | 无敌：Prefix 把 0 改成 1 |
| `NetworkTransform` | owner 本地写 `transform.position`，无校验 | 配送件隔空传送到送货台 |
| `NetworkList`（背包） | Everyone 可读、不去重、永远最新 | 读目标背包比缓存哈希全 |

## 4. 反射接口（private，直接调不了所以反射）

| 反射目标 | 类型 | 用处 |
|---|---|---|
| `entity_player.OnUseRPC` | MethodInfo（Instance\|NonPublic） | 隔空按按钮 |
| `PhoneController.PHONE_INDEX` | FieldInfo（Static\|NonPublic），index→字符 | 找电话按钮 |
| `ChatController.ChatServerRPC` | MethodInfo（Instance\|NonPublic） | 刷屏 |
| `entity_glass.OnBreakRPC` / `entity_item_lowgrav.ToggleRPC` | private 方法 | 全场远程触发 |
| `entity_player_camera._yawInput/_pitchInput/_characterMovement/_vehicle` | 字段 | AA 读真实朝向 |
| `entity_player_camera.IsBeingForced` | 方法 | AA 跳过过场 / 强制视角 |

## 5. Harmony 补丁

| 补丁目标 | 方式 | 用处 |
|---|---|---|
| `entity_player.SetHealth` | Prefix | 无敌 |
| `Character.GetMaxSpeed` | Postfix | 移动加速（virtual，走/飞/游全吃） |
| `Character.UpdateRotation` | Prefix | AA 禁自动转向 |
| `entity_player_camera.UpdateView` | Postfix | AA 假视角 |
| `entity_player_movement.HandleMove` | Transpiler | AA 换 forward/right |
| `entity_player_movement.HandleMove` | Prefix | 飞行 / 穿墙 |
| `entity_player_movement.GetMaxSpeed` | Postfix | 飞行速度下限 |
| `Character.GetMaxAcceleration` | Postfix | 飞行加速手感 |

## 6. 关键单例 / 控制器

| 单例 | 用处 |
|---|---|
| `PlayerController.LOCAL` | 本地玩家 |
| `NetController<PhoneController>.Instance` | 电话 |
| `NetController<ContractController>.Instance` | 任务 / 下单 |
| `NetController<ScrapController>.Instance` | 废料账本 |
| `NetController<DeliveryController>.Instance` | 送货台 |
| `NetController<ChatController>.Instance` | 聊天 |
| `NetController<CurrencyController>.Instance` | 债务（通关判定） |
| `NetController<IngameController>.Instance.Status()` | 对局状态（PLAYING） |
| `MonoController<PlayerController>.Instance` | 玩家列表 |
| `MonoController<StartupController>.Instance` | 光标请求栈 |
| `NetworkManager.Singleton.IsServer` | 房主判断 |

## 7. 关键方法 / 字段

- 任务：`ContractController.GetAffordableTasks(claimedScrap, false)`、`Task.Address`、`Task.HasDeliveryItem`
- 电话：`PhoneController.phoneButtons`、`entity_button.IsLocked()`、`PHONE_STATUS.IDLE / TALKING / SPECIAL_MODE / INVALID_NUMBER`
- 配送：`DeliveryController.GetDeliverySpotByAddress(addr)`、`entity_prop_delivery.GetAddress() / IsLocked() / NetworkObjectId`
- 废料：`ScrapController.GetMaxContainerScrap() / GetWorldScrap(false)`、`entity_phys_prop_scrap.GetReward() / .scrap / CanScrap(local)`
- 玩家：`GetPlayerID() / GetPlayerName() / IsDead() / GetHealth() / IsSpawned() / MAX_HEALTH`
- 真空：`local.GetVacuum() → GetVacuumHolder() → GetTotalScrap()`
- 移动：`moveAction`、`characterMovement.detectCollisions`、`gravity`、`SetMovementMode(Flying/Walking)`、`SetMovementDirection(dir)`
- 其它：`Physics.SyncTransforms()`（传送后同步；配送结算要求松手后静止满 1s）、`NetworkBehaviourReference(obj)`（RPC 参数包装）

## 8. 通用套路

1. **隔空调用**：`OnUseRPC`/`SetGrabbing` 这类不校验距离的，直接反射调或调公开方法即可。
2. **房主直写**：`Add`/`Clear`/`SetScrap` 这类 [Server] 方法，房主直接调绕开物理流程（如倒袋、复活收钱）。
3. **传送结算**：拿到 owner 后本地写 `transform.position`，再 `Physics.SyncTransforms()`，服务端触发器照常结算。
4. **补刀破 D-SAFE**：第一杀被保活道具挡，0.2s 后再杀一次必死。
