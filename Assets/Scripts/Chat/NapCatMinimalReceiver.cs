using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

// ============================================================
//  NapCat OneBot v11 最小消息接收器
//  零依赖，挂载即用。在 Inspector 中绑定回调即可。
//
//  使用方法：
//    1. 将本脚本和 UnityMainThreadDispatcher 挂到场景中
//    2. 确保 NapCat 的 WebSocket 已启动 (默认 ws://localhost:3000/onebot/v11/ws)
//    3. 运行 —— 群消息/私聊消息会通过 UnityEvent 触发你绑定的方法
// ============================================================

/// <summary>
/// NapCat 最小消息接收器。
/// 特点：只接收消息，不发送，不依赖任何第三方包（仅 UnityEngine + System.Net.WebSockets）。
/// </summary>
public class NapCatMinimalReceiver : MonoBehaviour
{
    [Header("WebSocket 设置")]
    [Tooltip("NapCat OneBot v11 WebSocket 地址")]
    public string webSocketUrl = "ws://localhost:3000/onebot/v11/ws";
    [Tooltip("断线自动重连间隔（秒），0 表示不重连")]
    public float reconnectDelay = 5f;

    [Header("群消息回调")]
    /// <summary>参数: (群号, 发送者昵称, 消息文本)</summary>
    public UnityEvent<string, string, string> onGroupMessage;

    [Header("私聊消息回调")]
    /// <summary>参数: (对方QQ号, 发送者昵称, 消息文本)</summary>
    public UnityEvent<string, string, string> onPrivateMessage;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    private void Start() => ConnectAsync();

    private void OnDestroy() => Disconnect();

    [ContextMenu("手动连接")]
    public void ConnectAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = ConnectLoopAsync(_cts.Token);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(webSocketUrl), ct);
                Debug.Log($"[NapCatMinimal] 已连接到 {webSocketUrl}");
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NapCatMinimal] 连接失败: {ex.Message}");
            }

            if (reconnectDelay > 0f && !ct.IsCancellationRequested)
            {
                Debug.Log($"[NapCatMinimal] {reconnectDelay}s 后重连...");
                await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), ct);
            }
            else break;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64];

        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
            catch { break; }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(json);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[NapCatMinimal] 服务器关闭了连接");
                break;
            }
        }
    }

    private void ProcessMessage(string json)
    {
        // 跳过心跳
        if (json.Contains("\"meta_event_type\":\"heartbeat\"")) return;

        // 只处理消息事件
        if (!json.Contains("\"post_type\":\"message\"")) return;

        if (json.Contains("\"message_type\":\"group\""))
        {
            try
            {
                var msg = JsonUtility.FromJson<OneBotGroupMessage>(json);
                string text = ExtractText(msg.message);
                if (string.IsNullOrEmpty(text)) return;

                UnityMainThreadDispatcher.Run(() =>
                    onGroupMessage?.Invoke(msg.group_id, GetSenderName(msg.sender), text));
            }
            catch (Exception ex) { Debug.LogWarning($"[NapCatMinimal] 解析群消息失败: {ex.Message}"); }
        }
        else if (json.Contains("\"message_type\":\"private\""))
        {
            try
            {
                var msg = JsonUtility.FromJson<OneBotPrivateMessage>(json);
                string text = ExtractText(msg.message);
                if (string.IsNullOrEmpty(text)) return;

                UnityMainThreadDispatcher.Run(() =>
                    onPrivateMessage?.Invoke(msg.user_id, GetSenderName(msg.sender), text));
            }
            catch (Exception ex) { Debug.LogWarning($"[NapCatMinimal] 解析私聊消息失败: {ex.Message}"); }
        }
    }

    /// <summary>从 OneBot 消息段数组中提取纯文本（text + @ 合并）</summary>
    private string ExtractText(OneBotSegment[] segments)
    {
        if (segments == null || segments.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.type == "text" && seg.data != null && !string.IsNullOrEmpty(seg.data.text))
                sb.Append(seg.data.text);
            else if (seg.type == "at" && seg.data != null)
                sb.Append($"@{seg.data.qq} ");
        }
        return sb.ToString().Trim();
    }

    private string GetSenderName(OneBotSender sender)
    {
        if (sender == null) return "未知";
        return !string.IsNullOrEmpty(sender.nickname) ? sender.nickname : sender.user_id;
    }

    // ==================== 数据模型（仅用于 JsonUtility 解析） ====================

    [Serializable] public class OneBotGroupMessage  { public string group_id;  public OneBotSender sender;   public OneBotSegment[] message; }
    [Serializable] public class OneBotPrivateMessage { public string user_id;   public OneBotSender sender;   public OneBotSegment[] message; }
    [Serializable] public class OneBotSender         { public string user_id;   public string nickname; }
    [Serializable] public class OneBotSegment        { public string type;      public OneBotSegmentData data; }
    [Serializable] public class OneBotSegmentData    { public string text;      public string qq;              public string id; }
}

// ============================================================
//  Unity 主线程调度器 —— 将 WebSocket 回调切回主线程
//  自动创建，无需手动挂载
// ============================================================
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _queue = new Queue<Action>();
    private static readonly object _lock = new object();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("[UnityMainThreadDispatcher]");
        _instance = go.AddComponent<UnityMainThreadDispatcher>();
        DontDestroyOnLoad(go);
    }

    /// <summary>把 action 调度到主线程执行</summary>
    public static void Run(Action action)
    {
        lock (_lock) { _queue.Enqueue(action); }
    }

    private void Update()
    {
        lock (_lock)
        {
            while (_queue.Count > 0)
                _queue.Dequeue()?.Invoke();
        }
    }
}
