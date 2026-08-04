
using UnityEngine;

public class PlayerStateBase : StateBase
{
    protected PlayerController playerController;
    protected PlayerModel playerModel;
    public override void Init(IStateMachineOwner owner)
    {
        playerModel = (PlayerModel)owner;
        // ★ 通过属性注入获取 PlayerController（由 PlayerController 在初始化时设置）
        playerController = playerModel.OwnerController;
    }
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

    public override void Update()
    {
        if (!playerModel.cc.isGrounded)
        {
            playerModel.verticalSpeed += playerModel.gravity * Time.deltaTime;
            if (playerModel.IsHover())
            {
                playerModel.SwitchState(PlayerState.Hover);
            }
        }
        else
        {
            playerModel.verticalSpeed = playerModel.gravity * Time.deltaTime;
        }

        if (IsBeControl() && (playerController.isAiming || playerController.isFire))
        {
            playerModel.SwitchState(PlayerState.Aiming);
        }
    }
    public bool IsBeControl()
    {
        return playerModel == playerController.currentPlayerModel;
    }
    public void SwitchToHover()
    {
        playerModel.verticalSpeed = Mathf.Sqrt(-2 * playerModel.gravity * playerModel.jumpHeight);
        playerModel.SwitchState(PlayerState.Hover);
    }
}
