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
            var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] { "BTCUSDT", "xlmusdt", "XrPUsDT" }, true);

            client.BestPriceUpdateCallback = (time, symbol, bid, ask) =>
                Console.WriteLine($"{symbol}  {time:HH:mm:ss}  {bid}  {ask}");

            client.Start();


            var cmd =Console.ReadLine();

            client.BestPriceUpdateCallback = null;

            while (cmd != "exit")
            {
                var book = client.GetOrderBook("BTCUSDT");

                if (book != null)
                    Console.WriteLine($"{book.Symbol}  {book.Time}  {book.Asks.Count}|{book.Bids.Count}");

                cmd = Console.ReadLine();
            }

            

            client.Stop();
            client.Dispose();
        }
    }
}
