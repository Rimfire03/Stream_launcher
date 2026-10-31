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

    /// <summary>Loads the profile, then the scene collection, then the scene: each only when given and not already current.</summary>
    public static void Apply(string host, string password, string profile, string collection, string scene, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            await s.SwitchAsync("GetProfileList", "currentProfileName", "SetCurrentProfile", "profileName", profile, cts.Token);
            await s.SwitchAsync("GetSceneCollectionList", "currentSceneCollectionName", "SetCurrentSceneCollection", "sceneCollectionName", collection, cts.Token);
            if (!string.IsNullOrWhiteSpace(scene))
                await s.RequestAsync("SetCurrentProgramScene", new { sceneName = scene }, cts.Token);
        }).GetAwaiter().GetResult();
    }

    /// <summary>start / stop of the live, the recording or the virtual camera; does nothing when OBS is already in the wanted state</summary>
    public static void RunAction(string host, string password, ObsActionKind action, int timeoutMs, CancellationToken ct)
    {
        var (status, request, wanted) = action switch
        {
            ObsActionKind.StartStream => ("GetStreamStatus", "StartStream", true),
            ObsActionKind.StopStream => ("GetStreamStatus", "StopStream", false),
            ObsActionKind.StartRecord => ("GetRecordStatus", "StartRecord", true),
            ObsActionKind.StopRecord => ("GetRecordStatus", "StopRecord", false),
            ObsActionKind.StartVirtualCam => ("GetVirtualCamStatus", "StartVirtualCam", true),
            ObsActionKind.StopVirtualCam => ("GetVirtualCamStatus", "StopVirtualCam", false),
            _ => throw new InvalidOperationException("action OBS inconnue"),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            async Task<bool> Active() => (await s.RequestAsync(status, null, cts.Token)).GetProperty("outputActive").GetBoolean();
            if (await Active() == wanted) return;
            await s.RequestAsync(request, null, cts.Token);
            if (action == ObsActionKind.StartStream) // a refused stream (wrong key, no network) only shows as "not active"
            {
                for (int i = 0; i < 16 && !await Active(); i++) await Task.Delay(500, cts.Token);
                if (!await Active()) throw new InvalidOperationException("OBS n'a pas démarré le live (clé de diffusion ? réseau ?)");
            }
        }).GetAwaiter().GetResult();
    }

    /// <summary>switches the OBS profile / scene collection when it is not already the current one, and waits until OBS has loaded it</summary>
    async Task SwitchAsync(string listRequest, string currentKey, string setRequest, string setField, string target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var list = await RequestAsync(listRequest, null, ct);
        if (list.GetProperty(currentKey).GetString() == target) return;
        await RequestAsync(setRequest, new Dictionary<string, string> { [setField] = target }, ct);
        while (true)
        {
            await Task.Delay(500, ct);
            if ((await RequestAsync(listRequest, null, ct)).GetProperty(currentKey).GetString() == target) break;
        }
        await Task.Delay(800, ct); // let OBS finish loading
    }

    public static List<string> ListProfiles(string host, string password, int timeoutMs = 3000)
    {
        using var cts = Timeout(timeoutMs);
        return Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            var r = await s.RequestAsync("GetProfileList", null, cts.Token);
            return r.GetProperty("profiles").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        }).GetAwaiter().GetResult();
    }

    public static List<string> ListCollections(string host, string password, int timeoutMs = 3000)
    {
        using var cts = Timeout(timeoutMs);
        return Task.Run(async () =>
        {
            using var s = await ConnectAsync(host, password, cts.Token);
            var r = await s.RequestAsync("GetSceneCollectionList", null, cts.Token);
            return r.GetProperty("sceneCollections").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
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
        // the PropertyGrid hands over the selected object (a StepView), not the step itself
        var s = c?.Instance as Step ?? (c?.Instance as StepView)?.Step;
        if (s == null) return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        try { return new TypeConverter.StandardValuesCollection(ObsSession.ListScenes(s.ObsHost, s.ObsPassword)); }
        catch (Exception e) // OBS closed, WebSocket disabled, wrong password
        {
            RunLog.Write("liste des scènes OBS impossible : " + e.Message);
            return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        }
    }
}

/// <summary>Drop-down of the scene collections read live from OBS; typing a name is still allowed.</summary>
class ObsCollectionConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c) => false;

    public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c)
    {
        var s = c?.Instance as Step ?? (c?.Instance as StepView)?.Step;
        if (s == null) return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        try { return new TypeConverter.StandardValuesCollection(ObsSession.ListCollections(s.ObsHost, s.ObsPassword)); }
        catch (Exception e)
        {
            RunLog.Write("liste des collections OBS impossible : " + e.Message);
            return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        }
    }
}

/// <summary>Drop-down of the OBS profiles read live from OBS; typing a name is still allowed.</summary>
class ObsProfileConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c) => false;

    public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c)
    {
        var s = c?.Instance as Step ?? (c?.Instance as StepView)?.Step;
        if (s == null) return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        try { return new TypeConverter.StandardValuesCollection(ObsSession.ListProfiles(s.ObsHost, s.ObsPassword)); }
        catch (Exception e)
        {
            RunLog.Write("liste des profils OBS impossible : " + e.Message);
            return new TypeConverter.StandardValuesCollection(Array.Empty<string>());
        }
    }
}
