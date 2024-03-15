using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MyJetWallet.Connector.Binance.Ws.Models
{
    public class SubscribePacket
    {
        public string method { get; set; }
        public long id { get; set; }
        public object[] @params { get; set; }
    }

    public class OrderBookTopXDto
    {
        [JsonPropertyName("stream")] public string stream { get; set; }

        [JsonPropertyName("data")] public DataType data { get; set; }

        public class DataType
        {
            [JsonPropertyName("lastUpdateId")]
            public long lastUpdateId { get; set; }
            
            public string[][] bids { get; set; }
            public string[][] asks { get; set; }
        }
    }

    public class OrderBookDto
    {
        [JsonPropertyName("stream")]
        public string Stream { get; set; }

        [JsonPropertyName("data")]
        public DataType Data { get; set; }

        public class DataType
        {
            [JsonPropertyName("e")]
            public string EventType { get; set; }

            [JsonPropertyName("E")]
            public long Time { get; set; }

            [JsonPropertyName("U")]
            public long FirstUpdateId { get; set; }

            [JsonPropertyName("u")]
            public long LastUpdateId { get; set; }

            [JsonPropertyName("b")]
            public string[][] bids { get; set; }

            [JsonPropertyName("a")]
            public string[][] asks { get; set; }

            public DateTime GetTime()
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(Time).DateTime;
            }
        }

        
    }

    public class OrderBookSnapshotDto
    {
        [JsonPropertyName("lastUpdateId")]
        public long LastUpdateId { get; set; }

        [JsonPropertyName("bids")]
        public string[][] bids { get; set; }

        [JsonPropertyName("asks")]
        public string[][] asks { get; set; }
    }
}