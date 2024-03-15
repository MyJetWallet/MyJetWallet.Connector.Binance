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

            //var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] { "BCHBTC", "BCHUSDT", "BTCUSDT", "LTCBTC", "LTCUSDT", "XRPBTC", "ETHBTC", "ETHUSDT", "XRPUSDT", "TRXUSDT", "XLMUSDT" }, true);
            var client = new BinanceWsOrderBooks(loggerFactory.CreateLogger<BinanceWsOrderBooks>(), new[] { "BTCUSDT" }, true);


            client.BestPriceUpdateEvent += PrintBestPrice;

            client.Start();


            var cmd =Console.ReadLine();

            client.BestPriceUpdateEvent -= PrintBestPrice;

            while (cmd != "exit")
            {
                Print(client, "BTCUSDT");
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

        private static void PrintBestPrice(DateTime time, string symbol, decimal bid, decimal ask)
        {
            Console.WriteLine($"{symbol}  {time:HH:mm:ss}  {bid}  {ask}");
        }

        private static void Print(BinanceWsOrderBooks client, string symbol)
        {
            var book = client.GetOrderBook(symbol);

            if (book != null)
                Console.WriteLine($"{book.Symbol}  {book.Time}  {book.Asks.Count}|{book.Bids.Count}");
        }
    }
}

/*
{
     "stream": "bchbtc@depth20@100ms",
     "data": {
       "lastUpdateId": 1714276886,
       "bids": [
         [
           "0.00603200",
           "0.66600000"
         ],
         [
           "0.00603100",
           "2.50800000"
         ],
         [
           "0.00603000",
           "48.99300000"
         ],
         [
           "0.00602900",
           "6.14600000"
         ],
         [
           "0.00602800",
           "0.91800000"
         ],
         [
           "0.00602700",
           "0.73100000"
         ],
         [
           "0.00602600",
           "12.26700000"
         ],
         [
           "0.00602500",
           "52.62000000"
         ],
         [
           "0.00602400",
           "0.74100000"
         ],
         [
           "0.00602300",
           "1.91800000"
         ],
         [
           "0.00602200",
           "5.59900000"
         ],
         [
           "0.00601200",
           "6.16600000"
         ],
         [
           "0.00601000",
           "1.49800000"
         ],
         [
           "0.00600600",
           "20.00000000"
         ],
         [
           "0.00600500",
           "1.66500000"
         ],
         [
           "0.00600000",
           "10.60400000"
         ],
         [
           "0.00599500",
           "1.66800000"
         ],
         [
           "0.00599400",
           "70.36200000"
         ],
         [
           "0.00599100",
           "0.02200000"
         ],
         [
           "0.00599000",
           "1.66900000"
         ]
       ],
       "asks": [
         [
           "0.00603700",
           "0.73600000"
         ],
         [
           "0.00603900",
           "3.01800000"
         ],
         [
           "0.00604000",
           "0.08600000"
         ],
         [
           "0.00604100",
           "9.09500000"
         ],
         [
           "0.00604200",
           "48.32700000"
         ],
         [
           "0.00604300",
           "6.01300000"
         ],
         [
           "0.00604400",
           "1.84000000"
         ],
         [
           "0.00604500",
           "1.65100000"
         ],
         [
           "0.00604600",
           "48.32700000"
         ],
         [
           "0.00605000",
           "0.34000000"
         ],
         [
           "0.00605400",
           "20.00000000"
         ],
         [
           "0.00605500",
           "0.08400000"
         ],
         [
           "0.00605700",
           "6.12000000"
         ],
         [
           "0.00606300",
           "8.56800000"
         ],
         [
           "0.00606500",
           "2.98200000"
         ],
         [
           "0.00606600",
           "0.27200000"
         ],
         [
           "0.00606900",
           "0.83300000"
         ],
         [
           "0.00607100",
           "0.03600000"
         ],
         [
           "0.00607400",
           "70.60800000"
         ],
         [
           "0.00607600",
           "0.34300000"
         ]
       ]
     }
   }
 */
