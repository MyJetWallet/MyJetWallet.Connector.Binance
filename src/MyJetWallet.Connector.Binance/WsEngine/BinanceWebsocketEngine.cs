using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using MyJetWallet.Sdk.WebSocket;

namespace MyJetWallet.Connector.Binance.WsEngine
{
    public class BinanceWebsocketEngine : WebsocketEngine
    {
        public BinanceWebsocketEngine(string name, string url, int pingIntervalMSec, int silenceDisconnectIntervalMSec,
            ILogger logger) : base(name, url, pingIntervalMSec, silenceDisconnectIntervalMSec, logger)
        {
        }

        protected override void InitHeaders(ClientWebSocket clientWebSocket)
        {
        }
    }
}