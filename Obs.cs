using System.ComponentModel;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>Minimal obs-websocket v5 client (built into OBS Studio 28+): list the scenes and switch the program scene.</summary>
sealed class ObsSession : IDisposable
{
    readonly ClientWebSocket ws = new();
    int nextId;

    ObsSession() => ws.Options.AddSubProtocol("obswebsocket.json"); // required by obs-websocket v5

    public static async Task<ObsSession> ConnectAsync(string host, string password, CancellationToken ct)
    {
        var s = new ObsSession();
        try
        {
            await s.ws.ConnectAsync(new Uri("ws://" + (string.IsNullOrWhiteSpace(host) ? "localhost:4455" : host.Trim())), ct);
            using var hello = await s.ReceiveAsync(ct); // op 0 Hello
            var d = hello.RootElement.GetProperty("d");
            var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            if (d.TryGetProperty("authentication", out var auth))
            {
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("OBS demande un mot de passe WebSocket");
                static string B64Sha(string v) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(v)));
                var secret = B64Sha(password + auth.GetProperty("salt").GetString());
                identify["authentication"] = B64Sha(secret + auth.GetProperty("challenge").GetString());
            }
            await s.SendAsync(1, identify, ct); // op 1 Identify
            using var ok = await s.ReceiveAsync(ct); // op 2 Identified (or a close frame when the password is wrong)
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    async Task SendAsync(int op, object data, CancellationToken ct) =>
        await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { op, d = data }), WebSocketMessageType.Text, true, ct);

    async Task<JsonDocument> ReceiveAsync(CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("connexion OBS refusée (mot de passe incorrect ?)");
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonDocument.Parse(ms.ToArray());
    }

    /// <returns>the "responseData" element of the reply (cloned); throws when OBS reports an error</returns>
    public async Task<JsonElement> RequestAsync(string type, object? data, CancellationToken ct)
    {
        var id = (++nextId).ToString();
        await SendAsync(6, new { requestType = type, requestId = id, requestData = data ?? new { } }, ct);
        while (true)
        {
            using var msg = await ReceiveAsync(ct);
            var root = msg.RootElement;
            if (root.GetProperty("op").GetInt32() != 7) continue;
            var d = root.GetProperty("d");
            if (d.GetProperty("requestId").GetString() != id) continue;
            var st = d.GetProperty("requestStatus");
            if (!st.GetProperty("result").GetBoolean())
                throw new InvalidOperationException(st.TryGetProperty("comment", out var c) ? c.GetString() : "requête OBS refusée");
            return d.TryGetProperty("responseData", out var rd) ? rd.Clone() : default;
        }
    }

    public void Dispose() => ws.Dispose();

    // ---- one-shot helpers ----

    static CancellationTokenSource Timeout(int ms) => new(ms);

    public static List<string> ListScenes(string host, string password, int timeoutMs = 3000)
    {
        using var cts = Timeout(timeoutMs);
        return Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            var r = await s.RequestAsync("GetSceneList", null, cts.Token);
            // OBS lists scenes bottom to top: reverse to match the order shown in OBS
            return r.GetProperty("scenes").EnumerateArray().Select(x => x.GetProperty("sceneName").GetString() ?? "").Reverse().ToList();
        }).GetAwaiter().GetResult();
    }

    public static void SetScene(string host, string password, string scene, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            await s.RequestAsync("SetCurrentProgramScene", new { sceneName = scene }, cts.Token);
        }).GetAwaiter().GetResult();
    }
}

/// <summary>Drop-down of the scenes read live from OBS; typing a name is still allowed (OBS not started yet).</summary>
class ObsSceneConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c) => false;

    public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c)
    {
        if (c?.Instance is not Step s) return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        try { return new TypeConverter.StandardValuesCollection(ObsSession.ListScenes(s.ObsHost, s.ObsPassword)); }
        catch { return new TypeConverter.StandardValuesCollection(Array.Empty<string>()); } // OBS closed or WebSocket disabled
    }
}
