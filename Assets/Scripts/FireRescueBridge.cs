using UnityEngine;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json.Linq;

public class FireRescueBridge : MonoBehaviour
{
    private WebSocket ws;
    [SerializeField] public string serverUrl = "ws://127.0.0.1:8765";
    [SerializeField] public WorldBuilder worldBuilder;

    async void Start()
    {
        if (worldBuilder == null)
            worldBuilder = FindObjectOfType<WorldBuilder>();

        ws = new WebSocket(serverUrl);

        ws.OnOpen  += () => Debug.Log("✅ Connected to " + serverUrl);
        ws.OnError += (e) => Debug.LogError("WebSocket Error: " + e);
        ws.OnClose += (code) => Debug.Log("🔌 Closed: " + code);

        ws.OnMessage += (bytes) =>
        {
            var json = Encoding.UTF8.GetString(bytes);
            var msg  = JObject.Parse(json);

            if ((string)msg["type"] == "world_init")
            {
                worldBuilder.BuildFromSpec((JObject)msg["world"]);
                worldBuilder.SetupActionUI((JArray)msg["candidates"]);
                worldBuilder.SetupDynamics((JObject)msg["dynamic"]);
                Debug.Log("🌍 World + UI ready");
            }
        };

        await ws.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    async void OnApplicationQuit()
    {
        if (ws != null)
            await ws.Close();
    }
}
