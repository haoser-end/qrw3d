using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerIdleState : PlayerStateBase
{
    public override void Enter()
    {
        base.Enter();
        playerModel.PlayerStateAnimation("Idle");
    }
    public override void Update()
    {
        base.Update();
        if (IsBeControl())
        {
            if (playerController.moveInput.magnitude != 0)
            {
                playerModel.SwitchState(PlayerState.Move);
            }
            if (playerController.isJumping)
            {
                SwitchToHover();
            }
        }
        else
        {
            if(playerModel.DistanceCurrentPlayerModel()>playerModel.stoppingDistance)
            {
                playerModel.SwitchState(PlayerState.Move);
            }
        }
    }
}
