using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombieEnemy : EnemyBase
{
    public override void SwitchState(EnemyState state)
    {
        switch(state)
        {
            case EnemyState.Idle:
                stateMachine.EnterState<ZombieIdleState>();
                break;
            case EnemyState.Move:
                stateMachine.EnterState<ZombieMoveState>();
                break;
            case EnemyState.Attack:
                stateMachine.EnterState<ZombieAttackState>();
                break;
            case EnemyState.Dead:
                stateMachine.EnterState<ZombieDeadState>();
                break;
        }
    }
}
