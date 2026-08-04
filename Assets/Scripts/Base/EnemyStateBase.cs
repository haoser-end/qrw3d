using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyStateBase : StateBase
{
    protected EnemyBase enemyModel;
    public override void Destory()
    {
    }

    public override void Enter()
    {
        MonoManager.INSTANCE.AddUpdateAction(Update);
    }

    public override void Exit()
    {
        MonoManager.INSTANCE.RemoveUpdateAction(Update);
    }

    public override void Init(IStateMachineOwner owner)
    {
        enemyModel = (EnemyBase)owner;
    }

    public override void Update()
    {
    }
    protected bool IsAnimationBreak(int layer)
    {
        AnimatorStateInfo info = enemyModel.animator.GetCurrentAnimatorStateInfo(layer);
        return info.normalizedTime >= 1.0f && !enemyModel.animator.IsInTransition(layer);
    }


}
