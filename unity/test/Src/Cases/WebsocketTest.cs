//#if PUERTS_GENERAL
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace Puerts.UnitTest
{
    public class WebSocketServer
    {
        private HttpListener _httpListener;
        private WebSocket _webSocket;

        public WebSocketServer(string url)
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(url);
        }

        public void Listen()
        {
            _httpListener.Start();
            Console.WriteLine("Server started, waiting for connection...");
        }

        public async Task AcceptAsync()
        {
            HttpListenerContext context = await _httpListener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
                _webSocket = webSocketContext.WebSocket;
                Console.WriteLine("Client connected.");
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }

        public async Task<string> ReceiveAsync()
        {
            byte[] buffer = new byte[1024];
            WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            string clientMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"Received message from client: {clientMessage}");
            return clientMessage;
        }

        public async Task SendAsync(string message)
        {
            byte[] responseBuffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(responseBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"Sent message to client: {message}");
        }

        public async Task CloseAsync()
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            Console.WriteLine("Connection closed.");
        }

        public void Stop()
        {
            _httpListener.Stop();
            Console.WriteLine("Server stopped.");
        }
    }

    [TestFixture]
    public class WebsocketTest
    {
        [Test]
        public async Task SmokeTest()
        {
            WebSocketServer wss = new WebSocketServer("http://localhost:5123/");
#if PUERTS_GENERAL
            var jsEnv = new JsEnv(new TxtLoader());
#else
            var jsEnv = new JsEnv(new DefaultLoader());
#endif
            Action waitJsEnv = () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(2);
                    jsEnv.Tick();
                }
            };
            wss.Listen();
            var acceptTask = wss.AcceptAsync();

            jsEnv.Eval(@"
                (function() {
                    global.con = new WebSocket('ws://localhost:5123');
                    con.addEventListener('open', (ev) => {
                        console.log(`on open`);
                        con.send('puerts websocket');
                    });
                    con.addEventListener('message', (ev) => {
                        console.log(`on message: ${ev.data}`);
                        global.webSocketMessage = ev.data;
                        //con.close();
                    });
                    con.addEventListener('close', (ev) => {
                        global.onclose_called = true;
                    });
                    con.addEventListener('error', (ev) => {
                        global.onerror_called = true;
                    });
                })();
            ");

            waitJsEnv();
            await acceptTask;

            waitJsEnv();
            string msg = await wss.ReceiveAsync();
            Assert.AreEqual(msg, "puerts websocket");
            await wss.SendAsync(msg);

            waitJsEnv();
            
            var res = jsEnv.Eval<string>("global.webSocketMessage");
            Assert.AreEqual(res, "puerts websocket");

            waitJsEnv();

            jsEnv.Eval(@"
                con._raw.send = () => {throw new Error()};
                con.send('some message');
            ");

            waitJsEnv();

            var flag = jsEnv.Eval<bool>("global.onclose_called");
            Assert.AreEqual(flag, true);
            flag = jsEnv.Eval<bool>("global.onerror_called");
            Assert.AreEqual(flag, true);

            wss.Stop();
        }
    }
}
//#endif
