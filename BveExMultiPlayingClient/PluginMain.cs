using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BveEx.Extensions.Native;
using BveEx.Extensions.MapStatements;
using BveEx.PluginHost;
using BveEx.PluginHost.Plugins;
using BveTypes.ClassWrappers;
using System.Xml.Schema;
using System.Linq;
using BveEx.Extensions.PreTrainPatch;

namespace BveExMultiPlayingClient
{
    [Plugin(PluginType.MapPlugin)]
    public class PluginMain : AssemblyPluginBase
    {
        //BveEX自列車番号設定オリジナルマップ構文取得用
        private readonly IStatementSet Statements;
        //デバッグテキスト表示用
        private AssistantText debugText;
        //DDNSインターネット通信用
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        //private const string ServerUrl = "http://naruchanaout.clear-net.jp:5001/api/update";
        private const string ServerUrl = "http://naruchan-aout.softether.net:5001/api/update";
        //private const string ServerUrl = "http://133.32.217.166:5001/api/update";
        //自列車情報（送信用）
        TrainInfo myTrain = new TrainInfo();//自列車情報用インスタンスを生成

        private static readonly object lockObj = new object();
        private Timer sendTimer;

        //他列車情報（受信用）
        public static Dictionary<string, OtherTrainInfo> OtherTrainData { get; set; }

        private Timer receiveTimer;
        private readonly object trainMapLockObj = new object();

        //先行列車解除の有無用
        //private Train Train;
        //private PreTrainPatch PreTrainPatch;
        
        //先行列車解除の有無用クラス
        /*private class PreTrainLocationConverter : IPreTrainLocationConverter
        {
            private readonly Train SourceTrain;
            private readonly SectionManager SectionManager;

            public PreTrainLocationConverter(Train sourceTrain, SectionManager sectionManager)
            {
                SourceTrain = sourceTrain;
                SectionManager = sectionManager;
            }

            public PreTrainLocation Convert(PreTrainLocation source)
                => SourceTrain.TrainInfo.TrackKey == "0" ? PreTrainLocation.FromLocation(SourceTrain.Location, SectionManager) : source;
        }*/


        //コンストラクタ
        public PluginMain(PluginBuilder builder) : base(builder)
        {
            //BveEX自列車番号設定オリジナルマップ構文取得用
            Statements = Extensions.GetExtension<IStatementSet>();
            //デバッグテキスト表示用
            debugText = AssistantText.Create("");
            BveHacker.Assistants.Items.Add(debugText);
            //自列車情報（送信用）
            myTrain.ClientId = Guid.NewGuid().ToString();//ユーザーIDを発行、自列車情報用インスタンスmyTrainに設定
            //sendTimer = new Timer(SendDataToServer, null, 1000, 1000);//1秒ごとにデータ送信
            //他列車情報（受信用）
            OtherTrainData = new Dictionary<string, OtherTrainInfo>();//ここに移動
            //receiveTimer = new Timer(ReceiveOtherClientsData, null, 1000, 1000);//1秒ごとにデータ受信//Tickに移動

            //イベント購読
            BveHacker.ScenarioCreated += OnScenarioCreated;
        }

        //終了時処理
        public override void Dispose()
        {
            BveHacker.Assistants.Items.Remove(debugText);
            sendTimer?.Dispose();
            receiveTimer?.Dispose();
            BveHacker.ScenarioCreated -= OnScenarioCreated;
        }

        //フレーム毎処理
        public override void Tick(TimeSpan elapsed)
        {
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
            //デバッグテキスト表示用
            debugText.Text = "自列車番号: " + myTrain.TrainNumber +
                                 $"位置: {myTrain.Location:F1}m, 速度: {myTrain.Speed:F1}m/s";

            /*if (BveHacker.Scenario.Trains.ContainsKey("0523m"))
            {
                try
                {
                debugText.Text = "自列車番号: " + myTrain.TrainNumber +
                                    $"位置: {myTrain.Location:F1}m, 速度: {myTrain.Speed:F1}m/s" + Environment.NewLine +
                                 //"他列車番号: " + "0523m" + $" :{BveHacker.Scenario.Trains["0523m"].IsEnabled} " +
                                 //$"位置: {BveHacker.Scenario.Trains["0523m"].Location:F1}m, 速度: {BveHacker.Scenario.Trains["0523m"].Speed:F1}m/s";
                    $"他列車番号: {OtherTrainData["0523m"].TrainNumber} :{BveHacker.Scenario.Trains["0523m"].IsEnabled} ,受信他列車:{OtherTrainData.Count} ,0523m受信:{OtherTrainData.ContainsKey("0523m")}" +
                    $"位置: {OtherTrainData["0523m"].Location:F1}m, 速度: {OtherTrainData["0523m"].Speed:F1}m/s";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラー: {ex.Message}");
                }
            }
            if (BveHacker.Scenario.Trains.ContainsKey("0521m"))
            {
                try
                {
                debugText.Text = "自列車番号: " + myTrain.TrainNumber +
                                    $"位置: {myTrain.Location:F1}m, 速度: {myTrain.Speed:F1}m/s" + Environment.NewLine +
                                 //"他列車番号: " + "0521m" + $" :{BveHacker.Scenario.Trains["0521m"].IsEnabled} " +
                                    //$"位置: {BveHacker.Scenario.Trains["0521m"].Location:F1}m, 速度: {BveHacker.Scenario.Trains["0521m"].Speed:F1}m/s";
                    $"他列車番号: {OtherTrainData["0521m"].TrainNumber} :{BveHacker.Scenario.Trains["0521m"].IsEnabled} ,受信他列車:{OtherTrainData.Count} ,0521m受信:{OtherTrainData.ContainsKey("0521m")}" +
                    $"位置: {OtherTrainData["0521m"].Location:F1}m, 速度: {OtherTrainData["0521m"].Speed:F1}m/s";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラー: {ex.Message}");
                }
            }*/

        }

        //シナリオ作成イベント購読時の処理
        private void OnScenarioCreated(ScenarioCreatedEventArgs e)
        {
            sendTimer = new Timer(SendDataToServer, null, 1000, 1000);//1秒ごとにデータ送信
            receiveTimer = new Timer(ReceiveOtherClientsData, null, 1000, 1000);//1秒ごとにデータ受信
            //BveEX自列車番号設定オリジナルマップ構文取得用
            Statement put = Statements.FindUserStatement("YUtrain",
                ClauseFilter.Element("MultiPlaying", 0),
                ClauseFilter.Function("TrainNumber", 1));
            MapStatementClause function = put.Source.Clauses[put.Source.Clauses.Count - 1];
            myTrain.TrainNumber = function.Args[0] as string;//自列車情報用インスタンスmyTrainに列車番号を設定

            //先行列車解除の有無
            //Train = e.Scenario.Trains[]
            //SectionManager sectionManager = e.Scenario.SectionManager;
            //PreTrainPatch = Extensions.GetExtension<IPreTrainPatchFactory>().Patch(nameof(PreTrainPatch), sectionManager, new PreTrainLocationConverter(Train, sectionManager));

        }

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
    }

    //各列車インスタンス用の情報クラス
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
    public class OtherTrainInfo
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
    }
}
