using System.Net.WebSockets;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Text;

namespace TerminalDanmakuChan
{
    public class DanmakuReceiver
    {
        public delegate void ConnectCallback();
        public delegate void DanmakuCallback(string uname, string text);
        public delegate void GiftCallback(string uname, string name, int count, int price);
        public delegate void SuperchatCallback(string uname, string text, string price);
        public delegate void GuardCallback(string name, string type);
        public event ConnectCallback Connected;
        public event DanmakuCallback OnDanmaku;
        public event GiftCallback OnGift;
        public event SuperchatCallback OnSuperChat;
        public event GuardCallback OnGuard;
        private readonly ClientWebSocket client = new ClientWebSocket();
        private readonly HttpClient httpClient = new HttpClient();
        private readonly long roomId;
        private readonly string token, serverUrl;
        private readonly System.Timers.Timer timer = new System.Timers.Timer();
        public bool isStoped = false;
        public DanmakuReceiver(long roomId)
        {
            this.roomId = roomId;
            var res = httpClient.GetAsync($"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={roomId}&platform=pc&player=web").Result;
            JObject resJson = JObject.Parse(res.Content.ReadAsStringAsync().Result);
            token = resJson["data"]["token"].ToString();
            serverUrl = $"wss://{resJson["data"]["host_server_list"][0]["host"]}:{resJson["data"]["host_server_list"][0]["wss_port"]}/sub";
            timer.AutoReset = true;
            timer.Elapsed += (object _sender, System.Timers.ElapsedEventArgs _args) =>
            {
                try
                {
                    Send(GeneratePacket(1, 2, "陈睿你妈死了"));
                }
                catch
                {
                    Connect();
                }
            };
        }
        public void Connect()
        {
            client.ConnectAsync(new Uri(serverUrl), CancellationToken.None).Wait();
            JObject authPayload = new JObject();
            authPayload.Add("roomid", roomId);
            authPayload.Add("protover", 3);
            authPayload.Add("platform", "web");
            authPayload.Add("uid", 0);
            authPayload.Add("key", token);
            Send(GeneratePacket(1, 7, authPayload.ToString()));
        }
        public void Send(byte[] packet)
        {
            if (client.State == WebSocketState.Open)
            {
                client.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
            }
            else
            {
                throw new Exception("与服务器断开连接");
            }
        }
        public static byte[] GeneratePacket(ushort protocol, uint type, string payloadString)
        {
            byte[] payload = Encoding.UTF8.GetBytes(payloadString);
            int totalLength = 16 + payload.Length;
            byte[] packet = new byte[totalLength];

            byte[] totalLengthBuffer = BitConverter.GetBytes(totalLength);
            byte[] headerLengthBuffer = BitConverter.GetBytes((short)16);
            byte[] protocolBuffer = BitConverter.GetBytes(protocol);
            byte[] typeBuffer = BitConverter.GetBytes(type);
            byte[] placeholderBuffer = BitConverter.GetBytes((uint)1);
            Array.Reverse(totalLengthBuffer);
            Array.Reverse(headerLengthBuffer);
            Array.Reverse(protocolBuffer);
            Array.Reverse(typeBuffer);
            Array.Reverse(placeholderBuffer);
            Buffer.BlockCopy(totalLengthBuffer, 0, packet, 0, sizeof(int));
            Buffer.BlockCopy(headerLengthBuffer, 0, packet, 4, sizeof(short));
            Buffer.BlockCopy(protocolBuffer, 0, packet, 6, sizeof(ushort));
            Buffer.BlockCopy(typeBuffer, 0, packet, 8, sizeof(uint));
            Buffer.BlockCopy(placeholderBuffer, 0, packet, 12, sizeof(uint));
            Buffer.BlockCopy(payload, 0, packet, 16, payload.Length);
            return packet;
        }
        public void ReceiveData()
        {
            while (true)
            {
                if (isStoped)
                {
                    break;
                }
                if (client.State == WebSocketState.Open)
                {
                    byte[] buf = new byte[8192];
                    ArraySegment<byte> buffer = new ArraySegment<byte>(buf);
                    int totalLength = client.ReceiveAsync(buffer, CancellationToken.None).Result.Count;
                    byte[] protocolBuffer = new ArraySegment<byte>(buf, 6, 2).ToArray();
                    byte[] typeBuffer = new ArraySegment<byte>(buf, 8, 4).ToArray();
                    Array.Reverse(protocolBuffer);
                    Array.Reverse(typeBuffer);
                    ushort protocol = BitConverter.ToUInt16(protocolBuffer);
                    uint type = BitConverter.ToUInt32(typeBuffer);
                    ArraySegment<byte> payload = new ArraySegment<byte>(buf, 16, totalLength - 16);
                    switch (type)
                    {
                        case 8:
                            {
                                Connected();
                                timer.Enabled = true;
                                break;
                            }
                        case 5:
                            {
                                if (protocol == 3)
                                {
                                    byte[] msgBuffer = new byte[4096];
                                    BrotliDecoder.TryDecompress(payload, msgBuffer, out int payloadTotalLength);
                                    int offset = 0;
                                    while (offset < payloadTotalLength)
                                    {
                                        byte[] lengthBuffer = new ArraySegment<byte>(msgBuffer, offset, 4).ToArray();
                                        Array.Reverse(lengthBuffer);
                                        uint length = BitConverter.ToUInt32(lengthBuffer);
                                        ArraySegment<byte> singalPayloadBuffer = new ArraySegment<byte>(msgBuffer, offset + 16, Convert.ToInt32(length));
                                        try
                                        {
                                            int leftQuoteCount = 0;
                                            string msgJson = Encoding.UTF8.GetString(singalPayloadBuffer);
                                            string msgJsonFinal = "";
                                            char[] msgJsonChars = msgJson.ToArray();
                                            foreach (char item in msgJsonChars)
                                            {
                                                if (item == '}')
                                                {
                                                    leftQuoteCount--;
                                                    msgJsonFinal += item.ToString();
                                                    if (leftQuoteCount == 0)
                                                    {
                                                        break;
                                                    }
                                                }
                                                else
                                                {
                                                    msgJsonFinal += item.ToString();
                                                    if (item == '{')
                                                    {
                                                        leftQuoteCount++;
                                                    }
                                                }
                                            }
                                            JObject msgObject = JObject.Parse(msgJsonFinal);
                                            string cmd = msgObject["cmd"].ToString();
                                            switch (cmd)
                                            {
                                                case "DANMU_MSG":
                                                    {
                                                        OnDanmaku($"{msgObject["info"][2][1]}", $"{msgObject["info"][1]}");
                                                        break;
                                                    }
                                                case "SEND_GIFT":
                                                    {
                                                        JObject data = (JObject)msgObject.GetValue("data");
                                                        OnGift($"{data["uname"]}", $"{data["giftName"]}", data["super_gift_num"].ToObject<int>(), data["price"].ToObject<int>() / 1000 * msgObject["data"]["super_gift_num"].ToObject<int>());
                                                        break;
                                                    }
                                                case "SUPER_CHAT_MESSAGE":
                                                    {
                                                        // TODO: 抓个SC的包看看
                                                        break;
                                                    }
                                                case "GUARD_BUY":
                                                    {
                                                        JObject data = (JObject)msgObject.GetValue("data");
                                                        OnGuard(data["username"].ToString(), data["gift_name"].ToString());
                                                        break;
                                                    }
                                            }

                                        }
                                        catch (Exception e)
                                        {
                                            OnDanmaku("解析失败", e.Message);
                                        }
                                        finally
                                        {
                                            offset += Convert.ToInt32(length);
                                        }
                                    }
                                }
                                break;
                            }
                    }
                }
                else if (client.State == WebSocketState.Closed)
                {
                    Connect();
                }
            }
        }
    }
}