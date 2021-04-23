using System;
using Microsoft.Extensions.Logging;
using MyJetWallet.Connector.Binance.Ws;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            using ILoggerFactory loggerFactory =
                LoggerFactory.Create(builder =>
                    builder.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    }));

            ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

            //var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] {"BTCUSDT"}, true);
            //var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] { "BTCUSDT", "xlmusdt", "XrPUsDT" }, true);

            var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] { "BCHBTC", "BCHUSDT", "BTCUSDT", "LTCBTC", "LTCUSDT", "XRPBTC", "ETHBTC", "ETHUSDT", "XRPUSDT", "TRXUSDT", "XLMUSDT" }, true);


            client.BestPriceUpdateCallback = (time, symbol, bid, ask) =>
                Console.WriteLine($"{symbol}  {time:HH:mm:ss}  {bid}  {ask}");

            client.Start();


            var cmd =Console.ReadLine();

            client.BestPriceUpdateCallback = null;

            while (cmd != "exit")
            {
                Print(client, "BCHBTC");
                Print(client, "BCHUSDT");
                Print(client, "BTCUSDT");
                Print(client, "LTCBTC");

                Print(client, "LTCUSDT");
                Print(client, "XRPBTC");
                Print(client, "ETHBTC");
                Print(client, "ETHUSDT");

                Print(client, "XRPUSDT");
                Print(client, "TRXUSDT");
                Print(client, "XLMUSDT");

                cmd = Console.ReadLine();
            }

            

            client.Stop();
            client.Dispose();
        }

        private static void Print(BinanceWsOrderBooks client, string symbol)
        {
            var book = client.GetOrderBook(symbol);

            if (book != null)
                Console.WriteLine($"{book.Symbol}  {book.Time}  {book.Asks.Count}|{book.Bids.Count}");
        }
    }
}
