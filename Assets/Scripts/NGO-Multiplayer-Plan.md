# QRW3D NGO 多人联机改造计划文档

## 一、项目现状概览

| 模块 | 现状 | 网络就绪度 |
|------|------|-----------|
| 输入系统 | Unity Input System，PlayerController 单例读取 | 需改造 |
| 玩家移动 | CharacterController + NavMeshAgent 双模式 | 需改造 |
| 玩家射击 | PlayerWeapon / PlayerWeaponBullet，本地即时生成 | 需改造 |
| 敌人 AI | NavMeshAgent 寻路，状态机驱动 | 需改造 |
| 状态机 | 泛型 StateMachine + MonoManager Update 分发 | 需改造 |
| 管理器 | GameManager / MonoManager / UIManager 均单例 | 需改造 |
| 网络基础 | 已有 NetworkManagerUI + RPC 测试代码 | 部分就绪 |
| UI | 敌人血条（世界空间 Billboard） | 需扩展 |

---

## 二、架构改造总览

```
改造前（单机）                          改造后（NGO 多人）
─────────────────────────────────      ─────────────────────────────────
Client = Server（同一进程）             Server（权威） / Client（表现）
Instantiate() 直接生成                  NetworkObject.Instantiate() 网络生成
本地输入直接控制角色                     本地输入 → ServerRpc → 服务器处理
敌人AI在客户端运行                      敌人AI仅在服务器运行，状态同步给客户端
单例全局访问                            区分 Server/Client 实例
GameManager 全权管理                    引入 LobbyManager / PlayerSpawnManager
```

---

## 三、详细改造清单

### 阶段一：网络基础设施搭建

#### 1.1 NetworkManager 配置强化

**文件**: `Managers/` 新增 `NetworkGameManager.cs`

```
改动内容：
├── 创建 NetworkGameManager : NetworkBehaviour
│   ├── 持有 NetworkManager 引用
│   ├── ConnectionApprovalCallback — 连接审批（可选密码/版本校验）
│   ├── OnClientConnected / OnClientDisconnected 回调
│   ├── OnServerStarted / OnServerStopped 回调
│   └── 场景加载/切换管理（NetworkManager.SceneManager）
│
├── 修改 NetworkManagerUI.cs
│   ├── 增加玩家名称输入
│   ├── 增加 IP 地址输入（局域网联机）
│   ├── 增加房间密码（可选）
│   └── 连接状态提示（Loading / Connected / Failed）
│
└── 配置 NetworkManager Prefab
    ├── NetworkObject 预制体列表（Player / Enemy 等）
    ├── NetworkAnimator 组件注册
    └── RPC 可靠性配置
```

#### 1.2 网络预制体注册

```
需要注册为 NetworkObject 预制体的对象：
├── PlayerModel      → 玩家角色（含 CharacterController, Animator, NetworkAnimator）
├── PlayerWeaponBullet → 子弹（含 Rigidbody, NetworkRigidbody）
├── ZombieEnemy      → 僵尸敌人（含 NavMeshAgent, Animator, NetworkAnimator）
└── （可选）掉落物 / 特效
```

---

### 阶段二：玩家系统改造

#### 2.1 PlayerController 改造

**文件**: `Player/PlayerController.cs`

```
改造要点：
├── 不再作为全局单例 — 每个客户端只控制自己的角色
│   ├── 移除 SingleMonoBase<PlayerController> 继承
│   ├── 改为普通 MonoBehaviour，由 NetworkGameManager 分配本地玩家引用
│   └── 玩家切换改为网络消息（切换控制哪个 PlayerModel）
│
├── 输入处理区分所有权
│   ├── if (!IsOwner) return; → 仅本地玩家处理输入
│   ├── if (!IsLocalPlayer) return; → 他人角色不做输入响应
│   └── 输入读取保留在 Update 中，但结果通过 ServerRpc/NetworkVariable 传递
│
├── 相机管理
│   ├── 改为客户端权威：只有本地玩家的 Cinemachine 相机激活
│   └── 远程玩家不创建/激活相机
│
└── 多角色切换
    └── 服务器管理所有 PlayerModel 归属，客户端请求切换
```

#### 2.2 PlayerModel 改造

**文件**: `Player/PlayerModel.cs`

```
改造要点：
├── 继承关系变更：MonoBehaviour → NetworkBehaviour
│   ├── 添加 NetworkObject 组件
│   └── 添加 ClientNetworkTransform（客户端权威的位置同步）
│
├── 位置/旋转同步
│   ├── 本地玩家：使用 ClientNetworkTransform + CharacterController
│   ├── 远程玩家：接收网络位置数据，插值平滑显示
│   └── NavMeshAgent 跟随逻辑改为仅服务器执行并同步
│
├── 动画同步
│   ├── 添加 NetworkAnimator 组件
│   ├── 配置同步参数：MoveSpeed, IsJumping, IsAiming 等
│   └── 动画触发同步（Hit 动画等通过 RPC）
│
├── 跳跃同步
│   ├── 本地：Input → ServerRpc(RequestJump) → 服务器执行跳跃逻辑
│   └── 服务器计算重力/滞空，NetworkVariable 同步 fallHeight
│
└── IK 瞄准同步
    ├── AimTarget Transform 位置通过 NetworkVariable<Vector3> 同步
    └── TwoBoneIKConstraint / MultiAimConstraint 权重本地计算
```

#### 2.3 PlayerWeapon 改造

**文件**: `Player/PlayerWeapon.cs`

```
改造要点：
├── 射击请求：客户端 → ServerRpc(FireRequest)
│   ├── 服务器验证射击条件（间隔、子弹数等）
│   ├── 服务器执行 Fire() → 生成子弹 NetworkObject
│   └── ClientRpc(OnFireEffect) → 所有客户端播放枪口特效/音效
│
├── 子弹间隔在服务器端校验（防止作弊）
│   ├── 使用 NetworkVariable<float> 或服务器端时间戳
│   └── 客户端本地也做间隔限制（体验），服务器为最终权威
│
└── bulletSpawnPoint 同步
    └── Transform 引用通过 NetworkObject 的引用解析（本地即可）
```

#### 2.4 PlayerWeaponBullet 改造

**文件**: `Player/PlayerWeaponBullet.cs`

```
改造要点：
├── 改为网络生成
│   ├── 服务器 Spawn NetworkObject
│   ├── 添加 NetworkRigidbody 组件（速度同步）
│   └── 添加 NetworkTransform（位置同步）
│
├── 碰撞检测
│   ├── 服务器端检测碰撞 + 伤害计算（权威）
│   ├── 客户端仅做特效播放（预测 + 确认）
│   └── 命中确认通过 ClientRpc 广播
│
├── 子弹生命周期
│   ├── 服务器管理生存时间 → 到时 Despawn
│   └── 命中后服务器 Despawn（同步销毁）
│
└── 尾迹特效
    └── 本地即时生成（基于 TrailRenderer），不依赖网络同步
```

#### 2.5 玩家状态改造

**文件**: `Player/States/PlayerIdleState.cs`, `PlayerMoveState.cs`, `PlayerHoverState.cs`, `PlayerAimingState.cs`

```
改造要点：
├── 状态切换触发机制变更
│   ├── 本地玩家：输入驱动状态切换（保留现有逻辑）
│   ├── 远程玩家：网络状态驱动（ServerRpc 通知状态变更）
│   └── 状态变更时调用 UpdatePlayerStateServerRpc(stateEnum)
│
├── 每个状态的 Update 中区分 IsOwner
│   ├── 本地玩家：正常执行移动/旋转/射击逻辑
│   └── 远程玩家：仅做视觉同步（动画由 NetworkAnimator 自动处理）
│
├── PlayerMoveState 特殊处理
│   ├── 非控制角色的 NavMeshAgent 跟随 → 仅服务器执行
│   └── 远程玩家不运行 NavMeshAgent，由网络位置驱动
│
└── PlayerAimingState 特殊处理
    ├── 瞄准方向通过 NetworkVariable<Vector3> 同步
    └── IK 瞄准目标位置通过 NetworkVariable<Vector3> 同步
```

---

### 阶段三：敌人系统改造

#### 3.1 EnemyBase 改造 — 服务器权威

**文件**: `Enemy/EnemyBase.cs`

```
改造要点：
├── 继承关系：MonoBehaviour → NetworkBehaviour
│   ├── 添加 NetworkObject 组件
│   └── 添加 NetworkAnimator 组件
│
├── AI 逻辑改造：服务器权威模式
│   ├── if (!IsServer) return; → 仅在服务器执行 AI 逻辑
│   ├── FindAttackTarget() → 仅在服务器寻找目标
│   ├── NavMeshAgent 驱动 → 仅在服务器运行
│   └── 客户端不运行寻路/状态机
│
├── 位置同步
│   ├── 添加 NetworkTransform
│   ├── 配置为服务器到客户端单向同步
│   └── 客户端插值平滑显示
│
├── 动画同步
│   ├── NetworkAnimator 自动同步 Animator 参数
│   └── 触发型动画（Hit/Attack/Dead）通过 RPC 同步
│
├── 属性同步（使用 NetworkVariable）
│   ├── NetworkVariable<float> currentHealth
│   ├── NetworkVariable<float> moveSpeed
│   ├── NetworkVariable<bool> isDead
│   └── NetworkVariable<int> currentStateEnum（状态枚举）
│
├── Hurt() 改造
│   ├── 客户端子弹命中 → ServerRpc(RequestHurt, damage, attackerId)
│   ├── 服务器计算伤害 + 更新 currentHealth
│   ├── currentHealth.OnValueChanged → 客户端同步血条更新
│   ├── 特效播放：ClientRpc(PlayHurtEffect) — 血液特效
│   └── 死亡判定：服务器检测 health <= 0 → 状态切换
│
└── 血条 UI 同步
    ├── EnemyHealthBar 在客户端本地管理（服务器不需要）
    ├── currentHealth.OnValueChanged → 更新 fillAmount
    └── 血条显示/隐藏通过 ClientRpc 同步
```

#### 3.2 ZombieEnemy 状态改造

**文件**: `Enemy/ZombieEnemy.cs`, `Enemy/EnemyState/`

```
改造要点：
├── 状态机仅在服务器运行
│   ├── if (!IsServer) return; 在每个状态的 Update 开头
│   └── MonoManager.AddUpdateAction 也仅服务器调用
│
├── 状态变更通过网络同步给客户端
│   ├── currentStateEnum.OnValueChanged → 客户端收到状态变更
│   └── 客户端根据状态枚举播放对应的客户端动画逻辑
│
├── 各状态改造
│   ├── ZombieIdleState  → 目标检测逻辑仅服务器
│   ├── ZombieMoveState  → ChaseTarget() 仅服务器
│   ├── ZombieAttackState → 攻击伤害判定仅服务器 + ClientRpc(PlayAttackAnimation)
│   └── ZombieDeadState   → 服务器触发 + ClientRpc(PlayDeathEffect), 定时 Despawn
│
└── 客户端仅做视觉播放
    └── 摧毁由服务器 Despawn 触发（NetworkObject.Despawn）
```

#### 3.3 敌人生成管理

**文件**: `Managers/` 新增 `EnemySpawnManager.cs`

```
新增内容：
├── EnemySpawnManager : NetworkBehaviour
│   ├── 仅在服务器运行
│   ├── 定时/波次生成敌人
│   ├── 生成点配置（Transform[] spawnPoints）
│   ├── 最大敌人数限制
│   ├── 根据玩家数量调整生成量
│   └── NetworkObject.Instantiate() 网络生成僵尸
│
└── 配置项
    ├── spawnInterval — 生成间隔
    ├── maxEnemyCount — 最大数量
    ├── enemiesPerPlayer — 每玩家敌人数
    └── difficultyCurve — 难度曲线（时间增长）
```

---

### 阶段四：管理器改造

#### 4.1 GameManager 改造

**文件**: `Managers/GameManager.cs`

```
改造要点：
├── 拆分职责（当前过于集中）
│   ├── GameManager — 保留游戏状态管理 + 玩家列表
│   └── 新增的 NetworkGameManager — 网络连接管理
│
├── 添加网络感知
│   ├── 玩家加入/离开回调
│   ├── 游戏开始/结束状态同步
│   └── NetworkVariable<GameState> gameState
│
├── PlayerModel[] 数组
│   ├── 改为动态管理（玩家加入时添加，离开时移除）
│   └── 使用 NetworkList<ulong> connectedPlayerIds
│
├── 相机管理
│   └── Cinemachine 相机归本地玩家的 PlayerController 管理，不再放 GameManager
│
└── AimTarget
    └── 每个玩家有自己的 AimTarget，不再全局共用
```

#### 4.2 MonoManager 改造

**文件**: `Managers/MonoManager.cs`

```
改造要点：
├── 区分 Server/Client 的 Update 注册
│   ├── 服务器：注册所有 AI 状态的 Update（状态机逻辑）
│   └── 客户端：仅注册本地玩家的状态 Update + UI 更新
│
├── 添加 NetworkUpdate 阶段处理
│   └── 与 NetworkManager 的 NetworkTickSystem 配合
│
└── 或者保持现状，通过 IsServer/IsClient 判断在各状态内部处理
    （推荐：在各状态 Update 开头加判断，MonoManager 不修改）
```

#### 4.3 UIManager 改造

**文件**: `Managers/UIManager.cs`

```
改造要点：
├── WorldSpaceCanvas 保持
│   └── 血条 UI 的 Billboard 效果不变
│
├── 新增网络 UI 管理
│   ├── 玩家列表显示（名字 + 状态）
│   ├── 击杀/死亡统计
│   ├── 聊天消息（预留 Chat 目录）
│   └── 断线重连提示
│
└── 本地化
    └── 根据 NetworkManager.IsConnectedClient 决定是否显示网络 UI
```

---

### 阶段五：新增系统

#### 5.1 玩家生成与管理

**文件**: `Managers/` 新增 `PlayerSpawnManager.cs`

```
PlayerSpawnManager : NetworkBehaviour
├── 玩家加入时自动生成 PlayerModel
│   ├── OnClientConnected → SpawnPlayer(clientId)
│   ├── 使用 NetworkManager.SpawnManager.Instantiate()
│   └── 分配生成点
│
├── 玩家离开时清理
│   ├── OnClientDisconnected → DespawnPlayer(clientId)
│   └── 清理所属的 PlayerModel NetworkObject
│
├── 玩家角色选择
│   ├── 目前是 3 个 PlayerModel 的切换
│   ├── 网络版可改为每个玩家拥有一个角色
│   └── 或保留多角色切换，但通过 ServerRpc 请求
│
└── 断线重连
    ├── 保留玩家数据（可选）
    └── NetworkManager.ConnectionApprovalCallback 处理重连
```

#### 5.2 同步 NetworkVariable 数据模型

**文件**: `Multi/` 新增 `PlayerNetworkData.cs`, `GameNetworkData.cs`

```csharp
// PlayerNetworkData
public struct PlayerNetworkData : INetworkSerializable
{
    public FixedString32Bytes PlayerName;
    public int SelectedCharacterIndex;
    public int Score;
    public int Kills;
    public int Deaths;
}

// GameNetworkData
public struct GameNetworkData : INetworkSerializable
{
    public int CurrentWave;
    public int EnemiesRemaining;
    public float GameTime;
}
```

#### 5.3 Chat 聊天系统

**文件**: `Chat/` 新增（使用预留目录）

```
Chat 系统（可选）
├── ChatMessage.cs — 消息数据结构（INetworkSerializable）
├── ChatManager.cs : NetworkBehaviour
│   ├── SendMessage(string msg) → ServerRpc
│   └── ClientRpc(ReceiveMessage, senderName, msg)
└── ChatUI.cs — 聊天框 UI 显示
```

---

### 阶段六：状态机架构调整

#### 6.1 StateMachine 兼容网络

**文件**: `Utils/StateMachine.cs`

```
改造要点：
├── 状态机本身不需要改动（纯业务逻辑）
├── 改动在状态基类层面：
│   ├── EnemyStateBase 添加 [ServerOnly] 标记
│   └── PlayerStateBase 区分本地/远程行为
│
├── 状态切换流程变更：
│   本地玩家输入 → 状态切换 → ServerRpc(NotifyStateChange)
│   服务器收到 → 状态验证 → 更新 NetworkVariable
│   远程客户端 → OnValueChanged → 驱动本地动画/表现
│
└── 或者更简单的方案：
    使用 NetworkAnimator 同步 Animator 参数即可
    状态机仅做本地逻辑控制（推荐）
```

**推荐方案**：状态机保留客户端逻辑，通过网络变量 + NetworkAnimator 驱动动画同步，而非直接同步状态枚举。这样可以避免状态机同步的复杂性和时序问题。

---

## 四、文件改动清单汇总

| 文件 | 改动类型 | 优先级 |
|------|---------|--------|
| **新增** | | |
| `Managers/NetworkGameManager.cs` | 新建 | P0 |
| `Managers/PlayerSpawnManager.cs` | 新建 | P0 |
| `Managers/EnemySpawnManager.cs` | 新建 | P1 |
| `Multi/PlayerNetworkData.cs` | 新建 | P1 |
| `Multi/GameNetworkData.cs` | 新建 | P1 |
| `Chat/ChatManager.cs` | 新建 | P2 |
| `Chat/ChatUI.cs` | 新建 | P2 |
| **修改** | | |
| `Player/PlayerController.cs` | 改造 | P0 |
| `Player/PlayerModel.cs` | 改造 | P0 |
| `Player/PlayerWeapon.cs` | 改造 | P0 |
| `Player/PlayerWeaponBullet.cs` | 改造 | P0 |
| `Player/States/PlayerIdleState.cs` | 改造 | P1 |
| `Player/States/PlayerMoveState.cs` | 改造 | P1 |
| `Player/States/PlayerHoverState.cs` | 改造 | P1 |
| `Player/States/PlayerAimingState.cs` | 改造 | P1 |
| `Enemy/EnemyBase.cs` | 改造 | P0 |
| `Enemy/ZombieEnemy.cs` | 改造 | P1 |
| `Enemy/EnemyState/ZombieIdleState.cs` | 改造 | P1 |
| `Enemy/EnemyState/ZombieMoveState.cs` | 改造 | P1 |
| `Enemy/EnemyState/ZombieAttackState.cs` | 改造 | P1 |
| `Enemy/EnemyState/ZombieDeadState.cs` | 改造 | P1 |
| `Managers/GameManager.cs` | 改造 | P0 |
| `Managers/UIManager.cs` | 改造 | P2 |
| `Multi/NetworkManagerUI.cs` | 改造 | P0 |
| `Multi/PlayerNet.cs` | 改造/删除 | P0 |
| **无需改动** | | |
| `Base/SingleMonoBase.cs` | 保留 | — |
| `Base/StateBase.cs` | 保留（加 IsOwner 判断逻辑在各状态内部） | — |
| `Managers/MonoManager.cs` | 保留 | — |
| `Utils/StateMachine.cs` | 保留 | — |
| `UI/EnemyHealthBarUI.cs` | 保留（小改 OnValueChanged 绑定） | — |

---

## 五、实施建议

### 实施顺序

```
第1步（1-2天）：网络基础设施
├── 创建 NetworkGameManager
├── 改造 NetworkManagerUI
├── 配置 NetworkManager Prefab（注册预制体）
└── 测试：Host/Client 连接建立

第2步（2-3天）：玩家网络化
├── PlayerModel + PlayerController 改造
├── PlayerWeapon 射击同步
├── PlayerWeaponBullet 子弹网络化
└── 测试：两个玩家可看到对方移动、射击

第3步（1-2天）：敌人网络化
├── EnemyBase 改造（服务器权威）
├── 伤害/死亡同步
├── EnemySpawnManager 创建
└── 测试：敌人行为在所有客户端一致

第4步（1天）：UI 和体验优化
├── 玩家列表/名称显示
├── 击杀统计
├── 延迟/插值优化
└── 测试：整体联机体验

第5步（可选）：高级功能
├── Chat 聊天
├── 房间/大厅
├── 断线重连
└── 观战模式
```

### 关键技术决策

| 决策点 | 推荐方案 | 原因 |
|--------|---------|------|
| 玩家位置同步 | ClientNetworkTransform（客户端权威） | 减少延迟感，适合 PvE 游戏 |
| 敌人AI | 服务器权威 | 防止作弊，行为一致 |
| 子弹命中 | 服务器端碰撞检测 | 防止作弊，伤害权威 |
| 动画同步 | NetworkAnimator | Unity 原生支持，参数自动同步 |
| 状态机 | 保留本地，通过网络变量驱动 | 降低复杂度，避免状态同步时序问题 |
| 多角色切换 | 每个玩家一个角色（简化） | 或保留切换但通过 ServerRpc |

---

## 六、风险与注意事项

1. **CharacterController vs Rigidbody**：目前使用 CharacterController（非物理驱动），网络同步需借助 ClientNetworkTransform，不支持插值外推。建议评估是否改用 Rigidbody + NetworkRigidbody。

2. **MonoManager Update 分发**：当前所有状态通过 MonoManager 统一 Update。网络化后需确保仅在正确的端执行（服务器端运行 AI，客户端仅本地玩家）。

3. **Instantiate 调用改造**：所有 `Instantiate()` 需改为网络版本，否则对象只在本地存在，其他客户端看不到。

4. **Scene 中的预制体**：场景中预放置的 `ZombieEnemy` 需要移除，改为由 `EnemySpawnManager` 动态网络生成。或用 `NetworkObject.SceneMigration` 转换。

5. **PlayerNet.cs / RpcTest**：现有测试代码需删除或替换为实际游戏网络代码。

6. **类名不一致问题**：`PlayerNet.cs` 中类名为 `RpcTest`、`EnemyHealthBarUI.cs` 中类名为 `EnemyHealthBar`，建议一并修正。

---

*文档生成日期：2026-08-01*
