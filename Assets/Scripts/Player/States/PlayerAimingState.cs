using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAimingState : PlayerStateBase
{
    private int aimingXHash;
    private int aimingYHash;
    private float aimingX = 0;
    private float aimingY = 0;
    private float transitionSpeed = 5;
    public override void Init(IStateMachineOwner owner)
    {
        base.Init(owner);
        aimingXHash = Animator.StringToHash("AimingX");
        aimingYHash = Animator.StringToHash("AimingY");
    }
    public override void Enter()
    {
        base.Enter();
        playerModel.PlayerStateAnimation("Aiming");
        if (IsBeControl())
        {
            UpdateAimingTarget();
            playerController.EnterAim();
        }
    }
    public override void Update()
    {
        base.Update();
        if (IsBeControl())
        {
            playerModel.transform.rotation = Quaternion.Euler(0, Camera.main.transform.rotation.eulerAngles.y, 0);
            UpdateAimingTarget();

            if (!playerController.isAiming&& !playerController.isFire)
            {
                playerModel.SwitchState(PlayerState.Idle);
                return;
            }

            if (playerController.isFire)
            {
                playerController.SyncFire(playerController.AimTarget.position);
            }

            aimingX = Mathf.Lerp(aimingX, playerController.moveInput.x, transitionSpeed * Time.deltaTime);
            aimingY = Mathf.Lerp(aimingY, playerController.moveInput.y, transitionSpeed * Time.deltaTime);
            playerModel.animator.SetFloat(aimingXHash, aimingX);
            playerModel.animator.SetFloat(aimingYHash, aimingY);
        }
    }
    public override void Exit()
    {
        base.Exit();
        if (IsBeControl())
        {
            playerController.ExitAim();
        }
    }
    private void UpdateAimingTarget()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;
        if(Physics.Raycast(ray,out hit, playerController.maxRayDistance, playerController.aimLayerMask))
        {
            playerController.AimTarget.position = hit.point;
        } else
        {
            playerController.AimTarget.position= ray.origin+ray.direction*playerController.maxRayDistance;
        }
    }
}
