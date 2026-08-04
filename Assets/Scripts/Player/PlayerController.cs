using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// 玩家控制器 — 挂载在 PlayerNetwork 预制体根节点上。
/// 持有属于自己的 3 个 PlayerModel 引用，负责输入处理、模型切换和相机管理。
/// 网络模式下每个玩家持有自己的实例，通过 IsOwner 判断是否处理输入。
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("Network Sync")]
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private NetworkAnimator[] networkAnimators; // 每个 PlayerModel 上的 NetworkAnimator

    [Header("Owned Models")]
    [Tooltip("该玩家拥有的 3 个角色模型（在预制体中拖入或代码注入）")]
    public PlayerModel[] ownedModels;

    /// <summary>网络同步的当前激活模型索引（0/1/2）</summary>
    private NetworkVariable<int> activeModelIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    /// <summary>网络同步的动画状态名，非 Owner 端用于播放对应动画</summary>
    private NetworkVariable<FixedString32Bytes> syncAnimState = new NetworkVariable<FixedString32Bytes>(
        "Idle",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    /// <summary>网络同步的 AimTarget 世界坐标，非 Owner 端用于 IK 约束</summary>
    private NetworkVariable<Vector3> syncAimTargetPos = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    /// <summary>网络同步的瞄准混合参数，控制 BlendTree 方向</summary>
    private NetworkVariable<float> syncAimingX = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );
    private NetworkVariable<float> syncAimingY = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private static readonly int AimingXHash = Animator.StringToHash("AimingX");
    private static readonly int AimingYHash = Animator.StringToHash("AimingY");

    /// <summary>当前激活的 PlayerModel（根据 activeModelIndex 动态获取）</summary>
    public PlayerModel currentPlayerModel
    {
        get
        {
            if (ownedModels == null || ownedModels.Length == 0) return null;
            int idx = Mathf.Clamp(activeModelIndex.Value, 0, ownedModels.Length - 1);
            return ownedModels[idx];
        }
    }

    private Transform cameraTransform;

    public CinemachineFreeLook freeLookCamera;
    public CinemachineFreeLook aimingCamera;

    private MyInputSystem input;
    [HideInInspector] public Vector2 moveInput;
    [HideInInspector] public bool isSprint;
    [HideInInspector] public bool isAiming;
    [HideInInspector] public bool isJumping;
    [HideInInspector] public bool isFire;

    public Transform AimTarget;
    public float maxRayDistance = 1000;
    public LayerMask aimLayerMask = ~0;

    private CinemachineImpulseSource impulseSource;

    [Tooltip("转向速度")]
    public float rotationSpeed = 300;

    [HideInInspector] public Vector3 localMovement;
    [HideInInspector] public Vector3 worldMovement;

    /// <summary>上一帧激活模型的世界坐标，用于增量同步根节点位置</summary>
    private Vector3 lastModelWorldPos;

    private void Awake()
    {
        input = new MyInputSystem();

        // ★ 尽早将 OwnerController 注入到每个模型
        foreach (var model in ownedModels)
        {
            if (model != null)
                model.OwnerController = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();


        // ★ 再次注入（确保覆盖，尤其在预制体实例化时）
        foreach (var model in ownedModels)
        {
            if (model != null)
                model.OwnerController = this;
        }

        // ★ 注册网络变量回调（所有人执行）
        syncAnimState.OnValueChanged += OnAnimStateChanged;
        syncAimTargetPos.OnValueChanged += OnAimTargetPosChanged;
        syncAimingX.OnValueChanged += OnAimingBlendChanged;
        syncAimingY.OnValueChanged += OnAimingBlendChanged;

        // ★ 只有本地玩家才激活输入和相机
        if (!IsOwner)
        {
            // 远程玩家：NetworkTransform 驱动位置，禁用 PlayerModel/NavMeshAgent 避免冲突
            SetupNonOwnerModel();
            enabled = false;
            return;
        }

        // 以下代码仅本地玩家执行

        freeLookCamera = GameManager.INSTANCE.freeLookCamera;
        aimingCamera = GameManager.INSTANCE.aimingCamera;
        cameraTransform = Camera.main.transform;
        ExitAim();
        impulseSource = aimingCamera.GetComponent<CinemachineImpulseSource>();

        // ★ 本地玩家生成后立即锁定鼠标（大厅阶段由 Lobby UI 面板负责解锁）
        Cursor.lockState = CursorLockMode.Locked;

        // 记录初始位置，用于 LateUpdate 增量同步根节点
        if (currentPlayerModel != null)
            lastModelWorldPos = currentPlayerModel.transform.position;

        // 初始化当前模型（Owner 模式）
        currentPlayerModel?.Enter();
        ResetCameraTarget();
    }

    /// <summary>
    /// 非 Owner 玩家的模型初始化：禁用 PlayerModel + NavMeshAgent。
    /// 位置由 NetworkTransform 驱动，动画由 NetworkAnimator 同步，
    /// 状态机和 NavMeshAgent 会与 NetworkTransform 冲突，必须禁用。
    /// </summary>
    private void SetupNonOwnerModel()
    {
        if (ownedModels == null) return;

        // NetworkAnimator 同步动画参数
        if (networkAnimators != null)
        {
            foreach (var na in networkAnimators)
                if (na != null) na.enabled = true;
        }

        // 禁用所有模型的 MonoBehaviour 和 NavMeshAgent，
        // 防止状态机（NavMeshAgent 追踪）与 NetworkTransform 冲突
        // ★ 同时重置本地旋转，避免预制体自带旋转导致最终朝向偏差
        foreach (var model in ownedModels)
        {
            if (model != null)
            {
                model.navMeshAgent.enabled = false;
                model.enabled = false;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
            }
        }

        // ★ 主动应用当前动画状态 + IK 约束权重（NetworkVariable 初始值回调可能不触发）
        ApplyAnimStateToAllModels(syncAnimState.Value.ToString(), 0f);

        // ★ 主动应用 AimTarget 初始位置（回调可能不触发）
        AimTarget.position = syncAimTargetPos.Value;

        // ★ 主动应用 BlendTree 参数初始值
        foreach (var model in ownedModels)
        {
            if (model != null && model.animator != null)
            {
                model.animator.SetFloat(AimingXHash, syncAimingX.Value);
                model.animator.SetFloat(AimingYHash, syncAimingY.Value);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // 清理模型引用
        foreach (var model in ownedModels)
        {
            if (model != null)
                model.OwnerController = null;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // ★ ESC 菜单打开时跳过所有输入，防止菜单背后角色仍能移动
        if (InGameMenuPanel.IsOpen) return;

        moveInput = input.Player.Move.ReadValue<Vector2>().normalized;
        isSprint = input.Player.IsSprint.IsPressed();
        isAiming = input.Player.IsAiming.IsPressed();
        isJumping = input.Player.IsJumping.triggered;
        isFire = input.Player.Fire.IsPressed();

        if (currentPlayerModel == null) return;

        Vector3 cameraForwardProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z);
        worldMovement = cameraForwardProjection * moveInput.y + cameraTransform.right * moveInput.x;
        localMovement = currentPlayerModel.transform.InverseTransformVector(worldMovement);

        // 模型切换：通过 ServerRpc 请求，或直接修改 NetworkVariable（Owner 权限）
        if (input.Player.First.triggered)
            SwitchPlayerModel(0);
        else if (input.Player.Second.triggered)
            SwitchPlayerModel(1);
        else if (input.Player.Third.triggered)
            SwitchPlayerModel(2);
    }

    private void LateUpdate()
    {
        if (!IsOwner || currentPlayerModel == null) return;

        // ★ CharacterController.Move() 移动的是子对象 PlayerModel，
        // NetworkTransform 在根节点上读不到位置/旋转变化。
        // 这里把子模型的位置和旋转同步到根节点，同时补偿子模型避免二重位移/旋转。

        Vector3 modelWorldPos = currentPlayerModel.transform.position;
        Quaternion modelWorldRot = currentPlayerModel.transform.rotation;

        // 位置增量同步
        Vector3 delta = modelWorldPos - lastModelWorldPos;
        transform.position += delta;
        lastModelWorldPos = modelWorldPos;

        // 旋转直接同步
        transform.rotation = modelWorldRot;

        // 补偿：子模型归位，本地旋转归零（消除父移动/旋转的二重影响）
        currentPlayerModel.transform.position = modelWorldPos;
        currentPlayerModel.transform.localRotation = Quaternion.identity;

        // ★ 同步 AimTarget 世界坐标（供非 Owner 端 IK 约束使用）
        syncAimTargetPos.Value = AimTarget.position;

        // ★ 同步瞄准 BlendTree 参数（非 Owner 端用于 Aiming 方向动画）
        syncAimingX.Value = currentPlayerModel.animator.GetFloat(AimingXHash);
        syncAimingY.Value = currentPlayerModel.animator.GetFloat(AimingYHash);
    }

    public void SwitchPlayerModel(int index)
    {
        if (ownedModels == null || index < 0 || index >= ownedModels.Length || ownedModels[index] == null)
            return;

        if (activeModelIndex.Value == index) return; // 已是当前模型

        currentPlayerModel?.Exit();

        // ★ 通过 NetworkVariable 同步模型切换（Owner 写权限）
        activeModelIndex.Value = index;

        currentPlayerModel?.Enter();
        // 切换模型后重置位置基准，避免根节点跳变
        if (currentPlayerModel != null)
            lastModelWorldPos = currentPlayerModel.transform.position;
        ResetCameraTarget();
    }

    // ===================== 开火同步 =====================
    /// <summary>
    /// 由状态机调用，本地生成子弹 + 网络广播给其他客户端
    /// </summary>
    public void SyncFire(Vector3 targetPos)
    {
        // 本地立即生成子弹（处理伤害）
        currentPlayerModel?.weapon.Fire(targetPos, dealDamage: true);
        ShakeCamera();
        // 通知服务器广播
        FireServerRpc(targetPos);
    }

    [ServerRpc]
    private void FireServerRpc(Vector3 targetPos)
    {
        // 服务器广播给所有其他客户端
        FireClientRpc(targetPos);
    }

    [ClientRpc]
    private void FireClientRpc(Vector3 targetPos)
    {
        if (IsOwner) return; // Owner 已在 SyncFire 中生成了
        // 远程客户端生成纯视觉效果（不处理伤害）
        currentPlayerModel?.weapon.Fire(targetPos, dealDamage: false);
    }

    public void EnterAim()
    {
        aimingCamera.m_XAxis.Value = freeLookCamera.m_XAxis.Value;
        aimingCamera.m_YAxis.Value = freeLookCamera.m_YAxis.Value;

        currentPlayerModel?.EnterAim();

        freeLookCamera.Priority = 0;
        aimingCamera.Priority = 100;
    }

    public void ExitAim()
    {
        freeLookCamera.m_XAxis.Value = aimingCamera.m_XAxis.Value;
        freeLookCamera.m_YAxis.Value = aimingCamera.m_YAxis.Value;

        currentPlayerModel?.ExitAim();

        freeLookCamera.Priority = 100;
        aimingCamera.Priority = 0;
    }

    public void ResetCameraTarget()
    {
        if (currentPlayerModel == null) return;

        aimingCamera.Follow = currentPlayerModel.transform;
        aimingCamera.LookAt = currentPlayerModel.transform;
        freeLookCamera.Follow = currentPlayerModel.transform;
        freeLookCamera.LookAt = currentPlayerModel.transform;
    }

    public void ShakeCamera()
    {
        impulseSource.GenerateImpulse();
    }

    /// <summary>
    /// Owner 端调用，将动画状态名写入 NetworkVariable 同步给所有客户端
    /// </summary>
    public void SyncAnimState(string stateName)
    {
        if (!IsOwner) return;
        syncAnimState.Value = stateName;
    }

    /// <summary>
    /// NetworkVariable 回调：非 Owner 端播放对应动画
    /// </summary>
    private void OnAnimStateChanged(FixedString32Bytes oldState, FixedString32Bytes newState)
    {
        if (IsOwner) return;
        string stateName = newState.ToString();
        ApplyAnimStateToAllModels(stateName, 0.25f);
    }



    /// <summary>
    /// NetworkVariable 回调：非 Owner 端更新 AimTarget 位置，保证 IK 约束指向正确目标
    /// </summary>
    private void OnAimTargetPosChanged(Vector3 oldPos, Vector3 newPos)
    {
        if (IsOwner) return;
        AimTarget.position = newPos;
    }

    /// <summary>
    /// NetworkVariable 回调：非 Owner 端更新瞄准 BlendTree 参数（AimingX/AimingY）
    /// </summary>
    private void OnAimingBlendChanged(float oldVal, float newVal)
    {
        if (IsOwner) return;
        foreach (var model in ownedModels)
        {
            if (model != null && model.animator != null)
            {
                model.animator.SetFloat(AimingXHash, syncAimingX.Value);
                model.animator.SetFloat(AimingYHash, syncAimingY.Value);
            }
        }
    }

    /// <summary>
    /// 给所有模型播放动画并调整 IK 约束权重（非 Owner 端专用）
    /// </summary>
    private void ApplyAnimStateToAllModels(string stateName, float fadeTime)
    {
        foreach (var model in ownedModels)
        {
            if (model == null || model.animator == null) continue;

            // ★ 非 Owner 端用极小过渡时间（0.08s），既不突兀又不过度拖沓
            model.animator.CrossFadeInFixedTime(stateName, 0.08f);

            // IK 约束权重同步：Aiming 状态启用身体/手部瞄准约束
            if (stateName == "Aiming")
                model.EnterAim();
            else
                model.ExitAim();
        }
    }

    private void OnEnable()
    {
        input.Enable();
    }

    private void OnDisable()
    {
        input.Disable();
    }
}
