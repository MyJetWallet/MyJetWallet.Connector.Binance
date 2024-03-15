using System;
using System.Collections.Generic;
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

        private readonly Dictionary<string, BinanceOrderBookCache> _cache = new();
        private readonly object _sync = new object();

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
                @params = new[] {$"{symbol}@depth{interval}"}
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
                @params = new[] {$"{symbol}@depth{interval}"}
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
                var packet = JsonSerializer.Deserialize<OrderBookDto>(msg);

                if (packet == null || string.IsNullOrEmpty(packet.Stream))
                {
                    Console.WriteLine(msg);
                    return;
                }

                BinanceOrderBookCache book;

                lock (_sync)
                {
                    if (!_cache.TryGetValue(packet.Stream, out book))
                    {
                        book = null;
                    }
                }

                if (book == null)
                {
                    if ((DateTime.UtcNow - _lastLoadFail).TotalMinutes > 2)
                    {
                        var symbol = packet.Stream.Replace("@depth", "").Replace("@100ms", "");
                        try
                        {
                            book = await LoadSnapshot(symbol, packet.Stream);
                            BestPriceUpdate(book);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Cannot load snapshot for {symbol}. wait 2 min", symbol);
                            _lastLoadFail = DateTime.UtcNow;
                        }
                    }
                }

                if (packet.Data.LastUpdateId <= book.LastId)
                {
                    return;
                }


                if ((packet.Data.FirstUpdateId == book.LastId + 1 ||
                     (packet.Data.FirstUpdateId <= book.LastId && book.LastId <= packet.Data.LastUpdateId))
                    && book.Asks.Count < 3000 && book.Bids.Count < 3000)
                {
                    lock (_sync)
                    {
                        foreach (var level in packet.Data.asks)
                        {
                            var price = decimal.Parse(level[0]);
                            var volume = decimal.Parse(level[1]);

                            if (volume == 0)
                            {
                                book.Asks.Remove(price);
                            }
                            else
                            {
                                book.Asks[price] = volume;
                            }
                        }

                        foreach (var level in packet.Data.bids)
                        {
                            var price = decimal.Parse(level[0]);
                            var volume = decimal.Parse(level[1]);

                            if (volume == 0)
                            {
                                book.Bids.Remove(price);
                            }
                            else
                            {
                                book.Bids[price] = volume;
                            }
                        }

                        book.LastId = packet.Data.LastUpdateId;
                        book.Time = packet.Data.GetTime();
                    }

                    BestPriceUpdate(book);
                }
                else
                {
                    if ((DateTime.UtcNow - _lastLoadFail).TotalMinutes > 2)
                    {
                        var symbol = packet.Stream.Replace("@depth", "").Replace("@100ms", "");
                        _logger.LogInformation(
                            $"Resubscribe {symbol}. LastId={book.LastId}. Receive: {packet.Data.FirstUpdateId}|{packet.Data.LastUpdateId}. Count: {book.Asks.Count}|{book.Bids.Count}");

                        try
                        {
                            book = await LoadSnapshot(symbol, packet.Stream);
                            lock (_sync)
                            {
                                _cache[packet.Stream] = book;
                            }

                            BestPriceUpdate(book);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Cannot load snapshot for {symbol}. wait 2 min", symbol);
                            _lastLoadFail = DateTime.UtcNow;
                        }

                        
                    }
                }
            }
        }
        private DateTime _lastLoadFail = DateTime.MinValue;

        private void BestPriceUpdate(BinanceOrderBookCache book)
        {
            var action = BestPriceUpdateEvent;
            BinanceOrderBookMonitoringLocator.BinanceQuoteIncome.WithLabels(book.Symbol).Inc();

            var bid = book.Bids.Keys.Max();
            var ask = book.Asks.Keys.Min();

            try
            {
                action?.Invoke(
                    book.Time,
                    book.Symbol,
                    bid,
                    ask
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot execute BestPriceUpdateEvent. Symbol: {simbol}; bid: {bid}; ask: {ask}", book.Symbol, bid, ask);
            }
        }
        
        
        private async Task<BinanceOrderBookCache> LoadSnapshot(string symbol, string stream)
        {
            BinanceOrderBookCache book;
            var snapshot = await GetSnapshot(symbol);

            book = new BinanceOrderBookCache()
            {
                Symbol = symbol.ToUpper(),
                Time = DateTime.UtcNow,
                LastId = snapshot.LastUpdateId,
                Asks = snapshot.asks.ToDictionary(e => decimal.Parse(e[0]), e => decimal.Parse(e[1])),
                Bids = snapshot.bids.ToDictionary(e => decimal.Parse(e[0]), e => decimal.Parse(e[1]))
            };

            lock (_sync)
            {
                _cache[stream] = book;
            }

            Console.WriteLine($"Load snapshot: {symbol}. LastId: {book.LastId}. Time: {book.Time:O}");

            return book;
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
            var interval = _fasted ? "@100ms" : "";
            var key = $"{symbol.ToLower()}@depth{interval}";

            lock (_sync)
            {
                if (!_cache.TryGetValue(key, out var book))
                {
                    return null;
                }

                var result = new BinanceOrderBookCache()
                {
                    Symbol = book.Symbol,
                    Time = book.Time,
                    LastId = book.LastId,
                    Asks = book.Asks.ToDictionary(e => e.Key, e => e.Value),
                    Bids = book.Bids.ToDictionary(e => e.Key, e => e.Value)
                };

                return result;
            }
        }

        private async Task<OrderBookSnapshotDto> GetSnapshot(string symbol)
        {
            var json = await _httpClient.GetStringAsync(
                $"https://api.binance.com/api/v3/depth?symbol={symbol.ToUpper()}&limit=1000");

            var data = JsonSerializer.Deserialize<OrderBookSnapshotDto>(json);

            if (data.LastUpdateId <= 0)
            {
                throw new Exception($"Cannot get order book shapshot {symbol}. Response: {json}");
            }

            return data;
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