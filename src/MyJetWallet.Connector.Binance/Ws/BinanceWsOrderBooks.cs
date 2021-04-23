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


namespace MyJetWallet.Connector.Binance.Ws
{
    public class BinanceWsOrderBooks : IDisposable
    {
        private ILogger<BinanceWsOrderBooks> _logger;
        private readonly string[] _symbols;
        private readonly bool _fasted;
        private readonly WebsocketEngine _engine;
        private readonly HttpClient _httpClient = new ();

        private Dictionary<string, BinanceOrderBookCache> _cache = new();
        private readonly object _sync = new object();

        public BinanceWsOrderBooks(ILogger<BinanceWsOrderBooks> logger, string[] symbols, bool fasted)
        {
            var url = "wss://stream.binance.com:9443/ws";

            _logger = logger;
            _symbols = symbols.Select(e => e.ToLower()).ToArray();
            _fasted = fasted;
            _engine = new WebsocketEngine(nameof(BinanceWsOrderBooks), url, 5000, 10000, logger);
            _engine.SendPing = SendPing;
            _engine.OnReceive = Receive;
            _engine.OnConnect = Connect;
        }

        public Action<DateTime, string, decimal, decimal> BestPriceUpdateCallback { get; set; } = null;

        private async Task Connect(ClientWebSocket socket)
        {
            var packet = new SubscribePacket()
            {
                id = 1,
                method = "SET_PROPERTY",
                @params = new object[] { "combined", true }
            };

            var msg = JsonSerializer.Serialize(packet);

            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);

            foreach (var symbol in _symbols)
            {
                await Subscribe(socket, symbol);
            }
        }

        private async Task Subscribe(ClientWebSocket socket, string symbol)
        {
            var interval = _fasted ? "@100ms" : "";

            var packet = new SubscribePacket()
            {
                id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                method = "SUBSCRIBE",
                @params = new []{ $"{symbol}@depth{interval}" }
            };

            var msg = JsonSerializer.Serialize(packet);

            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task Receive(ClientWebSocket socket, string msg)
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
                var symbol = packet.Stream.Replace("@depth", "").Replace("@100ms", "");
                book = await LoadSnapshot(symbol, packet.Stream);
                BestPriceUpdate(book);
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
                var symbol = packet.Stream.Replace("@depth", "").Replace("@100ms", "");
                _logger.LogInformation($"Resubscribe {symbol}. LastId={book.LastId}. Receive: {packet.Data.FirstUpdateId}|{packet.Data.LastUpdateId}. Count: {book.Asks.Count}|{book.Bids.Count}");
                book = await LoadSnapshot(symbol, packet.Stream);
                lock (_sync)
                {
                    _cache[packet.Stream] = book;
                }

                BestPriceUpdate(book);
            }
        }

        private void BestPriceUpdate(BinanceOrderBookCache book)
        {
            var action = BestPriceUpdateCallback;

            action?.Invoke(
                book.Time,
                book.Symbol,
                book.Bids.Keys.Max(),
                book.Asks.Keys.Min()
            );
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
            var json = await _httpClient.GetStringAsync($"https://api.binance.com/api/v3/depth?symbol={symbol.ToUpper()}&limit=1000");

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