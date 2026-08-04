using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 游戏内 ESC 菜单面板。
/// 支持暂停/继续、断开连接返回大厅。
/// 
/// UI 结构（需要在 Unity 中搭建）：
///   InGameMenuPanel (Canvas, ScreenSpace-Overlay)
///   ├─ Background        (半透明黑色遮罩)
///   ├─ MenuPanel         (居中面板容器)
///   │    ├─ TitleText    ("暂停")
///   │    ├─ ResumeBtn    (继续游戏)
///   │    ├─ SettingsBtn  (设置 - 预留)
///   │    └─ QuitBtn      (返回大厅)
///   └─ (其他子节点保持隐藏)
/// 
/// 使用方式：
///   1. 在 GameManager 或持久化 Canvas 下挂此脚本
///   2. 拖入对应 UI 组件引用
///   3. 游戏开始后按 ESC 呼出
/// </summary>
public class InGameMenuPanel : MonoBehaviour
{
    // ===================== 序列化字段 =====================
    [Header("面板")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject background;

    [Header("按钮")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button quitButton;

    /// <summary>全局状态：菜单是否打开，PlayerController 等可据此屏蔽输入</summary>
    public static bool IsOpen { get; private set; }

    // ===================== 状态 =====================
    private NetworkManagerUI networkUI; // 断开后显示连接界面

    // ===================== Unity 生命周期 =====================
    private void Awake()
    {
        // 初始隐藏
        if (menuPanel != null) menuPanel.SetActive(false);
        if (background != null) background.SetActive(false);

        // 绑定按钮
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResume);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuit);
    }

    private void Start()
    {
        networkUI = FindObjectOfType<NetworkManagerUI>(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Toggle();
    }

    private void OnDestroy()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(OnResume);
        if (quitButton != null) quitButton.onClick.RemoveListener(OnQuit);
    }

    // ===================== 核心逻辑 =====================
    public void Toggle()
    {
        SetPanelState(!IsOpen);
    }

    public void OnResume()
    {
        SetPanelState(false);
    }

    public void OnQuit()
    {
        // 恢复时间、解锁鼠标
        SetPanelState(false);

        // 断开网络 → 返回大厅
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 如果有 NetworkManagerUI，显示它的菜单面板
        if (networkUI != null)
        {
            networkUI.gameObject.SetActive(true);
            // 取决于 NetworkManagerUI 是否监听 OnClientDisconnectCallback 自动切 Menu
        }

        // 如果没有联网（单机），直接回到主菜单场景
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            LoadMainMenu();
        }
    }

    private void SetPanelState(bool open)
    {
        IsOpen = open;

        if (menuPanel != null) menuPanel.SetActive(open);
        if (background != null) background.SetActive(open);

        // 锁定 / 释放鼠标
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;

    }

    private void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenu"); // 你的主菜单场景名，按实际改
    }
}
