//using Unity.Netcode;
//using Unity.Netcode.Transports.UTP;
//using UnityEngine;
//using UnityEngine.UI;

///// <summary>
///// 网络连接 UI — 支持 Host / Client / Server 三种模式
///// 包含玩家名、IP 地址、密码输入，状态提示
///// </summary>
//public class NetworkManagerUI : MonoBehaviour
//{
//    // === UI 按钮 ===
//    [Header("按钮")]
//    [SerializeField] private Button hostButton;
//    [SerializeField] private Button clientButton;
//    [SerializeField] private Button serverButton;
//    [SerializeField] private Button cancelButton;

//    // === UI 输入 ===
//    [Header("输入框")]
//    [SerializeField] private InputField playerNameInput;
//    [SerializeField] private InputField ipAddressInput;
//    [SerializeField] private InputField passwordInput;

//    // === UI 面板 ===
//    [Header("面板")]
//    [SerializeField] private GameObject menuPanel;
//    [SerializeField] private GameObject connectingPanel;

//    // === UI 文本 ===
//    [Header("状态")]
//    [SerializeField] private Text statusText;

//    private void Awake()
//    {
//        // 绑定按钮事件
//        hostButton.onClick.AddListener(OnHostClicked);
//        clientButton.onClick.AddListener(OnClientClicked);
//        serverButton.onClick.AddListener(OnServerClicked);

//        if (cancelButton != null)
//            cancelButton.onClick.AddListener(OnCancelClicked);

//        // 默认 IP 设为本地
//        if (ipAddressInput != null)
//            ipAddressInput.text = "127.0.0.1";

//        // 默认玩家名（给个随机默认名）
//        if (playerNameInput != null)
//            playerNameInput.text = "Player_" + Random.Range(1000, 9999);

//        ShowMenu();
//    }

//    private void Start()
//    {
//        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
//        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
//    }

//    // ========== 按钮回调 ==========
//    private void OnHostClicked()
//    {
//        if (!ValidatePlayerName()) return;

//        SetConnectionPayload();
//        ShowConnecting("正在创建房间...");
//        NetworkManager.Singleton.StartHost();
//    }

//    private void OnClientClicked()
//    {
//        if (!ValidatePlayerName()) return;

//        // 设置目标 IP
//        SetTargetAddress();

//        SetConnectionPayload();
//        string ip = GetIPAddress();
//        ShowConnecting($"正在连接 {ip}...");
//        NetworkManager.Singleton.StartClient();
//    }

//    private void OnServerClicked()
//    {
//        ShowConnecting("正在启动专用服务器...");
//        NetworkManager.Singleton.StartServer();
//    }

//    private void OnCancelClicked()
//    {
//        if (NetworkManager.Singleton.IsListening)
//        {
//            NetworkManager.Singleton.Shutdown();
//        }
//        ShowMenu();
//    }

//    // ========== 连接负载 ==========
//    private void SetConnectionPayload()
//    {
//        var payload = new ConnectionPayload
//        {
//            playerName = GetPlayerName(),
//            gameVersion = "1.0.0",
//            password = GetPassword()
//        };
//        string json = JsonUtility.ToJson(payload);
//        NetworkManager.Singleton.NetworkConfig.ConnectionData =
//            System.Text.Encoding.UTF8.GetBytes(json);
//    }

//    private void SetTargetAddress()
//    {
//        string ip = GetIPAddress();
//        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
//        if (transport != null)
//        {
//            transport.ConnectionData.Address = ip;
//        }
//    }

//    // ========== 网络回调 ==========
//    private void OnClientConnected(ulong clientId)
//    {
//        if (clientId == NetworkManager.Singleton.LocalClientId)
//        {
//            Debug.Log($"[NetworkManagerUI] 已连接！ClientId: {clientId}");
//            gameObject.SetActive(false); // 隐藏连接 UI
//        }
//    }

//    private void OnClientDisconnected(ulong clientId)
//    {
//        if (clientId == NetworkManager.Singleton.LocalClientId)
//        {
//            Debug.Log("[NetworkManagerUI] 连接断开");
//            ShowMenu();
//        }
//    }

//    // ========== 输入校验 ==========
//    private bool ValidatePlayerName()
//    {
//        if (string.IsNullOrWhiteSpace(GetPlayerName()))
//        {
//            UpdateStatus("请输入玩家名称");
//            return false;
//        }
//        return true;
//    }

//    // ========== UI 状态切换 ==========
//    private void ShowMenu()
//    {
//        if (menuPanel != null) menuPanel.SetActive(true);
//        if (connectingPanel != null) connectingPanel.SetActive(false);
//        UpdateStatus("请输入信息并选择模式");
//    }

//    private void ShowConnecting(string msg)
//    {
//        if (menuPanel != null) menuPanel.SetActive(false);
//        if (connectingPanel != null) connectingPanel.SetActive(true);
//        UpdateStatus(msg);
//    }

//    private void UpdateStatus(string msg)
//    {
//        if (statusText != null) statusText.text = msg;
//        Debug.Log($"[NetworkManagerUI] {msg}");
//    }

//    // ========== 输入获取 ==========
//    private string GetPlayerName()
//    {
//        return playerNameInput != null ? playerNameInput.text.Trim() : "";
//    }

//    private string GetIPAddress()
//    {
//        return ipAddressInput != null ? ipAddressInput.text.Trim() : "127.0.0.1";
//    }

//    private string GetPassword()
//    {
//        return passwordInput != null ? passwordInput.text : "";
//    }

//    // ========== 销毁 ==========
//    private void OnDestroy()
//    {
//        if (NetworkManager.Singleton != null)
//        {
//            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
//            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
//        }
//    }
//}
