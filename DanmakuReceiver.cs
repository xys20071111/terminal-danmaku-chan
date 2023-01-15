using System.IO.Compression;
using System.Net.WebSockets;
using System.Threading;
using System.Net.Http;
using System.Timers;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
// using UnityEngine;

namespace TerminalDanmakuChan
{
    public class DanmakuReceiver
    {
        public delegate void ConnectCallback();
        public delegate void DisconnectCallback(int type);
        public delegate void DanmakuCallback(string uname, string text, long uid);
        public delegate void GiftCallback(string uname, string giftName, int count, float price);
        public delegate void SuperchatCallback(string uname, float price, string text);
        public delegate void GuardCallback(string uname, string type);
        public event ConnectCallback OnConnect;
        public event DisconnectCallback OnDisconnect;
        public event DanmakuCallback OnDanmaku;
        public event GiftCallback OnGift;
        public event SuperchatCallback OnSuperChat;
        public event GuardCallback OnGuard;
        static readonly HttpClient httpClient = new HttpClient();
        private int roomId;
        private ClientWebSocket client = new ClientWebSocket();
        private Uri serverAddress;
        private string accessToken;
        private System.Timers.Timer timer;
        public DanmakuReceiver(int roomId)
        {
            this.roomId = roomId;
            timer = new System.Timers.Timer(10000);
            timer.Elapsed += (object _, ElapsedEventArgs _) =>
            {
                Send(CreatePacket(1, 2, "陈睿你妈死了"));
            };
            timer.AutoReset = true;
        }
        private byte[] CreatePacket(ushort protocol, uint type, string payloadString)
        {
            byte[] payload = Encoding.UTF8.GetBytes(payloadString);
            int totalLength = 16 + payload.Length;
            byte[] packet = new byte[totalLength];

            byte[] totalLengthBuffer = BitConverter.GetBytes(totalLength);
            byte[] headerLengthBuffer = BitConverter.GetBytes(Convert.ToInt16(16));
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
        private void Send(byte[] packet)
        {
            if (client.State == WebSocketState.Open)
            {
                client.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
            }
            else
            {
                if (OnDisconnect != null)
                {
                    OnDisconnect(1);
                }
            }
        }
        /// <summary>
        /// 获取服务器信息和认证信息，连接弹幕服务器
        /// </summary>
        public void Connect()
        {
            var reponse = httpClient.GetAsync($"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={this.roomId}&type=0").Result;
            string rawJsonString = reponse.Content.ReadAsStringAsync().Result;
            JObject response = JObject.Parse(rawJsonString);
            JObject danmakuInfo = response["data"].ToObject<JObject>();
            // serverAddress = new Uri($"wss://{danmakuInfo["host_list"][0]["host"]}:{danmakuInfo["host_list"][0]["wss_port"]}/sub");
            serverAddress = new Uri("wss://hw-sh-live-comet-02.chat.bilibili.com:2245/sub");
            accessToken = danmakuInfo["token"].ToString();
            client.ConnectAsync(serverAddress, CancellationToken.None).Wait();
            JObject authPayload = new JObject();
            authPayload.Add("roomid", roomId);
            authPayload.Add("protover", 3);
            authPayload.Add("platform", "web");
            authPayload.Add("uid", 0);
            authPayload.Add("key", accessToken);
            Send(CreatePacket(1, 7, authPayload.ToString()));
        }
        public void Disconnect()
        {
            timer.Enabled = false;
            client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
            if (OnDisconnect != null)
            {
                OnDisconnect(0);
            }
        }
        private void ProcessPayload(byte[] payload, int totalLength)
        {
            int offset = 0;
            while (offset < totalLength)
            {
                byte[] lengthBuffer = new ArraySegment<byte>(payload, offset, 4).ToArray();
                Array.Reverse(lengthBuffer);
                uint length = BitConverter.ToUInt32(lengthBuffer);
                try
                {
                    if (offset + 16 + length > payload.Length)
                    {
                        throw new Exception("弹幕信息buffer太小了");
                    }
                    ArraySegment<byte> singalPayloadBuffer = new ArraySegment<byte>(payload, offset + 16, Convert.ToInt32(length));
                    string msgJson = Encoding.UTF8.GetString(singalPayloadBuffer);
                    JObject msgObject = JObject.Parse(msgJson);
                    ProcessMessage(msgObject);
                }
                catch (Exception e)
                {
                    ;
                    // Debug.LogError("解析错误");
                    // Debug.LogError(e.Message);
                }
                finally
                {
                    offset += Convert.ToInt32(length);
                }
            }
        }
        private void ProcessMessage(JObject msgObject)
        {
            string cmd = msgObject["cmd"].ToString();
            switch (cmd)
            {
                case "DANMU_MSG":
                    {
                        if (OnDanmaku != null)
                        {
                            OnDanmaku($"{msgObject["info"][2][1]}", $"{msgObject["info"][1]}", msgObject["info"][2][0].ToObject<long>());
                        }
                        break;
                    }
                case "SEND_GIFT":
                    {
                        JObject data = (JObject)msgObject.GetValue("data");
                        if(OnGift != null)
                        {
                            OnGift($"{data["uname"]}", $"{data["giftName"]}", data["super_gift_num"].ToObject<int>(), data["price"].ToObject<float>() / 1000f * msgObject["data"]["super_gift_num"].ToObject<float>());
                        }
                        break;
                    }
                case "SUPER_CHAT_MESSAGE":
                    {
                        JObject data = (JObject)msgObject.GetValue("data");
                        if(OnSuperChat != null)
                        {
                            OnSuperChat($"{data["user_info"]["uname"]}", data["price"].ToObject<int>(), $"{data["message"]}");
                        }
                        break;
                    }
                case "GUARD_BUY":
                    {
                        JObject data = (JObject)msgObject.GetValue("data");
                        if(OnGuard != null)
                        {
                            OnGuard(data["username"].ToString(), data["gift_name"].ToString());
                        }
                        break;
                    }
            }
        }
        /// <summary>
        /// 接收信息，会阻塞,要放到循环里
        /// </summary>
        public void Receive()
        {
            if (client.State == WebSocketState.Open)
            {
                byte[] buf = new byte[16777216];
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
                            // Debug.Log("连接成功");
                            timer.Enabled = true;
                            if (OnConnect != null)
                            {
                                OnConnect();
                            }
                            break;
                        }
                    case 5:
                        {
                            if (protocol == 3)
                            {
                                byte[] msgBuffer = new byte[16777216];
                                BrotliDecoder.TryDecompress(payload, msgBuffer, out int payloadTotalLength);
                                ProcessPayload(msgBuffer, payloadTotalLength);
                            }
                            break;
                        }
                }
            }
            else if (client.State == WebSocketState.Closed)
            {
                if (OnDisconnect != null)
                {
                    OnDisconnect(1);
                }
            }
        }
    }
}