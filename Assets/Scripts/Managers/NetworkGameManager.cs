using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 游戏级网络管理器 — 挂载到 NetworkManager 的 GameObject 上
/// 负责连接审批、玩家追踪、游戏状态管理
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    // === 连接审批设置 ===
    [Header("连接审批")]
    [SerializeField] private bool requirePassword;
    [SerializeField] private string gamePassword = "";
    [SerializeField] private string gameVersion = "1.0.0";

    // === Debug ===
    [Header("Debug")]
    [SerializeField] private bool verboseDebug = true;

    // === 游戏网络状态 ===
    public enum GameNetworkState { Offline, Lobby, Playing, GameOver }
    public GameNetworkState CurrentState { get; private set; } = GameNetworkState.Offline;

    // === 玩家追踪 ===
    public int ConnectedPlayerCount { get; private set; }
    private Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
    private Dictionary<ulong, string> pendingPlayerNames = new Dictionary<ulong, string>();

    // === 事件（其他系统订阅） ===
    public event Action<ulong, string> OnPlayerJoined;
    public event Action<ulong> OnPlayerLeft;
    public event Action<GameNetworkState> OnGameStateChanged;

    private NetworkManager networkManager;

    // ========== 生命周期 ==========
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("[NetworkGameManager] NetworkManager.Singleton 不存在！");
            return;
        }

        // Domain Reload 关闭时，上一次运行的网络状态可能残留
        if (networkManager.IsListening)
        {
            Debug.LogWarning("[NetworkGameManager] ⚠️ 检测到残留的网络连接，正在关闭...");
            Debug.LogWarning("  如果你看到这条消息，请检查: Edit → Project Settings → Editor → Reload Domain 是否勾选");
            networkManager.Shutdown();
        }

        DbgLog($"NetworkGameManager 初始化, PlayerPrefab={networkManager.NetworkConfig.PlayerPrefab?.name ?? "NULL"}");

        // 连接审批（密码 / 版本校验）
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApprovalCheck;

        // 客户端事件
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;

        // 服务器事件
        networkManager.OnServerStarted += OnServerStarted;
        networkManager.OnServerStopped += OnServerStopped;

        // 传输异常
        networkManager.OnTransportFailure += OnTransportFailure;
    }

    // ========== 连接审批 ==========
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        ConnectionPayload data = JsonUtility.FromJson<ConnectionPayload>(payload);

        DbgLog($"=== 审批连接请求 ===");
        DbgLog($"  ClientNetworkId: {request.ClientNetworkId}");
        DbgLog($"  PlayerName: {data.playerName}");
        DbgLog($"  GameVersion: {data.gameVersion}");
        DbgLog($"  HasPassword: {!string.IsNullOrEmpty(data.password)}");

        // 版本检查
        if (data.gameVersion != gameVersion)
        {
            response.Approved = false;
            response.Reason = $"版本不匹配 (服务器: {gameVersion}, 客户端: {data.gameVersion})";
            DbgLog($"  ❌ 拒绝: {response.Reason}");
            return;
        }

        // 密码检查
        if (requirePassword && data.password != gamePassword)
        {
            response.Approved = false;
            response.Reason = "密码错误";
            DbgLog($"  ❌ 拒绝: {response.Reason}");
            return;
        }

        // 缓存玩家名，OnClientConnected 时使用
        pendingPlayerNames[request.ClientNetworkId] = data.playerName;

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;

        DbgLog($"  ✅ 审批通过，CreatePlayerObject=true");
    }

    // ========== 服务器回调 ==========
    private void OnServerStarted()
    {
        DbgLog($"=== 服务器已启动 (IsHost={networkManager.IsHost}) ===");
        SetState(GameNetworkState.Lobby);
    }

    private void OnServerStopped(bool wasHost)
    {
        DbgLog($"=== 服务器已停止 (wasHost: {wasHost}) ===");
        ConnectedPlayerCount = 0;
        playerNames.Clear();
        pendingPlayerNames.Clear();
        SetState(GameNetworkState.Offline);
    }

    // ========== 客户端回调 ==========
    private void OnClientConnected(ulong clientId)
    {
        DbgLog($"=== OnClientConnected: clientId={clientId} ===");

        if (networkManager.IsServer)
        {
            ConnectedPlayerCount++;
            DbgLog($"  服务器端: 当前在线 {ConnectedPlayerCount} 人");

            string playerName = pendingPlayerNames.TryGetValue(clientId, out var n) ? n : $"Player_{clientId}";
            pendingPlayerNames.Remove(clientId);
            playerNames[clientId] = playerName;
            OnPlayerJoined?.Invoke(clientId, playerName);
        }

        if (clientId == networkManager.LocalClientId)
        {
            DbgLog($"  ✅ 本地客户端已连接 [{clientId}]");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        DbgLog($"=== OnClientDisconnected: clientId={clientId} ===");

        if (clientId == networkManager.LocalClientId)
        {
            DbgLog("  本地客户端断开连接");
            SetState(GameNetworkState.Offline);
            return;
        }

        if (networkManager.IsServer)
        {
            ConnectedPlayerCount--;
            string name = playerNames.TryGetValue(clientId, out var n) ? n : "Unknown";
            playerNames.Remove(clientId);
            OnPlayerLeft?.Invoke(clientId);
            DbgLog($"  服务器端: {name} 断开 (当前在线: {ConnectedPlayerCount})");
        }
    }

    private void OnTransportFailure()
    {
        DbgLog("!!! OnTransportFailure - 传输层错误 !!!");
        SetState(GameNetworkState.Offline);
    }

    // ========== 状态管理 ==========
    private void SetState(GameNetworkState newState)
    {
        if (CurrentState == newState) return;
        var previous = CurrentState;
        CurrentState = newState;
        OnGameStateChanged?.Invoke(newState);
        DbgLog($"状态变更: {previous} → {newState}");
    }

    // ========== Debug ==========
    private void DbgLog(string msg)
    {
        if (!verboseDebug) return;
        Debug.Log($"[NetworkGameManager-DEBUG] {msg}");
    }

    // ========== 公开方法 ==========
    public void BroadcastStartGame()
    {
        if (!networkManager.IsServer)
        {
            Debug.LogWarning("[NetworkGameManager] 只有服务器可以开始游戏！");
            return;
        }
        SetState(GameNetworkState.Playing);
    }

    public string GetPlayerName(ulong clientId)
    {
        return playerNames.TryGetValue(clientId, out var name) ? name : $"Player_{clientId}";
    }

    // ========== 销毁 ==========
    public  void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.ConnectionApprovalCallback = null;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnServerStopped -= OnServerStopped;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }

        if (Instance == this) Instance = null;
    }
}

// ========== 连接负载数据结构 ==========
[Serializable]
public class ConnectionPayload
{
    public string playerName;
    public string gameVersion;
    public string password;
}
