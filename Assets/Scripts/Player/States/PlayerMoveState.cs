using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveState : PlayerStateBase
{
    private int moveBlendHash;
    private float moveBlend;
    private float runThreshold = 0;
    private float sprintThreshold = 1;
    private float transitionSpeed = 5;
    public override void Init(IStateMachineOwner owner)
    {
        base.Init(owner);
        moveBlendHash = Animator.StringToHash("MoveBlend");
    }
    public override void Enter()
    {
        base.Enter();
        playerModel.PlayerStateAnimation("Move");
    }
    public override void Update()
    {
        base.Update();
        if (IsBeControl())
        {
            if (playerController.isJumping)
            {
                SwitchToHover();
                return;
            }

            if (playerController.moveInput.magnitude == 0)
            {
                playerModel.SwitchState(PlayerState.Idle);
                return;
            }
            if (playerController.isSprint)
            {
                moveBlend = Mathf.Lerp(moveBlend, sprintThreshold, transitionSpeed * Time.deltaTime);
            }
            else
            {
                moveBlend = Mathf.Lerp(moveBlend, runThreshold, transitionSpeed * Time.deltaTime);
            }
            playerModel.animator.SetFloat(moveBlendHash, moveBlend);


            float rad = Mathf.Atan2(playerController.localMovement.x, playerController.localMovement.z);
            playerModel.transform.Rotate(0, rad * playerController.rotationSpeed * Time.deltaTime, 0);


        }
        else
        {
            if (playerModel.DistanceCurrentPlayerModel() - playerModel.stoppingDistance < 2f)
            {
                moveBlend = Mathf.Lerp(moveBlend, runThreshold, transitionSpeed * Time.deltaTime);
            }
            else
            {
                moveBlend =Mathf.Lerp(moveBlend,sprintThreshold, transitionSpeed * Time.deltaTime);
            }
            playerModel.animator.SetFloat(moveBlendHash, moveBlend);


            if (playerModel.DistanceCurrentPlayerModel() <= playerModel.stoppingDistance)
            {
                playerModel.SwitchState(PlayerState.Idle);
                return;
            }
            playerModel.navMeshAgent.SetDestination(playerController.currentPlayerModel.transform.position);
        }
    }
}
