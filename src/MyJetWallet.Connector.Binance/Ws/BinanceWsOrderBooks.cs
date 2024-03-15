using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyJetWallet.Connector.Binance.Ws.Models;
using MyJetWallet.Connector.Binance.WsEngine;
using Prometheus;

namespace MyJetWallet.Connector.Binance.Ws
{
    public class BinanceWsOrderBooks : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string[] _symbols;
        private readonly bool _fasted;
        private readonly BinanceWebsocketEngine _engine;
        private readonly HttpClient _httpClient = new();

        private readonly object _sync = new object();
        
        private readonly Dictionary<string, OrderBookTopXDto> _cache = new();

        public BinanceWsOrderBooks(ILogger logger, string[] symbols, bool fasted)
        {
            const string url = "wss://stream.binance.com:9443/ws";

            _logger = logger;
            _symbols = symbols.Select(e => e.ToLower()).ToArray();
            _fasted = fasted;
            _engine = new BinanceWebsocketEngine(nameof(BinanceWsOrderBooks), url, 5000, 20000, logger)
            {
                SendPing = SendPing, OnReceive = Receive, OnConnect = Connect, OnDisconnect = OnDisconnect
            };
        }

        //time, symbol, bid, ask
        public event Action<DateTime, string, decimal, decimal> BestPriceUpdateEvent;

        private async Task Connect(ClientWebSocket socket)
        {
            var packet = new SubscribePacket()
            {
                id = 1,
                method = "SET_PROPERTY",
                @params = new object[] {"combined", true}
            };

            var msg = JsonSerializer.Serialize(packet);

            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true,
                CancellationToken.None);

#pragma warning disable 4014
            SubscribeToSymbols(socket, _symbols);
#pragma warning restore 4014
            BinanceOrderBookMonitoringLocator.SocketWssStatus.Inc();
        }

        private async Task SubscribeToSymbols(ClientWebSocket socket, IEnumerable<string> symbols)
        {
            await Task.Delay(10).ConfigureAwait(false);

            var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var symbol in symbols)
            {
                if (socket.State != WebSocketState.Open)
                    return;

                Console.WriteLine($"Subscribe to symbol {symbol}");

                await Subscribe(socket, symbol, ++id, _fasted).ConfigureAwait(false);

                await Task.Delay(2000);
            }
        }

        private static async Task Subscribe(WebSocket socket, string symbol, long id, bool fasted)
        {
            var interval = fasted ? "@100ms" : "";

            var packet = new SubscribePacket()
            {
                id = id,
                method = "SUBSCRIBE",
                @params = new[] {$"{symbol}@depth20{interval}"}
            };

            var msg = JsonSerializer.Serialize(packet);

            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }


        private static async Task Unsubscribe(WebSocket socket, string symbol, long id, bool fasted)
        {
            var interval = fasted ? "@100ms" : "";

            var packet = new SubscribePacket()
            {
                id = id,
                method = "UNSUBSCRIBE",
                @params = new[] {$"{symbol}@depth20{interval}"}
            };

            var msg = JsonSerializer.Serialize(packet);

            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        public async Task Reset(string symbol)
        {
            await Unsubscribe(symbol);
            await Subscribe(symbol);
        }

        public async Task Subscribe(string symbol)
        {
            var webSocket = _engine.GetClientWebSocket();
            if (webSocket is not {State: WebSocketState.Open})
                return;

            Console.WriteLine($"Subscribe to symbol {symbol}");

            await Subscribe(webSocket, symbol, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _fasted)
                .ConfigureAwait(false);

            await Task.Delay(2000);
        }

        public async Task Unsubscribe(string symbol)
        {
            var webSocket = _engine.GetClientWebSocket();
            if (webSocket is not {State: WebSocketState.Open})
                return;

            Console.WriteLine($"Unsubscribe from symbol {symbol}");

            await Unsubscribe(webSocket, symbol, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _fasted)
                .ConfigureAwait(false);

            await Task.Delay(2000);
        }

        private async Task Receive(ClientWebSocket socket, string msg)
        {
            BinanceOrderBookMonitoringLocator.SocketWssIncomeMessages.Inc();
            using (BinanceOrderBookMonitoringLocator.AvgWssQuoteProcessTime.NewTimer())
            {
                var packet = JsonSerializer.Deserialize<OrderBookTopXDto>(msg);

                if (packet == null || string.IsNullOrEmpty(packet.stream))
                {
                    Console.WriteLine(msg);
                    return;
                }
                
                if (packet.data == null)
                {
                    Console.WriteLine(msg);
                    return;
                }
                
                var action = BestPriceUpdateEvent;

                var symbol = packet.stream.Replace("@depth20", "").Replace("@100ms", "");

                var prices = packet.data
                    .asks
                    .Where(e => e.Length == 2 && e[1] != "0")
                    .Select(e => decimal.Parse(e[0], NumberStyles.Any, CultureInfo.InvariantCulture))
                    .ToList();
                
                var ask = prices.Any() ? prices.Min() : 0;
                
                prices = packet.data
                    .bids
                    .Where(e => e.Length == 2 && e[1] != "0")
                    .Select(e => decimal.Parse(e[0], NumberStyles.Any, CultureInfo.InvariantCulture))
                    .ToList();
                
                var bid = prices.Any() ? prices.Max() : 0;

                if (!string.IsNullOrEmpty(symbol) && ask > 0 && bid > 0)
                {
                    try
                    {
                        action?.Invoke(
                            DateTime.UtcNow,
                            symbol,
                            bid,
                            ask
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Cannot execute BestPriceUpdateEvent. Symbol: {simbol}; bid: {bid}; ask: {ask}", symbol,
                            bid, ask);
                    }
                }

                if (!string.IsNullOrEmpty(symbol))
                {
                    _cache[symbol] = packet;
                }
            }
        }

        private Task SendPing(ClientWebSocket arg)
        {
            BinanceOrderBookMonitoringLocator.SocketWssPingPongStatus.Inc();
            return Task.CompletedTask;
        }

        private Task OnDisconnect()
        {
            BinanceOrderBookMonitoringLocator.SocketWssStatus.Dec();
            return Task.CompletedTask;
        }

        public void Start()
        {
            _engine.Start();
        }

        public void Stop()
        {
            _engine.Stop();
        }

        public void Dispose()
        {
            _engine.Stop();
            _engine.Dispose();
        }

        public BinanceOrderBookCache GetOrderBook(string symbol)
        {
            lock (_sync)
            {
                if (!_cache.TryGetValue(symbol.ToLower(), out var book))
                {
                    return null;
                }

                var result = new BinanceOrderBookCache()
                {
                    Symbol = symbol.ToLower(),
                    Time = DateTime.UtcNow,
                    LastId = book.data.lastUpdateId,
                    Asks = book.data.asks
                        .Where(e => e.Length == 2 && e[1] != "0")
                        .ToDictionary(e => decimal.Parse(e[0], NumberStyles.Any, CultureInfo.InvariantCulture), e => decimal.Parse(e[1], NumberStyles.Any, CultureInfo.InvariantCulture)),
                    Bids = book.data.bids
                        .Where(e => e.Length == 2 && e[1] != "0")
                        .ToDictionary(e => decimal.Parse(e[0], NumberStyles.Any, CultureInfo.InvariantCulture), e => decimal.Parse(e[1], NumberStyles.Any, CultureInfo.InvariantCulture)),
                };

                return result;
            }
        }
    }

    public class BinanceOrderBookCache
    {
        public string Symbol { get; set; }
        public DateTime Time { get; set; }
        public long LastId { get; set; }
        public Dictionary<decimal, decimal> Asks { get; set; } = new();
        public Dictionary<decimal, decimal> Bids { get; set; } = new();
    }
}