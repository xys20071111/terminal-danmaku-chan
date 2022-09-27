namespace BLiveListenTool
{
    class Danmaku
    {
        public long color;
        public long bubble;
        public string msg;
        public long mode;
        public long fontsize;
        public long rnd;
        public long roomid;
        public string csrf;
        public string csrf_token;

        public static Danmaku CreateDanmaku(long roomId, string csrf, string text)
        {
            Danmaku danmaku = new Danmaku();
            danmaku.csrf = csrf;
            danmaku.csrf_token = csrf;
            danmaku.roomid = roomId;
            danmaku.rnd = new Random().NextInt64(35244353, 54534636);
            danmaku.fontsize = 24;
            danmaku.mode = 1;
            danmaku.bubble = 1;
            danmaku.color = 5816798;
            danmaku.msg = text;
            return danmaku;
        }
    }
    public class DanmakuSender
    {
        private readonly string csrf, buvid3, sessdata;
        private readonly long roomId;
        private HttpClient httpClient = new HttpClient(new HttpClientHandler() { UseCookies = true });
        private readonly Random rnd = new Random();
        
        public DanmakuSender(string csrf, string buvid3, string sessdata, long roomId)
        {
            this.csrf = csrf;
            this.buvid3 = buvid3;
            this.sessdata = sessdata;
            this.roomId = roomId;
            
        }

        public async void Send(string text)
        {
            if (text.Length > 18)
            {
                Send(text.Substring(0,18));
                var timer = new System.Timers.Timer();
                timer.Interval = 2500;
                timer.AutoReset = false;
                timer.Elapsed += (object _sender, System.Timers.ElapsedEventArgs _args) =>
                {
                    Send(text.Substring(18));
                };
                timer.Enabled = true;
                return;
            }
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(csrf), "csrf");
            content.Add(new StringContent(csrf), "csrf_token");
            content.Add(new StringContent(roomId.ToString()), "roomid");
            content.Add(new StringContent(rnd.NextInt64(324563452, 5645323563).ToString()), "rnd");
            content.Add(new StringContent("24"), "fontsize");
            content.Add(new StringContent("1"), "mode");
            content.Add(new StringContent("1"), "bubble");
            content.Add(new StringContent("5816798"), "color");
            content.Add(new StringContent(text), "msg");
            content.Headers.Add("Cookie", $"SESSDATA={sessdata}; buvid3={buvid3}; bili_jct={csrf}");
            await httpClient.PostAsync("https://api.live.bilibili.com/msg/send", content);
        }
    }
}