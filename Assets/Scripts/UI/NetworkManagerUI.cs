using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 网络连接 UI — 支持 Host / Client / Server 三种模式
///
/// UI 面板层级:
///   Root (NetworkManagerUI GameObject)
///   ├─ MenuPanel         [菜单面板] 玩家名/IP/密码 + Host/Client/Server 按钮
///   │    └─ StartGameButton   [开始游戏] (Host 连接后显示)
///   ├─ ConnectingPanel   [连接中面板] 状态文字 + Cancel 按钮
///   │    └─ DebugText     [调试日志] (仅 verboseDebug 时更新)
///   ├─ StatusText        [状态栏] 全局状态提示
///   └─ PlayerListText    [玩家列表] 显示在线玩家名及数量
///
/// UI 状态机:
///   Menu ──(点 Host/Client/Server)──▶ Connecting
///   Connecting ──(连上/Host)────────▶ HostLobby (menuPanel + startGameBtn)
///   Connecting ──(连上/Client)──────▶ Hidden
///   Connecting ──(取消/失败)────────▶ Menu
///   HostLobby ──(开始游戏)──────────▶ Hidden
///   HostLobby ──(断开)──────────────▶ Menu
/// </summary>
public class NetworkManagerUI : MonoBehaviour
{
    // ===================== 序列化字段 =====================
    [Header("面板")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject connectingPanel;

    [Header("按钮")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button cancelButton;

    [Header("输入框")]
    [SerializeField] private InputField playerNameInput;
    [SerializeField] private InputField ipAddressInput;
    [SerializeField] private InputField passwordInput;

    [Header("文本")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text playerListText;
    [SerializeField] private Text debugText;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = true;

    // ===================== 状态 =====================
    private enum UIState { Menu, Connecting, HostLobby, Hidden }
    private UIState currentState = UIState.Menu;
    private Coroutine connectionMonitor;
    private List<string> playerNames = new List<string>();

    // ===================== 生命周期 =====================
    private void Awake()
    {
        // 绑定按钮
        hostButton.onClick.AddListener(OnHostClicked);
        clientButton.onClick.AddListener(OnClientClicked);
        serverButton.onClick.AddListener(OnServerClicked);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);

        // 默认值
        if (ipAddressInput != null) ipAddressInput.text = "127.0.0.1";
        if (playerNameInput != null) playerNameInput.text = "Player_" + Random.Range(1000, 9999);

        SetUIState(UIState.Menu);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;

        NetworkGameManager.Instance.OnGameStateChanged += OnGameStateChanged;
        NetworkGameManager.Instance.OnPlayerJoined += OnPlayerJoined;
        NetworkGameManager.Instance.OnPlayerLeft += OnPlayerLeft;
    }

    private void OnDestroy()
    {
        StopConnectionMonitor();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.OnGameStateChanged -= OnGameStateChanged;
            NetworkGameManager.Instance.OnPlayerJoined -= OnPlayerJoined;
            NetworkGameManager.Instance.OnPlayerLeft -= OnPlayerLeft;
        }
    }

    // ===================== UI 状态机 =====================
    /// <summary>
    /// 统一管理所有面板/按钮的显隐，外部只调这一个方法
    /// </summary>
    private void SetUIState(UIState state)
    {
        currentState = state;

        bool menu = false, connecting = false, rootActive = true, startGameBtn = false;

        switch (state)
        {
            case UIState.Menu:
                menu = true;
                UpdateStatus("请输入信息并选择模式");
                break;
            case UIState.Connecting:
                connecting = true;
                break;
            case UIState.HostLobby:
                menu = true;
                startGameBtn = true;
                UpdateStatus("房间已创建 (Host模式)");
                break;
            case UIState.Hidden:
                rootActive = false;
                break;
        }

        gameObject.SetActive(rootActive);
        if (menuPanel != null) menuPanel.SetActive(menu);
        if (connectingPanel != null) connectingPanel.SetActive(connecting);
        if (startGameButton != null) startGameButton.gameObject.SetActive(startGameBtn);

        // ★ 大厅 UI 显示时解锁鼠标，游戏进行中（Hidden）锁定鼠标
        Cursor.lockState = (state == UIState.Hidden) ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = (state != UIState.Hidden);
    }

    // ===================== 按钮回调 =====================
    private void OnHostClicked()
    {
        if (!ValidatePlayerName()) return;

        SetConnectionPayload();
        SetUIState(UIState.Connecting);
        UpdateStatus("正在创建房间...");

        NetworkManager.Singleton.StartHost();
        StartConnectionMonitor();
    }

    private void OnClientClicked()
    {
        if (!ValidatePlayerName()) return;

        SetTargetAddress();
        SetConnectionPayload();

        string ip = GetIPAddress();
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        ushort port = transport != null ? transport.ConnectionData.Port : (ushort)7777;

        SetUIState(UIState.Connecting);
        UpdateStatus($"正在连接 {ip}:{port}...");

        NetworkManager.Singleton.StartClient();
        StartConnectionMonitor();
    }

    private void OnServerClicked()
    {
        SetUIState(UIState.Connecting);
        UpdateStatus("正在启动专用服务器...");

        NetworkManager.Singleton.StartServer();
        StartConnectionMonitor();
    }

    private void OnStartGameClicked()
    {
        NetworkGameManager.Instance.BroadcastStartGame();
        // SetUIState(Hidden) 由 OnGameStateChanged → Playing 触发
    }

    private void OnCancelClicked()
    {
        StopConnectionMonitor();
        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        SetUIState(UIState.Menu);
    }

    // ===================== 网络回调 =====================
    private void OnClientConnected(ulong clientId)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        Debug.Log($"[NetworkManagerUI] ✅ 已连接！ClientId: {clientId}");
        StopConnectionMonitor();

        // Host: 显示大厅（菜单 + 开始按钮），Client: 隐藏整个 UI
        SetUIState(NetworkManager.Singleton.IsHost ? UIState.HostLobby : UIState.Hidden);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        Debug.Log("[NetworkManagerUI] ❌ 连接断开");
        StopConnectionMonitor();
        SetUIState(UIState.Menu);
    }

    private void OnClientStopped(bool wasHost)
    {
        if (wasHost) return;

        Debug.Log("[NetworkManagerUI] ❌ 客户端连接失败");
        StopConnectionMonitor();
        SetUIState(UIState.Menu);
        UpdateStatus("连接失败，请检查 IP 地址和主机状态");
    }

    private void OnTransportFailure()
    {
        StopConnectionMonitor();
        SetUIState(UIState.Menu);
        UpdateStatus("传输层错误");
    }

    private void OnGameStateChanged(NetworkGameManager.GameNetworkState state)
    {
        if (state == NetworkGameManager.GameNetworkState.Playing)
            SetUIState(UIState.Hidden);
    }

    // ===================== 玩家列表 =====================
    private void OnPlayerJoined(ulong clientId, string playerName)
    {
        playerNames.Add(playerName);
        RefreshPlayerList();
    }

    private void OnPlayerLeft(ulong clientId)
    {
        string name = NetworkGameManager.Instance.GetPlayerName(clientId);
        playerNames.Remove(name);
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (playerListText == null) return;
        playerListText.text = $"玩家 ({playerNames.Count}):\n" + string.Join("\n", playerNames);
    }

    // ===================== 连接监控 =====================
    private void StartConnectionMonitor()
    {
        StopConnectionMonitor();
        connectionMonitor = StartCoroutine(ConnectionMonitorCoroutine());
    }

    private void StopConnectionMonitor()
    {
        if (connectionMonitor == null) return;
        StopCoroutine(connectionMonitor);
        connectionMonitor = null;
    }

    private IEnumerator ConnectionMonitorCoroutine()
    {
        int tick = 0;
        while (tick < 60)
        {
            yield return new WaitForSeconds(0.5f);
            tick++;

            if (!NetworkManager.Singleton.IsListening) break;

            if (NetworkManager.Singleton.IsConnectedClient)
            {
                StopConnectionMonitor();
                break;
            }
        }
    }

    // ===================== 连接负载 =====================
    private void SetConnectionPayload()
    {
        var payload = new ConnectionPayload
        {
            playerName = GetPlayerName(),
            gameVersion = "1.0.0",
            password = GetPassword()
        };
        string json = JsonUtility.ToJson(payload);
        NetworkManager.Singleton.NetworkConfig.ConnectionData =
            System.Text.Encoding.UTF8.GetBytes(json);
    }

    private void SetTargetAddress()
    {
        string ip = GetIPAddress();
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.ConnectionData.Address = ip;
    }

    // ===================== 输入辅助 =====================
    private bool ValidatePlayerName()
    {
        if (string.IsNullOrWhiteSpace(GetPlayerName()))
        {
            UpdateStatus("请输入玩家名称");
            return false;
        }
        return true;
    }

    private string GetPlayerName() =>
        playerNameInput != null ? playerNameInput.text.Trim() : "";

    private string GetIPAddress() =>
        ipAddressInput != null ? ipAddressInput.text.Trim() : "127.0.0.1";

    private string GetPassword() =>
        passwordInput != null ? passwordInput.text : "";

    // ===================== 状态文本 =====================
    private void UpdateStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log($"[NetworkManagerUI] {msg}");
    }

    private void DbgLog(string msg)
    {
        if (!verboseDebug) return;
        Debug.Log($"[NetworkManagerUI-DEBUG] {msg}");
        if (debugText != null)
        {
            debugText.text += $"\n{msg}";
            var lines = debugText.text.Split('\n');
            if (lines.Length > 20)
                debugText.text = string.Join("\n", lines, lines.Length - 20, 20);
        }
    }
}
