using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonoManager : SingleMonoBase<MonoManager>
{
    private Action updateAction;
    public void AddUpdateAction(Action task)
    {
        updateAction += task;
    }
    public void RemoveUpdateAction(Action task)
    {
        updateAction -= task;
    }

    // Update is called once per frame
    void Update()
    {
        updateAction?.Invoke();
    }
}
