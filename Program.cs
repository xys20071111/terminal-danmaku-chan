using Terminal.Gui;
using Newtonsoft.Json;
namespace TerminalDanmakuChan
{
    public class Program
    {
        public static void Main(string[] args)
        {
            List<String> danmakuList = new List<string>();
            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(args[0]));
            DanmakuReceiver danmakuReceiver = new DanmakuReceiver(args.Length > 1 ? int.Parse(args[1]) : config.roomId);
            DanmakuSender danmakuSender = new DanmakuSender(config.csrf, config.buvid3, config.sessdata, args.Length > 1 ? long.Parse(args[1]) : config.roomId);
            Application.Init();
            Toplevel top = Application.Top;
            Colors.Base.Normal = Application.Driver.MakeAttribute(Color.Green, Color.Black);
            MenuBar menuBar = new MenuBar(new MenuBarItem[] {
                new MenuBarItem("退出", "", () => {
                    if(Quit())
                    {
                        top.Running = false;
                    }
                })
            })
            {
                Width = Dim.Fill(),
                Height = Dim.Percent(5)
            };
            FrameView mainWindow = new FrameView($"终端弹幕酱 房间{(args.Length > 1 ? long.Parse(args[1]) : config.roomId)}")
            {
                Width = Dim.Fill(),
                Height = Dim.Percent(95),
                Y = Pos.Bottom(menuBar)
            };
            FrameView danmakuArea = new FrameView("弹幕")
            {
                Width = Dim.Fill(),
                Height = Dim.Percent(90)
            };
            ListView danmakuListView = new ListView(danmakuList)
            {
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            FrameView sendArea = new FrameView("发送弹幕")
            {
                Width = Dim.Fill(),
                Height = Dim.Percent(15),
                Y = Pos.Bottom(danmakuArea)
            };
            TextField danmakuInput = new TextField("")
            {
                Width = Dim.Percent(90),
                Height = Dim.Fill()
            };
            Button sendDanmaku = new Button("发送", true)
            {
                Height = Dim.Fill(),
                Width = Dim.Percent(10),
                X = Pos.Right(danmakuInput)
            };
            danmakuArea.Add(danmakuListView);
            sendArea.Add(danmakuInput);
            sendArea.Add(sendDanmaku);
            mainWindow.Add(sendArea);
            mainWindow.Add(danmakuArea);
            sendDanmaku.Clicked += () =>
            {
                danmakuSender.Send(danmakuInput.Text.ToString());
                danmakuInput.Text = "";
            };
            danmakuReceiver.OnConnect += () =>
            {
                danmakuList.Add("已连接到服务器");
            };
            danmakuReceiver.OnDanmaku += (string uname, string text, long _) =>
            {
                danmakuList.Add($"{uname}: {text}");
                danmakuListView.SelectedItem = danmakuList.Count - 1;
                if (danmakuList.Count > 16)
                {
                    danmakuListView.ScrollDown(1);
                }
            };
            danmakuReceiver.OnGift += (string uname, string name, int count, float price) =>
            {
                danmakuList.Add($"{uname}投喂了{count}个{name}, 共{price}元");
                danmakuListView.SelectedItem = danmakuList.Count - 1;
                if (danmakuList.Count > 16)
                {
                    danmakuListView.ScrollDown(1);
                }
            };
            // danmakuReceiver.OnSuperChat += (string uname, string text, string price) =>
            // {
            //     danmakuList.Add($"醒目留言 {price}元 {uname}: {text}");
            //     danmakuListView.SelectedItem = danmakuList.Count - 1;
            //     if (danmakuList.Count > 16)
            //     {
            //         danmakuListView.ScrollDown(1);
            //     }
            // };
            danmakuReceiver.OnGuard += (string name, string type) =>
            {
                danmakuList.Add($"{name}开通了{type}");
                danmakuListView.SelectedItem = danmakuList.Count - 1;
                if (danmakuList.Count > 16)
                {
                    danmakuListView.ScrollDown(1);
                }
            };
            danmakuReceiver.OnDisconnect += (int type) => {
                if (type != 0)
                {
                    danmakuReceiver.Connect();
                }
            };
            top.Add(mainWindow);
            top.Add(menuBar);
            danmakuReceiver.Connect();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            Thread receiveThread = new Thread(() => {
                while(true)
                {
                    if(cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    danmakuReceiver.Receive();
                }
            });
            receiveThread.Start();
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), (MainLoop caller) =>
            {
                danmakuListView.Visible = false;
                danmakuListView.Visible = true;
                return true;
            });
            Application.Run();
            cancellationTokenSource.Cancel();
            Application.Shutdown();
        }
        static bool Quit()
        {
            var n = MessageBox.Query(50, 7, "退出", "确定要退出吗", "是", "否");
            return n == 0;
        }
    }
}