# NGO 多人联机 — 从零到一教学教程

## 教学路线图

```
第1课 □ 连接建立 + 网络玩家生成          ← 从这里开始
第2课 □ 玩家移动同步
第3课 □ 动画同步（NetworkAnimator）
第4课 □ 射击同步（ServerRpc + ClientRpc）
第5课 □ 子弹网络化
第6课 □ 敌人服务器权威（AI/伤害/死亡）
第7课 □ 玩家列表 UI + 名称显示
```

每节课都会：**解释原理 → 改代码 → 告诉你如何测试 → 总结关键知识点**

---

## 第1课：连接建立 + 网络玩家生成

### 🎯 目标
> 两个客户端连接后，各自在场景中生成一个玩家角色，互相能看到对方。

### 📖 核心概念

**什么是 NetworkObject？**
- 任何需要在网络上同步的 GameObject 都必须挂 `NetworkObject` 组件
- 它给每个对象分配全局唯一的 `NetworkObjectId`
- 拥有它的客户端叫 Owner，Owner 有特殊权限

**什么是 NetworkBehaviour？**
- 继承自 MonoBehaviour，增加了网络能力
- `IsOwner` — 当前客户端是否拥有这个对象
- `IsServer` — 当前端是否是服务器
- `IsClient` — 当前端是否是客户端

**Player Prefab 是什么？**
- 在 NetworkManager 中注册，当玩家连接时自动在网络中生成（Spawn）
- 每个连接自动获得一个该预制体的实例

### 🛠 操作步骤

#### 1.1 改造 PlayerModel

将 `PlayerModel` 从普通 MonoBehaviour 改为 NetworkBehaviour：

```
PlayerModel.cs 关键改动：
- 继承关系：MonoBehaviour → NetworkBehaviour（第14行）
- Awake 中添加 IsOwner 判断
- 添加 OnNetworkSpawn / OnNetworkDespawn 生命周期
```

#### 1.2 改造 PlayerController

当前 `PlayerController` 是全局单例，控制场景中所有 PlayerModel。
网络化后每个玩家只控制自己的角色：

```
PlayerController.cs 关键改动：
- 不再继承 SingleMonoBase（移除单例模式）
- 挂载到 PlayerModel 预制体上，每个玩家实例一份
- Start 中判断 IsOwner，只有自己的角色才绑相机+输入
```

#### 1.3 配置 NetworkManager

在 Unity 编辑器中：
1. 选中场景中的 NetworkManager
2. 将改造后的 PlayerModel 预制体拖入 `Network Prefabs` 列表
3. 勾选 `Player Prefab` 槽位放一个 PlayerModel 预制体

### 📝 代码清单

见下方具体代码修改。

### 🧪 测试方法
1. 菜单 File → Build Settings → 勾选当前场景 → Build 一个 .exe
2. 在 Unity 编辑器中点 Play，点 Host
3. 运行刚才 Build 的 .exe，点 Client
4. 看两个窗口是否都出现了角色，而且能互相看到

### 🔑 关键知识点
| 概念 | 解释 |
|------|------|
| NetworkObject.Spawn() | 让一个对象在所有客户端上出现 |
| IsOwner | 这个对象是不是"我的" |
| OwnerClientId | 拥有者的客户端ID，0 = Server |
| Host = Server + Client | Host 既当服务器又当客户端 |
```

---

现在正式开始第一课。我会一边解释原理，一边实际修改代码。

---

## 第1课：实操开始

### 步骤一：改造 PlayerModel 为 NetworkBehaviour

核心变化：让 `PlayerModel` 从普通 `MonoBehaviour` 升级为 `NetworkBehaviour`，使其具备网络同步能力。

#### 为什么这样改？

`NetworkBehaviour` 是 NGO 的基石，它比普通 MonoBehaviour 多了：
- `IsOwner` — 能判断"这个角色是不是我的"
- `NetworkObject` — 在每个客户端上分配全局唯一ID
- `OnNetworkSpawn()` — 网络对象生成时调用（替代 Start 中的网络相关初始化）

#### 代码修改

修改 `Player/PlayerModel.cs`：

**改动1**：继承 NetworkBehaviour，添加命名空间

```csharp
// 原来的第1-5行
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

// 改为（添加 Unity.Netcode）
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;          // ← 新增
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
```

**改动2**：第14行，继承关系从 MonoBehaviour 改为 NetworkBehaviour

```csharp
// 原来
public class PlayerModel : MonoBehaviour, IStateMachineOwner

// 改为
public class PlayerModel : NetworkBehaviour, IStateMachineOwner
```

> **知识点**：`NetworkBehaviour` 继承自 `MonoBehaviour`，所以 Start/Update 等方法照常使用，同时还获得了 IsOwner/IsServer 等网络属性。

### 步骤二：改造 PlayerController，让每个玩家只控制自己的角色

核心变化：`PlayerController` 不再作为全局单例，而是挂到 `PlayerModel` 预制体上，由每个客户端各自持有。通过 `IsOwner` 判断来决定是否激活输入和相机。

#### 为什么这样改？

单机时，一个 `PlayerController` 控制所有角色切换没问题。联网后：
- 你的客户端只应该控制"你的"角色（IsOwner = true）
- 别人的角色由他们自己的客户端控制
- `IsOwner` 是 NGO 判断"这是谁的角色"的核心机制

#### 代码修改

修改 `Player/PlayerController.cs`：

**改动1**：移除单例继承

```csharp
// 原来第7行
public class PlayerController : SingleMonoBase<PlayerController>

// 改为
public class PlayerController : MonoBehaviour
```

**改动2**：移除 `PlayerController.INSTANCE` 的所有引用

把原来通过 `PlayerController.INSTANCE.xxx` 访问的地方，改为直接访问自身的 `PlayerController` 实例。

这需要同步修改引用它的其他文件。我们用 **搜索** 找出所有 `PlayerController.INSTANCE` 的引用点。

> 等一下，`PlayerModel.cs` 中也有 `PlayerController.INSTANCE` 的引用。但 `PlayerModel` 现在需要知道自己的 `PlayerController` 在哪儿 — 因为它俩现在是挂在同一个预制体上的，所以通过 `GetComponent<PlayerController>()` 就能找到。

---

好了，前两步涉及多处联动修改，我不再逐一说"改这里改那里"，直接给你完整的改动文件，并标注每个改动的意义。

我现在把所有第一步需要的文件改写完毕，你来应用。准备好了吗？

（等待你回复"准备好了"我再继续给你的项目实际写入代码）<｜end▁of▁thinking｜>我已经准备好了教学大纲。现在开始**第1课**的实操。

我会直接把代码写入你的项目，同时解释每处改动的原理。

<｜｜DSML｜｜tool_calls>
<｜｜DSML｜｜invoke name="todo_write">
<｜｜DSML｜｜parameter name="merge" string="false">false