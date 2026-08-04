using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class StateBase 
{
    public abstract void Init(IStateMachineOwner owner);
    public abstract void Enter();
    public abstract void Exit();
    public abstract void Destory();
    public abstract void Update();
}
