using Noesis.Javascript;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
var coloredOutputLock = new object();

// Pre-built JSON responses for the chrome://inspect and edge://inspect page (with hard-coded IDs which is fine here).
var chromiumInspectVersion = """
    {
        "Protocol-Version": "1.3"
    }

    """u8.ToArray();

var chromiumInspectList = """
    [
        {
            "id": "bf385103-49d3-43ed-8932-d4f15c5bb1e0",
            "title": "JavaScript.Net",
            "type": "node",
            "devtoolsFrontendUrl": "devtools://devtools/bundled/js_app.html?ws=127.0.0.1:9222/bf385103-49d3-43ed-8932-d4f15c5bb1e0",
            "webSocketDebuggerUrl": "ws://127.0.0.1:9222/bf385103-49d3-43ed-8932-d4f15c5bb1e0"
        }
    ]

    """u8.ToArray();

var code = """
    debugger;
    for (let it = 0; it < 3; it++) {
        const result = calc(1000);
        console.log(result);
    }

    function calc(n) {
        let sum = 0;
        for (let i = 1; i <= n; i++) {
            sum += i;
        }
        return sum;
    }
    """;
JavascriptContext.SetFatalErrorHandler(FatalErrorHandler);

var listener = new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:9222/"); // Necessary for the direct devtools link working (localhost is CSP blocked on new tabs)
listener.Prefixes.Add("http://[::1]:9222/"); // Necessary for chrome://inspect and edge://inspect discovery to work
listener.Start();

Console.WriteLine("Server started. Either visit chrome://inspect or edge://inspect in your Chromium browser (takes a while),");
Console.WriteLine("or use the following devtools frontend URL directly:");
Console.WriteLine(Encoding.UTF8.GetString(chromiumInspectList));
Console.WriteLine();

while (true)
{
    var context = await listener.GetContextAsync();
    if (context.Request.IsWebSocketRequest)
    {
        var websocketContext = await context.AcceptWebSocketAsync(null);
        Console.WriteLine("Connection established");
        var websocket = websocketContext.WebSocket;

        var engine = new JavascriptContext();
        engine.SetParameter("console", new DebugConsole(new(), message =>
        {
            if (websocket.State == WebSocketState.Open)
            {
                WriteColoredLine($"↑ {message}", ConsoleColor.Blue);
                websocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }));
        var debugger = new JavascriptDebugger(engine, true);
        var scriptTask = Task.Run(() =>
        {
            try
            {
                engine.Run(code, "foo.js");
            }
            catch (JavascriptException ex)
            {
                if (ex.Data["V8StackTrace"] is string stacktrace)
                {
                    WriteErrorLine(stacktrace);
                }
                else
                {
                    WriteErrorLine(ex.Message);
                }
            }
            catch (Exception ex)
            {
                WriteErrorLine(ex.Message);
            }
            debugger.Dispose();
            engine.Dispose();
        });

        debugger.MessageReceived += (sender, ev) =>
        {
            if (websocket.State == WebSocketState.Open)
            {
                WriteColoredLine($"↑ {ev.Message}", ConsoleColor.Blue);
                websocket.SendAsync(Encoding.UTF8.GetBytes(ev.Message), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        };

        while (websocket.State == WebSocketState.Open && debugger.IsConnected && !scriptTask.IsCompleted)
        {
            try
            {
                var messageBuilder = new List<byte>();
                var buffer = new byte[64 << 10];
                while (true)
                {
                    var received = await websocket.ReceiveAsync(buffer, CancellationToken.None);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    if (!debugger.IsConnected)
                    {
                        break;
                    }
                    if (scriptTask.IsCompleted)
                    {
                        break;
                    }
                    messageBuilder.AddRange(buffer.AsSpan()[..received.Count]);
                    if (received.EndOfMessage)
                    {
                        var command = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(messageBuilder));
                        WriteColoredLine($"↓ {command}", ConsoleColor.Green);
                        debugger.SendCommand(command);
                        break;
                    }
                }
            }
            catch (WebSocketException e)
            {
                WriteErrorLine($"WebSocketError, terminating execution: {e.Message} ({e.WebSocketErrorCode})");
                if (!scriptTask.IsCompleted)
                {
                    engine.TerminateExecution();
                }
            }
        }
        Console.WriteLine("Closing connection...");
        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        Console.WriteLine("---------------------");
        Console.WriteLine();
    }
    else
    {
        WriteColoredLine($"HTTP request to '{context.Request.RawUrl}'", ConsoleColor.Yellow);
        if (context.Request.Url?.LocalPath == "/json/version")
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = chromiumInspectVersion.LongLength;
            await context.Response.OutputStream.WriteAsync(chromiumInspectVersion);
            context.Response.OutputStream.Close();
        }
        else if (context.Request.Url?.LocalPath == "/json/list")
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = chromiumInspectList.LongLength;
            await context.Response.OutputStream.WriteAsync(chromiumInspectList);
            context.Response.OutputStream.Close();
        }
        else
        {
            WriteErrorLine($"Request to '{context.Request.RawUrl}' unhandled.");
            context.Response.StatusCode = 400;
            context.Response.OutputStream.Close();
        }
    }
}

void WriteColoredLine(string? message, ConsoleColor color)
{
    lock (coloredOutputLock)
    {
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }
}

void WriteErrorLine(string? message) => WriteColoredLine(message, ConsoleColor.DarkRed);

void FatalErrorHandler(string a, string b)
{
    WriteErrorLine(a);
    WriteErrorLine(b);
}

// Minimal implementation of a little console logger that only ever outputs strings, not typed values.
public class RuntimeConsole
{
    public void log(object message) => Console.WriteLine(ToJavaScriptString(message));

    internal static string ToJavaScriptString(object obj)
    {
        if (obj == null)
            return "null";
        if (obj is string s)
            return s;
        if (obj is Dictionary<string, object>)
            return JsonSerializer.Serialize(obj);
        if (typeof(IDictionary).IsAssignableFrom(obj.GetType()))
            return "[object Object]";
        if (obj is DateTime date)
            return date.ToString(@"ddd MMM dd yyyy HH:mm:ss G\MTzzzz", DateTimeFormatInfo.InvariantInfo);
        if (obj is IConvertible convertible)
            return convertible.ToString(CultureInfo.InvariantCulture);
        if (obj.GetType().IsArray)
        {
            var result = new StringBuilder();
            var enumerable = (IEnumerable) obj;
            var i = 0;
            foreach (var element in enumerable)
            {
                if (i++ > 0)
                    result.Append(",");
                result.Append(ToJavaScriptString(element));
            }
            return result.ToString();
        }
        return obj.ToString()!;
    }
}

public class DebugConsole
{
    private readonly RuntimeConsole console;
    private readonly Action<string> sendMessage;

    public DebugConsole(RuntimeConsole console, Action<string> sendMessage)
    {
        this.console = console;
        this.sendMessage = sendMessage;
    }

    public void log(object message)
    {
        var serialized = RuntimeConsole.ToJavaScriptString(message);
        // Send notification for a console log call and hard-code both type fields because we only support that.
        sendMessage(JsonSerializer.Serialize(new
        {
            method = "Runtime.consoleAPICalled",
            @params = new
            {
                type = "log",
                args = new object[]
                {
                    new
                    {
                        type = "string",
                        value = serialized,
                    },
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        }));
        console.log(serialized);
    }
}
