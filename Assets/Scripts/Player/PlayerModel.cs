using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

public enum PlayerState
{
    Idle,
    Move,
    Hover,
    Aiming,
}

public class PlayerModel : MonoBehaviour, IStateMachineOwner
{
    public PlayerWeapon weapon;
    [HideInInspector] public Animator animator;
    public CharacterController cc;
    private StateMachine stateMachine;
    private PlayerState currentState;

    public TwoBoneIKConstraint rightHandContraint;
    public MultiAimConstraint rightHandAimContraint;
    public MultiAimConstraint bodyAimContraint;

    public float gravity = -15;
    public float jumpHeight = 1.5f;
    [HideInInspector] public float verticalSpeed;
    public float fallHeight = 0.2f;

    private static readonly int CACHE_SIZE = 3;
    Vector3[] speedCache = new Vector3[CACHE_SIZE];
    private int speedCache_index = 0;
    private Vector3 averageDeltaMovement;

    [HideInInspector] public NavMeshAgent navMeshAgent;
    public float stoppingDistance = 2f;

    /// <summary>所属 PlayerController 引用，由 PlayerController 在初始化时注入</summary>
    public PlayerController OwnerController { get; set; }

    // ★ 静态注册表，供 EnemyBase 等查找所有玩家模型
    private static readonly List<PlayerModel> _allModels = new List<PlayerModel>();
    public static IReadOnlyList<PlayerModel> AllModels => _allModels;

    private void Awake()
    {
        stateMachine = new StateMachine(this);
        animator = GetComponent<Animator>();
        cc = GetComponent<CharacterController>();
        navMeshAgent = GetComponent<NavMeshAgent>();
        navMeshAgent.stoppingDistance = stoppingDistance;
        // ★ 不再 GetComponent<PlayerController>() — 改为外部注入 OwnerController
    }

    private void OnEnable()
    {
        _allModels.Add(this);
    }

    private void OnDisable()
    {
        _allModels.Remove(this);
    }
    // Start is called before the first frame update
    public void Init()
    {
        SwitchState(PlayerState.Idle);
        ExitAim();
    }
    
    // Update is called once per frame
    void Update()
    {

    }
    public void Enter()
    {
        navMeshAgent.enabled = false;
        if (OwnerController != null)
            navMeshAgent.angularSpeed = OwnerController.rotationSpeed;
        Init();
    }
    public void Exit()
    {
        navMeshAgent.enabled = true;
        SwitchState(PlayerState.Idle);
    }

    public void SwitchState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Idle:
                stateMachine.EnterState<PlayerIdleState>();
                break;
            case PlayerState.Move:
                stateMachine.EnterState<PlayerMoveState>();
                break;
            case PlayerState.Hover:
                stateMachine.EnterState<PlayerHoverState>();
                break;
            case PlayerState.Aiming:
                stateMachine.EnterState<PlayerAimingState>();
                break;
        }
        currentState = state;
    }
    /// <summary>
    /// 播放动画
    /// </summary>
    public void PlayerStateAnimation(string animationName, float transition = 0.25f, int layer = 0)
    {
        animator.CrossFadeInFixedTime(animationName, transition, layer);
        // 同步动画状态给其他客户端
        OwnerController?.SyncAnimState(animationName);
    }
    public bool IsHover()
    {
        return !Physics.Raycast(transform.position, Vector3.down, fallHeight);
    }
    private void UpdateAverageCecheSpeed(Vector3 newSpeed)
    {
        speedCache[speedCache_index++] = newSpeed;
        speedCache_index %= CACHE_SIZE;
        Vector3 sum = Vector3.zero;
        foreach (Vector3 cache in speedCache)
        {
            sum += cache;
        }
        averageDeltaMovement = sum / CACHE_SIZE;
    }
    private void OnAnimatorMove()
    {
        Vector3 playerDeltaMovement = animator.deltaPosition;
        if (currentState != PlayerState.Hover)
        {
            UpdateAverageCecheSpeed(animator.velocity);
        }
        else
        {
            playerDeltaMovement = averageDeltaMovement * Time.deltaTime;
        }
        playerDeltaMovement.y = verticalSpeed * Time.deltaTime;
        cc.Move(playerDeltaMovement);
    }

    public void EnterAim()
    {

        rightHandAimContraint.weight = 1;
        bodyAimContraint.weight = 1;
        rightHandContraint.weight = 0;
    }

    public void ExitAim()
    {
        rightHandAimContraint.weight = 0;
        bodyAimContraint.weight = 0;
        rightHandContraint.weight = 1;
    }
    /// <summary>
    /// 返回与当前控制角色的距离。
    /// </summary>
    public float DistanceCurrentPlayerModel()
    {
        if (OwnerController != null && OwnerController.currentPlayerModel != null)
            return Vector3.Distance(transform.position, OwnerController.currentPlayerModel.transform.position);
        return 0f;
    }
}
