using Unity.Netcode;
using UnityEngine;

public class RpcTest : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer && IsOwner) //只在拥有此 NetworkBehaviour 实例的 NetworkObject 的客户端向服务器发送 RPC
        {
            TestServerRpc(0, NetworkObjectId);
        }
    }

    [ClientRpc]
    void TestClientRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"客户端接收到 RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        if (IsOwner) //只在拥有此 NetworkBehaviour 实例的 NetworkObject 的客户端向服务器发送 RPC
        {
            TestServerRpc(value + 1, sourceNetworkObjectId);
        }
    }

    [ServerRpc]
    void TestServerRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"服务器接收到 RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        TestClientRpc(value, sourceNetworkObjectId);
    }
}
