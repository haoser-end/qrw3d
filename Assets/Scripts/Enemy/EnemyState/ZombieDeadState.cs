using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombieDeadState : EnemyStateBase
{
    public override void Enter()
    {
        base.Enter();
        enemyModel.PlayerStateAnimation("Dead");
    }
    public override void Update()
    {
        base.Update();
        if (IsAnimationBreak(0))
        {
            enemyModel.Clear();
        }
    }
}
