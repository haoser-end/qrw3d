using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState
{
    Idle, Move, Attack, Dead
}
public abstract class EnemyBase : MonoBehaviour, IStateMachineOwner
{
    [HideInInspector]
    public Animator animator;
    protected StateMachine stateMachine;

    [HideInInspector]
    public NavMeshAgent navMeshAgent;
    public float rotationSpeed = 300;
    public float minAttackDistance = 1f;
    public PlayerModel attackTarget;

    public GameObject bloodSmashPrefab;
    public GameObject bloodDrippingPrefab;

    protected int hitHash;
    protected int moveSpeedHash;
    protected float normalMoveSpeed = 1;
    protected float slowMoveSpeed = 0.5f;
    protected Coroutine recoverSpeedCoroutine;

    public int health = 100;
    private float currentHealth;
    private bool isDead = false;
    public GameObject healthBarPrefab;
    public Transform healthBarPos;
    [HideInInspector]
    public GameObject healthBar;
    public float healthBarShowTime = 6;
    private float healthBar_timer;
    protected virtual void Awake()
    {
        stateMachine = new StateMachine(this);
        animator = GetComponent<Animator>();
        navMeshAgent = GetComponent<NavMeshAgent>();
        navMeshAgent.stoppingDistance = minAttackDistance;
        navMeshAgent.angularSpeed = rotationSpeed;
        hitHash = Animator.StringToHash("Hit");
        moveSpeedHash = Animator.StringToHash("MoveSpeed");
        currentHealth = health;
        healthBar_timer = healthBarShowTime;
    }
    protected virtual void Start()
    {
        SwitchState(EnemyState.Idle);
        FindAttackTarget();
        healthBar = Instantiate(healthBarPrefab, healthBarPos.position, Quaternion.identity);
        healthBar.transform.SetParent(UIManager.INSTANCE.WorldSpaceCanvas.transform);
    }
    protected virtual void Update()
    {
        if (healthBar_timer < healthBarShowTime)
        {
            if (isDead) return;
            healthBar.SetActive(true);
            healthBar.transform.position = healthBarPos.transform.position;
            healthBar_timer += Time.deltaTime;
        }
        else
        {
            if (healthBar)
            {
                healthBar.SetActive(false);
            }
        }
    }

    public virtual void FindAttackTarget()
    {
        attackTarget = null;
        float minDistance = float.MaxValue;

        foreach (PlayerModel playerModel in PlayerModel.AllModels)
        {
            if (playerModel != null)
            {
                float distance = Vector3.Distance(transform.position, playerModel.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    attackTarget = playerModel;
                }
            }
        }
    }
    /// <summary>
    /// 减慢移动动画
    /// </summary>
    protected virtual void SlowMoveAnimation()
    {
        animator.SetFloat(moveSpeedHash, slowMoveSpeed);
        if (recoverSpeedCoroutine != null)
        {
            StopCoroutine(recoverSpeedCoroutine);
        }
        recoverSpeedCoroutine = StartCoroutine(RecoverMoveSpeed(0.5f));
    }
    protected IEnumerator RecoverMoveSpeed(float delay)
    {
        yield return new WaitForSeconds(delay);

        animator.SetFloat(moveSpeedHash, normalMoveSpeed);
        recoverSpeedCoroutine = null;

    }
    /// <summary>
    /// 受击
    /// </summary>
    /// <param name="bullet"></param>
    /// <param name="damageMultiplier"></param>
    public virtual void Hurt(PlayerWeaponBullet bullet, float damageMultiplier = 1)
    {
        animator.SetTrigger(hitHash);
        SlowMoveAnimation();
        Vector3 bulletDir = bullet.transform.forward;
        Quaternion rotation = Quaternion.LookRotation(-bulletDir);
        Destroy(Instantiate(bloodSmashPrefab, bullet.transform.position, rotation), 3);
        Destroy(Instantiate(bloodDrippingPrefab, transform.position + Vector3.up * 0.1f, Quaternion.Euler(0, 0, 0)), 3);

        currentHealth -= bullet.damage * damageMultiplier;
        if (currentHealth > 0)
        {
            healthBar_timer = 0;
            healthBar.GetComponent<EnemyHealthBar>().UpdateHealthBar(currentHealth / health);
        }
        else
        {
            SwitchState(EnemyState.Dead);
            navMeshAgent.enabled = false;
            GetComponent<BoxCollider>().enabled = false;
            currentHealth = 0;
            isDead = true;
            Destroy(healthBar);
        }
    }

    public virtual bool HasAttackTarget()
    {
        return attackTarget != null;
    }

    public virtual bool IsAtttackTargetInAttackRange()
    {
        if (HasAttackTarget()) return Vector3.Distance(transform.position, attackTarget.transform.position) < minAttackDistance;
        return false;
    }

    public virtual void ChaseTarget()
    {
        if (HasAttackTarget())
        {
            navMeshAgent.SetDestination(attackTarget.transform.position);
        }
    }
    public abstract void SwitchState(EnemyState state);
    public void PlayerStateAnimation(string animationName, float transition = 0.25f, int layer = 0)
    {
        animator.CrossFadeInFixedTime(animationName, transition, layer);
    }
    public void Clear()
    {
        stateMachine.Stop();
       Destroy(gameObject);
    }

}
