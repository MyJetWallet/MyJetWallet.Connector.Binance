using Prometheus;

namespace MyJetWallet.Connector.Binance
{
    public static class BinanceOrderBookMonitoringLocator
    {
        public static readonly Histogram AvgWssQuoteProcessTime =
            Metrics.CreateHistogram("binance_connector_order_book_wss_socket_quote_process_time",
                "Binance wss socket avg quote process time");
        
        public static readonly Gauge SocketWssStatus =
            Metrics.CreateGauge("binance_connector_order_book_wss_socket_status",
                "Binance wss socket status");
        
        public static readonly Counter SocketWssPingPongStatus =
            Metrics.CreateCounter("binance_connector_order_book_wss_socket_bing_pong_status",
                "Binance wss ping pong status");
        
        public static readonly Counter SocketWssIncomeMessages =
            Metrics.CreateCounter("binance_connector_order_book_wss_socket_income_messages",
                "Binance wss income messages");
        
        public static Gauge BinanceQuoteIncome =
            Metrics.CreateGauge("binance_connector_order_book_wss_socket_quote_income",
                "Binance wss socket avg quote process time", new GaugeConfiguration
                {
                    LabelNames = new []{"quote"}
                });
    }
}