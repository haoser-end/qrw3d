using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 客户端权威的 NetworkTransform。
/// 重写 OnIsServerAuthoritative() 返回 false，
/// 让每个客户端（Owner）有权将自己的位置同步给服务端和其他客户端。
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
