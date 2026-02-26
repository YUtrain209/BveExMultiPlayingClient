using System;
using System.Collections.Generic;
//using System.IO;
//using System.Net.Http;
//using System.Text;
//using System.Text.Json;
//UDP追加
using System.Net;
using System.Net.Sockets;

using System.Threading;
using System.Threading.Tasks;
//using BveEx.Extensions.Native;
//using BveEx.Extensions.MapStatements;
using BveEx.PluginHost;
using BveEx.PluginHost.Plugins;
using BveTypes.ClassWrappers;
//using System.Xml.Schema;
//using System.Linq;
//using BveEx.Extensions.PreTrainPatch;

namespace BveExMultiPlayingClient
{
    [Plugin(PluginType.MapPlugin)]
    public class PluginMain : AssemblyPluginBase
    {
        //UDP追加
        private UdpClient udp;
        private IPEndPoint serverEP;
        private Task receiveTask;
        private bool running = true;
        private Guid clientId = Guid.NewGuid();

        public static Dictionary<Guid, OtherTrainInfo> OtherTrainData
            = new Dictionary<Guid, OtherTrainInfo>();

        //BveEX自列車番号設定オリジナルマップ構文取得用
        //private readonly IStatementSet Statements;
        //デバッグテキスト表示用
        private AssistantText debugText;
        //DDNSインターネット通信用
        //private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        //private const string ServerUrl = "http://naruchanaout.clear-net.jp:5001/api/update";
        //private const string ServerUrl = "http://naruchan-aout.softether.net:5001/api/update";
        //private const string ServerUrl = "http://133.32.217.166:5001/api/update";
        //自列車情報（送信用）
        //TrainInfo myTrain = new TrainInfo();//自列車情報用インスタンスを生成

        //private static readonly object lockObj = new object();
        private Timer sendTimer;

        //他列車情報（受信用）
        //public static Dictionary<string, OtherTrainInfo> OtherTrainData { get; set; }

        //private Timer receiveTimer;
        private readonly object trainMapLockObj = new object();

        //コンストラクタ
        public PluginMain(PluginBuilder builder) : base(builder)
        {
            //BveEX自列車番号設定オリジナルマップ構文取得用
            //Statements = Extensions.GetExtension<IStatementSet>();
            //デバッグテキスト表示用
            debugText = AssistantText.Create("");
            BveHacker.Assistants.Items.Add(debugText);
            //自列車情報（送信用）
            //myTrain.ClientId = Guid.NewGuid().ToString();//ユーザーIDを発行、自列車情報用インスタンスmyTrainに設定
            //他列車情報（受信用）
            //OtherTrainData = new Dictionary<string, OtherTrainInfo>();//ここに移動

            //イベント購読
            BveHacker.ScenarioCreated += OnScenarioCreated;
        }

        //終了時処理
        public override void Dispose()
        {
            //UDP追加
            running = false;
            udp?.Close();

            BveHacker.Assistants.Items.Remove(debugText);
            sendTimer?.Dispose();
            //receiveTimer?.Dispose();
            BveHacker.ScenarioCreated -= OnScenarioCreated;
        }

        //フレーム毎処理
        public override void Tick(TimeSpan elapsed)
        {
            //UDP追加
            lock (trainMapLockObj)
            {
                foreach (var kv in OtherTrainData)
                {
                    var train = kv.Value;
                    train.Location += train.Speed * elapsed.TotalSeconds;
                }

                foreach (var trains in BveHacker.Scenario.Trains)
                {
                    foreach (var other in OtherTrainData)
                    {
                        if (other.Key.ToString() == trains.Key)
                        {
                            trains.Value.Location = other.Value.Location;
                            trains.Value.Speed = other.Value.Speed;
                        }
                    }
                }
            }
            /*
            ApplyReceivedData(elapsed);
            if (OtherTrainData != null)
            {
                foreach (var trains in BveHacker.Scenario.Trains)
                {
                    foreach (var otherTrainData in OtherTrainData)
                    {
                        if (otherTrainData.Key == trains.Key)
                        {
                            trains.Value.Location = otherTrainData.Value.Location;
                            trains.Value.Speed = otherTrainData.Value.Speed;
                        }
                    }
                }
            }
            */
            //デバッグテキスト表示用
            //UDP追加
            debugText.Text =
                $"自位置: {BveHacker.Scenario.VehicleLocation.Location:F1}m\n" +
                $"自速度: {BveHacker.Scenario.VehicleLocation.Speed:F1}m/s\n" +
                $"接続数: {OtherTrainData.Count}";
            /*
            debugText.Text = "自列車番号: " + myTrain.TrainNumber +
                                 $"位置: {myTrain.Location:F1}m, 速度: {myTrain.Speed:F1}m/s";
            */
        }

        //シナリオ作成イベント購読時の処理
        private void OnScenarioCreated(ScenarioCreatedEventArgs e)
        {
            //UDP追加
            udp = new UdpClient();
            serverEP = new IPEndPoint(
                IPAddress.Parse("127.0.0.1" +
                ""),
                5005);

            sendTimer = new Timer(SendData, null, 0, 50);
            receiveTask = Task.Run(ReceiveLoop);
            /*
            sendTimer = new Timer(SendDataToServer, null, 1000, 1000);//1秒ごとにデータ送信
            receiveTimer = new Timer(ReceiveOtherClientsData, null, 1000, 1000);//1秒ごとにデータ受信
            //BveEX自列車番号設定オリジナルマップ構文取得用
            Statement put = Statements.FindUserStatement("YUtrain",
                ClauseFilter.Element("MultiPlaying", 0),
                ClauseFilter.Function("TrainNumber", 1));
            MapStatementClause function = put.Source.Clauses[put.Source.Clauses.Count - 1];
            myTrain.TrainNumber = function.Args[0] as string;//自列車情報用インスタンスmyTrainに列車番号を設定
            */
        }

        //UDP追加
        private void SendData(object state)
        {
            try
            {
                TrainPacket packet = new TrainPacket
                {
                    ClientId = clientId,
                    TrackPosition = BveHacker.Scenario.VehicleLocation.Location,
                    Speed = (float)BveHacker.Scenario.VehicleLocation.Speed,
                    Length = 200,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                byte[] data = packet.ToBytes();
                udp.SendAsync(data, data.Length, serverEP);
            }
            catch { }
        }

        private async Task ReceiveLoop()
        {
            while (running)
            {
                try
                {
                    var result = await udp.ReceiveAsync();

                    if (result.Buffer.Length != 38)
                        continue;

                    var packet = TrainPacket.FromBytes(result.Buffer);

                    if (packet.ClientId == clientId)
                        continue;

                    HandleOtherTrain(packet);
                }
                catch { }
            }
        }

        private void HandleOtherTrain(TrainPacket packet)
        {
            lock (trainMapLockObj)
            {
                if (!OtherTrainData.ContainsKey(packet.ClientId))
                {
                    OtherTrainData[packet.ClientId] = new OtherTrainInfo();
                }

                OtherTrainData[packet.ClientId].Location = packet.TrackPosition;
                OtherTrainData[packet.ClientId].Speed = packet.Speed;
            }
        }
        /*
        //自列車情報（送信用）イベント（1秒ごと）←次ここから書く（Location,Speedに関してはまずは1秒おき）毎フレームリスト化しない！
        private async void SendDataToServer(object state)
        {
            //自列車情報（位置,速度）を取得、自列車情報用インスタンスmyTrainに設定
            myTrain.Location = BveHacker.Scenario.VehicleLocation.Location;
            myTrain.Speed = BveHacker.Scenario.VehicleLocation.Speed;
            //各自列車情報をリスト化

            var clientData = new TrainInfo();
            clientData = myTrain;
            string jsonData = JsonSerializer.Serialize(clientData);

            //自列車情報（送信用）をJSON形式に変換、送信
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await client.PostAsync(ServerUrl, content);
                Console.WriteLine($"送信ステータス: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"送信エラー: {ex.Message}");
            }
        }

        //他列車情報（受信用）イベント（1秒ごと）
        private async void ReceiveOtherClientsData(object state)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(ServerUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    var receivedClients = JsonSerializer.Deserialize<List<OtherTrainInfo>>(jsonData);
                    if (receivedClients != null)
                    {
                        lock (trainMapLockObj)
                        {
                            OtherTrainData = receivedClients.Where(x => x.ClientId != myTrain.ClientId).ToDictionary(x => x.TrainNumber, x => x);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"受信エラー: {ex.Message}");
            }
        }

        private void ApplyReceivedData(TimeSpan elapsed)
        {
            lock (trainMapLockObj)
            {
                if (OtherTrainData != null)
                {
                    foreach (var otherTrainData in OtherTrainData)
                    {
                        //var trainNumber = otherTrainData.Key;
                        otherTrainData.Value.Location += otherTrainData.Value.Speed * elapsed.TotalSeconds;
                    }
                }
            }
        }
        */
    }

    //各列車インスタンス用の情報クラス
    /*
    public class TrainInfo
    {
        //フィールド
        //UserID
        private string clientId = "";
        //列車番号
        private string trainNumber = "";
        //位置
        private double location = 0;
        //速度
        private double speed = 0;

        //情報の設定
        public void SetInfo(string clientId, string trainNumber, double location, double speed)
        {
            this.clientId = clientId;
            this.trainNumber = trainNumber;
            this.location = location;
            this.speed = speed;
        }

        //全列車情報の表示・取得
        public void ShowInfo()
        {

        }

        //情報の設定（・取得）
        public string ClientId
        {
            set { clientId = value; }
            get { return clientId; }
        }
        public string TrainNumber
        {
            set { trainNumber = value; }
            get { return trainNumber; }
        }
        public double Location
        {
            set { location = value; }
            get { return location; }
        }
        public double Speed
        {
            set { speed = value; }
            get { return speed; }
        }
    }
    */
    public class OtherTrainInfo
    {
        //フィールド
        //UDP追加
        public double Location { get; set; }
        public float Speed { get; set; }
        /*
        //UserID
        private string clientId = "";
        //列車番号
        private string trainNumber = "";
        //位置
        private double location = 0;
        //速度
        private double speed = 0;

        //情報の設定
        public void SetInfo(string clientId, string trainNumber, double location, double speed)
        {
            this.clientId = clientId;
            this.trainNumber = trainNumber;
            this.location = location;
            this.speed = speed;
        }

        //全列車情報の表示・取得
        public void ShowInfo()
        {

        }

        //情報の設定（・取得）
        public string ClientId
        {
            set { clientId = value; }
            get { return clientId; }
        }
        public string TrainNumber
        {
            set { trainNumber = value; }
            get { return trainNumber; }
        }
        public new double Location
        {
            set { location = value; }
            get { return location; }
        }
        public double Speed
        {
            set { speed = value; }
            get { return speed; }
        }
        */
    }
    //UDP追加
    struct TrainPacket
    {
        public Guid ClientId;
        public double TrackPosition;
        public float Speed;
        public short Length;
        public long Timestamp;

        public byte[] ToBytes()
        {
            byte[] buffer = new byte[38];

            Array.Copy(ClientId.ToByteArray(), 0, buffer, 0, 16);
            Array.Copy(BitConverter.GetBytes(TrackPosition), 0, buffer, 16, 8);
            Array.Copy(BitConverter.GetBytes(Speed), 0, buffer, 24, 4);
            Array.Copy(BitConverter.GetBytes(Length), 0, buffer, 28, 2);
            Array.Copy(BitConverter.GetBytes(Timestamp), 0, buffer, 30, 8);

            return buffer;
        }

        public static TrainPacket FromBytes(byte[] buffer)
        {
            TrainPacket p = new TrainPacket();

            byte[] guidBytes = new byte[16];
            Array.Copy(buffer, 0, guidBytes, 0, 16);
            p.ClientId = new Guid(guidBytes);

            p.TrackPosition = BitConverter.ToDouble(buffer, 16);
            p.Speed = BitConverter.ToSingle(buffer, 24);
            p.Length = BitConverter.ToInt16(buffer, 28);
            p.Timestamp = BitConverter.ToInt64(buffer, 30);

            return p;
        }
    }
}
