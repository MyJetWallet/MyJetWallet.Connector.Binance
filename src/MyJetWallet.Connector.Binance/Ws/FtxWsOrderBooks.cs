using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using MyJetWallet.Connector.Binance.Ws.Models;
using MyJetWallet.Connector.Binance.WsEngine;


namespace MyJetWallet.Connector.Binance.Ws
{
    public class FtxWsOrderBooks : IDisposable
    {
        private ILogger<FtxWsOrderBooks> _logger;
        private readonly string[] _symbols;
        private readonly bool _fasted;
        private readonly WebsocketEngine _engine;
        private readonly HttpClient _httpClient = new ();

        private Dictionary<string, BinanceOrderBookCache> _cache = new();
        private readonly object _sync = new object();

        public FtxWsOrderBooks(ILogger<FtxWsOrderBooks> logger, string[] symbols, bool fasted)
        {
            var url = "wss://stream.binance.com:9443/ws";

            _logger = logger;
            _symbols = symbols;
            _fasted = fasted;
            _engine = new WebsocketEngine(nameof(FtxWsOrderBooks), url, 5000, 10000, logger);
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
                await Subscribe(socket, _fasted, symbol);
            }
        }

        private static async Task Subscribe(ClientWebSocket socket, bool fasted, string symbol)
        {
            var interval = fasted ? "@100ms" : "";

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
                var symbol = packet.Stream.Replace("@depth", "").Replace("@100", "");
                book = await LoadSnapshot(symbol);
                BestPriceUpdate(book);
            }

            if (packet.Data.LastUpdateId <= book.LastId)
            {
                return;
            }

            if (packet.Data.FirstUpdateId == book.LastId + 1 ||
                (packet.Data.FirstUpdateId <= book.LastId && book.LastId <= packet.Data.LastUpdateId))
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

                        if (level[1] == "0")
                        {
                            book.Bids.Remove(price);
                        }
                        else
                        {
                            book.Bids[price] = volume;
                        }
                    }
                }

                BestPriceUpdate(book);
            }
            else
            {
                var symbol = packet.Stream.Replace("@depth", "").Replace("@100", "");
                _logger.LogError($"Resubscribe {symbol}. LastId={book.LastId}. Receive: {packet.Data.FirstUpdateId}|{packet.Data.LastUpdateId}");
                book = await LoadSnapshot(symbol);
                lock (_sync)
                {
                    _cache[book.Symbol] = book;
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
                book.Bids[book.Bids.Keys.Max()],
                book.Asks[book.Asks.Keys.Min()]
            );
        }

        private async Task<BinanceOrderBookCache> LoadSnapshot(string symbol)
        {
            BinanceOrderBookCache book;
            var snapshot = await GetSnapshot(symbol);

            book = new BinanceOrderBookCache()
            {
                Symbol = symbol,
                Time = DateTime.UtcNow,
                LastId = snapshot.LastUpdateId,
                Asks = snapshot.asks.ToDictionary(e => decimal.Parse(e[0]), e => decimal.Parse(e[1])),
                Bids = snapshot.bids.ToDictionary(e => decimal.Parse(e[0]), e => decimal.Parse(e[1]))
            };

            lock (_sync)
            {
                _cache[book.Symbol] = book;
            }
            
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