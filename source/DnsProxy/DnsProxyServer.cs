using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Remoting.Contexts;
using System.Data;
using static DnsProxyLibrary.DataBase;
using static System.Net.Mime.MediaTypeNames;
using System.Timers;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Xml.Linq;

namespace DnsProxyLibrary
{
    public class DnsProxyServer
    {
        class PacketData
        {
            public byte[] bytes;
            public IPEndPoint senderEndPoint;
            public byte[] originalTransactionID = new byte[2];
            public ushort TransactionID;
            public DateTime Time;
        }


        CancellationTokenSource cts = new CancellationTokenSource();
        UdpClient udpServer;
        UdpClient udpClient;
        Thread serverRecvThread;
        Thread clientRecvThread;
        Thread oneSecThread;

        IPEndPoint serverEP;
        IPEndPoint clientEP;
        IPEndPoint remoteEP;

        Dictionary<string,DateTime> recvMap = new Dictionary<string, DateTime>();
        Dictionary<ushort, PacketData> transactionMap = new Dictionary<ushort, PacketData>();
        AutoResetEvent eventRecv = new AutoResetEvent(false);
        List<PacketData> recvPacketList = new List<PacketData>();
        ushort TransactionID = 1;


        private readonly List<HistoryData> SetHistoryList = new List<HistoryData>();
        private readonly List<HistoryData> historyList = new List<HistoryData>();
        private readonly DataBase dataBase = new DataBase();
        private string configPath;
        private string basePath;
        private bool bModifyed = false;
        private bool bSetHistoryModifyed = false;
        private NamedPipe namedPipe = new NamedPipe();

        public delegate void ConnectFunc(object param, bool bConnect);
        public delegate void ReceiveFunc(Command cmd, object param);
        public delegate void StopFunc(object param);
        private ReceiveFunc _recvFunc;
        private ConnectFunc _connectFunc;
        private StopFunc _stopFunc;
        private object _funcParam;
        private bool bProxyEnable = true;
        private bool bHostoryEnable = false;
        private Statistics  statistics;


        public void Start(string base_path, ConnectFunc connectFunc, ReceiveFunc recvFunc, StopFunc stopFunc, object funcParam)
        {
            this.basePath = base_path;
            this._connectFunc = connectFunc;
            this._recvFunc = recvFunc;
            this._stopFunc = stopFunc;
            this._funcParam = funcParam;

            if(this.basePath.Substring(this.basePath.Length - 1, 1) != "\\")
            {
                this.basePath += "\\";
            }
            this.configPath = this.basePath + "config.ini";

            _ = Task.Run(() =>
            {
                Load();


                try
                {
                    this.statistics = new Statistics();

                    this.namedPipe.StartServer(Common.pipeGuid, PipeConnect, PipeReceiveAsync, this);

                    this.serverEP = new IPEndPoint(IPAddress.Any, 53);
                    this.udpServer = new UdpClient(this.serverEP);

                    this.clientEP = new IPEndPoint(IPAddress.Any, 0);

                    this.udpClient = new UdpClient(this.clientEP);

                    this.clientRecvThread = new Thread(new ParameterizedThreadStart(RecvThread));
                    this.clientRecvThread.Name = "clientRecvThread";
                    this.clientRecvThread.Start(this.udpClient);
                    
                    this.serverRecvThread = new Thread(new ParameterizedThreadStart(RecvThread));
                    this.serverRecvThread.Name = "ServerRecvThread";
                    this.serverRecvThread.Start(this.udpServer);

                    this.oneSecThread = new Thread(new ParameterizedThreadStart(TimerThread));
                    this.oneSecThread.Name = "OneSecThread";
                    this.oneSecThread.Start();
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.Start - Exception({0})\n", e.Message);
                    if (this._stopFunc != null)
                    {
                        this._stopFunc(this._funcParam);
                    }
                }
            });
        }



        public void Stop()
        {
            this.cts.Cancel();
            this.udpServer?.Close();
            this.udpClient?.Close();
            this.eventRecv.Set();

            this.serverRecvThread?.Join();
            this.clientRecvThread?.Join();
            this.oneSecThread?.Join();


            Save();

            this.namedPipe.Stop();

            this.udpServer?.Dispose();
            this.udpClient?.Dispose();
            this.cts.Dispose();

        }

        void LoadSetHistory(string path)
        {
            lock(this.SetHistoryList)
            {
                this.SetHistoryList.Clear();

                do
                {
                    if(!File.Exists(path))
                    {
                        break;
                    }

                    try
                    {
                        using(FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                        {
                            using(StreamReader reader = new StreamReader(fs))
                            {
                                while(true)
                                {
                                    string s = reader.ReadLine();
                                    if(s == null)
                                    {
                                        break;
                                    }

                                    HistoryData data = new HistoryData();
                                    if(data.FromString(s))
                                    {
                                        this.SetHistoryList.Add(data);
                                    }
                                }
                            }

                        }
                    }
                    catch(Exception e)
                    {
                        DBG.MSG("DnsProxyServer.LoadSetHistory - Exception({0})\n", e.Message);
                    }
                }
                while(false);
            }
        }

        void SaveSetHistory(string path)
        {
            lock(this.SetHistoryList)
            {
                try
                {

                    using(FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        fs.SetLength(0);

                        using(StreamWriter writer = new StreamWriter(fs))
                        {

                            foreach(var v in this.SetHistoryList.ToArray())
                            {
                                writer.WriteLine(v.ToString());
                            }
                        }
                    }
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.SaveSetHistory - Exception({0})\n", e.Message);
                }

            }
        }


        private void RecvThread (object obj)
        {
            UdpClient udp = (UdpClient)obj;


            for(; !this.cts.IsCancellationRequested;)
            {
                try
                {
#if false
                    UdpReceiveResult receiveResult = await udp.ReceiveAsync ();
                    IPEndPoint ep = receiveResult.RemoteEndPoint;
                    byte[] bytes = receiveResult.Buffer;
#else
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = udp.Receive(ref ep);
#endif

                    PacketData data = new PacketData();

                    data.Time = DateTime.Now;
                    data.senderEndPoint = new IPEndPoint (ep.Address, ep.Port);
                    data.bytes = new byte[bytes.Length];
                    bytes.CopyTo(data.bytes, 0);

                    ParseAndRelay (ref data);
                }
                catch(System.Net.Sockets.SocketException e)
                {
                    DBG.MSG("DnsProxyServer.RecvThread - SocketException, HResult={0:X8}, {1}\n", e.HResult, e.ToString());
                }
                catch(ObjectDisposedException e)
                {
                    DBG.MSG("DnsProxyServer.RecvThread - ObjectDisposedException, {0}\n", e.Message);
                }
                catch(AggregateException e)
                {
                    foreach (var v in e.InnerExceptions)
                    {
                        DBG.MSG("DnsProxyServer.RecvThread - AggregateException, {0}, {1}\n", v.HResult, v.Message);
                    }

                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.RecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                    Debug.Assert(false);
                }
            }

        }

        [DllImport("kernel32.dll") ]
        public static extern UInt64 GetTickCount64();
        
        private void TimerThread (object obj)
        {
            UInt64 tickCountNow = 0;
            UInt64 tickCount = 0;
            UInt64 interval = 1000;
            int sec = 0;

            DBG.MSG("DnsProxyServer.TimerThread - START\n");

            tickCount = GetTickCount64() + interval;


            for(; !this.cts.IsCancellationRequested;)
            {
                tickCountNow = GetTickCount64();

                if (tickCountNow >= tickCount)
                {
                    OnTimer(sec++);

                    if (tickCountNow >= (tickCount + (interval * 2)))
                {
                        tickCount += (tickCountNow - tickCount) / interval * interval;
                        sec = 0;
                }

                    else
                    {
                        tickCount += interval;
                    }
                }
                else
                {
                    int diff = (int)(tickCount - tickCountNow);

                    if (diff > 100)
                {
                        Thread.Sleep (100);
                }
                    else 
                {
                        Thread.Sleep (10);
                }
            }

        }


            DBG.MSG("DnsProxyServer.TimerThread - END\n");
                }

        private void OnTimer (int sec)
        {
            //DBG.MSG("DnsProxyServer.OnTimer - START({0})\n", sec);
            DateTime timeNow = DateTime.Now;

                Save();

                //timeout
            lock(this.transactionMap)
                {
                TimeSpan timeSpan = new TimeSpan(0, 0, 10);
                List<KeyValuePair<ushort, PacketData>> list = new List<KeyValuePair<ushort, PacketData>>();

                foreach(var v in this.transactionMap)
                    {
                        if((timeNow - v.Value.Time) > timeSpan)
                        {
                        list.Add(v);
                        }
                    }

                    foreach(var v in list)
                    {
                    if(this.transactionMap.Remove(v.Key))
                        {
                        DBG.MSG("DnsProxyServer.OnTimer - transactionMap.Timeout, Time={0}, TransactionID=0x{1:X2}\n", v.Value.Time, v.Value.TransactionID);
                    }
                }
            }

            lock (this.recvMap)
            {
                List<KeyValuePair<string, DateTime>> list = new List<KeyValuePair<string, DateTime>>();
                TimeSpan timeSpan = new TimeSpan(0, 0, 5);

                foreach (var v in this.recvMap)
                {
                    if ((timeNow - v.Value) > timeSpan)
                    {
                        list.Add (v);
                    }
                    }

                foreach (var v in list)
                    {
                    if (this.recvMap.Remove (v.Key))
                        {
                        DBG.MSG ("DnsProxyServer.OnTimer - recvMap.Timeout, Time={0}, key={1}\n", v.Value, v.Key);
                        }
                    }

                        }

            //DBG.MSG("DnsProxyServer.OnTimer - END\n");
        }



        private void ParseAndRelay(ref PacketData data)
        {
            DateTime timeNow = DateTime.Now;

            DBG.MSG ("\n");
            DBG.MSG ("DnsProxyServer.ParseAndRelay - START, {0}\n", data.senderEndPoint.ToString ());
                        //DBG.DUMP(data.bytes, data.bytes.Length);

            do
            {

                        DnsProtocol dns = new DnsProtocol();
                        dns.Parse(data.bytes);

                string key = string.Format("{0:X4},{1}", dns.header.TransactionID, data.senderEndPoint);

                lock (this.recvMap)
                {
                    if (this.recvMap.ContainsKey (key))
                    {
                        DBG.MSG ("DnsProxyServer.ParseAndRelay - duplicate Request, {0}\n", key);
                        break;
                    }

                    this.recvMap.Add (key, timeNow);
                }

                        if(dns.header.IsQuery())
                        {
                    ushort tID;

                    this.statistics.query++;

                    DBG.MSG ("DnsProxyServer.ParseAndRelay - Query {0}\n", data.senderEndPoint);
                            if(!IsAcceptAsync(dns, data.senderEndPoint.ToString()))
                            {
                        this.statistics.reject++;

                                data.bytes[0x02] |= 0x80;
                                data.bytes[0x03] |= 0x03;

                        DBG.MSG ("DnsProxyServer.ParseAndRelay - udpServer.SendAsync(not found) {0}\n", data.senderEndPoint);
                        //_ = this.udpServer.SendAsync (data.bytes, data.bytes.Length, data.senderEndPoint);
                        this.udpServer.Send (data.bytes, data.bytes.Length, data.senderEndPoint);

                                break;
                            }

                    lock (this.transactionMap)
                            {
                        while (this.transactionMap.ContainsKey (this.TransactionID))
                                {
                                    Debug.Assert(false);
                            this.TransactionID++;

                            }

                        if (this.TransactionID == 0)
                            {
                            this.TransactionID = 1;
                        }
                        
                        tID = this.TransactionID++;

                        data.Time = timeNow;
                        data.TransactionID = tID;
                        this.transactionMap.Add (tID, data);
                            }

                    Buffer.BlockCopy (data.bytes, 0, data.originalTransactionID, 0, 2);

                    data.bytes[0] = (byte)((tID & 0xFF00) >> 8);//TransactionID
                    data.bytes[1] = (byte)((tID & 0x00FF) >> 0);//TransactionID

                    DBG.MSG ("DnsProxyServer.ParseAndRelay - Query TransactionID=0x{0:X2}\n", tID);

                    DBG.MSG ("DnsProxyServer.ParseAndRelay - udpClient.SendAsync {0}\n", this.remoteEP);
                    //_ = this.udpClient.SendAsync (data.bytes, data.bytes.Length, this.remoteEP);
                    this.udpClient.Send (data.bytes, data.bytes.Length, this.remoteEP);
                    this.statistics.accept++;
                        }
                        else
                        {
                    DBG.MSG ("DnsProxyServer.ParseAndRelay - Response\n");
                    this.statistics.answer++;

                            for(int i = 0; i < dns.answers.Count; i++)
                            {
                                var v = dns.answers[i];

                                bool bMod = false;
                                string comment = "";
                                DataBase db = this.dataBase.Find(v.Name, false, ref bMod);
                                while(db != null)
                                {
                                    comment = db.GetComment();
                                    break;
                                    //if (!string.IsNullOrEmpty (comment))
                                    //{
                                    //    break;
                                    //}

                                    //db = db.GetParent();
                                }

                                HistoryAdd(FLAGS.Answer, data.senderEndPoint.Address.ToString(), v.Name, string.Format("{0}: {1}, {2}", i + 1, v.Type.ToString(), v.typeData.ToDetail()), comment);
                            }


                    lock (this.transactionMap)
                            {
                        if (this.transactionMap.TryGetValue(dns.header.TransactionID, out PacketData p) && this.transactionMap.Remove (dns.header.TransactionID))
                                {
                            Buffer.BlockCopy (p.originalTransactionID, 0, data.bytes, 0, 2);
                            DBG.MSG ("DnsProxyServer.ParseAndRelay - udpServer.SendAsync TransactionID=0x{0:X2}{1:X2}, {2}\n", p.originalTransactionID[0], p.originalTransactionID[1], p.senderEndPoint);
                            //_ = this.udpServer.SendAsync (data.bytes, data.bytes.Length, value.senderEndPoint);
                            this.udpServer.Send (data.bytes, data.bytes.Length, p.senderEndPoint);
                            DBG.MSG ("DnsProxyServer.ParseAndRelay - TryRemove, TransactionID=0x{0:X4}\n", dns.header.TransactionID);
                                }
                                else
                                {
                            DBG.MSG ("DnsProxyServer.ParseAndRelay - TryRemove, TransactionID=0x{0:X4} not found\n", dns.header.TransactionID);
                            Debug.Assert (false);
                    }
                }
                }
            }

            while (false);

            DBG.MSG("DnsProxyServer.ParseAndRelay - END, {0}\n", data.senderEndPoint.ToString ());
            DBG.MSG("\n");
        }

        private bool IsAcceptAsync(DnsProtocol dns, string ip)
        {
            bool result = false;

            for(int i = 0; i < dns.questions.Count; i++)
            {
                var v = dns.questions[i];

                DataBase.FLAGS flags = DataBase.FLAGS.None;
                bool bMod = false;
                DataBase db = this.dataBase.Find(v.Name, true, ref bMod);
                string comment = db.GetComment();
                db.UpdateDatetime();

                if(bMod)
                {
                    this.bModifyed = true;
                    byte[] b = Command.Create(CMD.ADD, new byte[] { (byte)db.GetFlags() }, db.GetFullName());
                    this.namedPipe.WriteDataAsync(b, 0, b.Length);
                }
                else
                {
                    byte[] b = Command.Create(CMD.DATETIME, null, db.GetFullName());
                    this.namedPipe.WriteDataAsync(b, 0, b.Length);
                }


                while(db != null)
                {
                    flags = db.GetFlags();

                    if(flags != DataBase.FLAGS.None)
                    {
                        break;
                    }

                    db = db.GetParent();
                }

                if(!this.bProxyEnable)
                {
                    DBG.MSG("DnsProxyServer.IsAcceptAsync - host={0}, {1} --> Accept(Proxy Disable)\n", v.Name, flags);
                    HistoryAdd(FLAGS.Disable, ip, v.Name, string.Format("{0}: {1}", i + 1, v.Type.ToString()), comment);

                    result = true;
                }
                else
                {
                    DBG.MSG("DnsProxyServer.IsAcceptAsync - host={0}, {1}\n", v.Name, flags);
                    HistoryAdd(flags, ip, v.Name, string.Format("{0}: {1}", i + 1, v.Type.ToString()), comment);

                    if(flags == DataBase.FLAGS.Accept)
                    {
                        result = true;
                    }
                    else if((flags == DataBase.FLAGS.Reject) || (flags == DataBase.FLAGS.Ignore))
                    {
                        //1つでもREJECT/IGNOREがあれば許可しない
                        result = false;
                        break;
                    }
                }
            }

            return result;
        }

        void HistoryAdd(DataBase.FLAGS flags, string ip, string host, string info, string comment)
        {
            HistoryData data = new HistoryData();
            data.time = DateTime.Now;
            data.flags = flags;
            data.ip = ip;
            data.host = host;
            data.info = info;
            data.comment = comment;

            switch(flags)
            {
            case DataBase.FLAGS.SetNone:
            case DataBase.FLAGS.SetAccept:
            case DataBase.FLAGS.SetReject:
            case DataBase.FLAGS.SetIgnore:
                {
                    lock(this.SetHistoryList)
                    {

                        this.SetHistoryList.Add(data);
                        if(this.SetHistoryList.Count > 3000)
                        {
                            this.SetHistoryList.RemoveAt(0);
                        }
                        this.bSetHistoryModifyed = true;
                    }
                }
                break;
            }


            lock(this.historyList)
            {
                this.historyList.Add(data);
                if(this.historyList.Count > 1000)
                {
                    this.historyList.RemoveRange(0, this.historyList.Count - 1000);
                }
            }

            if(this.bHostoryEnable)
            {
                HistorySend(data);
            }
        }

        void HistorySend(HistoryData data)
        {
            byte[] b = Command.Create(CMD.HISTORY, new byte[] { (byte)data.flags }, data.ToString());
            this.namedPipe.WriteDataAsync(b, 0, b.Length);
        }

        void Load()
        {
            Config config = new Config();

            config.Load(this.configPath);

            string base_path = (string)config.GetValue(Config.Name.server_base_path, "");
            if(Directory.Exists(base_path))
            {
                this.basePath = base_path;
                if(this.basePath.Substring(this.basePath.Length - 1, 1) != "\\")
                {
                    this.basePath += "\\";
                }
            }

            //Database
            this.dataBase.Import(this.basePath + "database");
            this.bModifyed = false;

            //History
            this.historyList.Clear();
            LoadSetHistory(this.basePath + "set_history.txt");
            this.bSetHistoryModifyed = false;

            //DNS Server
            string dns_server = (string)config.GetValue(Config.Name.server_dns_server, "8.8.8.8");
            if (IPAddress.TryParse (dns_server, out IPAddress ipaddress))
            {
                this.remoteEP = new IPEndPoint (ipaddress, 53);
            }
            else
            {
                this.remoteEP = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
            }

            config.Save(this.configPath, true);
        }

        public void Save()
        {
            lock(this.historyList)
            {
                if(this.bModifyed)
                {
                    DBG.MSG("DnsProxyServer.Save \n");
                    this.dataBase.Export(this.basePath + "database");

                    this.bModifyed = false;
                }

                if(this.bSetHistoryModifyed)
                {
                    DBG.MSG("DnsProxyServer.Save \n");
                    SaveSetHistory(this.basePath + "set_history.txt");
                    this.bSetHistoryModifyed = false;
                }
            }
        }

        void PipeConnect(object param, bool bConnect)
        {
            DBG.MSG("DnsProxyServer.PipeConnect \n");

            byte[] b;
            b = Command.Create(CMD.ENABLE, new byte[1] { (byte)(this.bProxyEnable ? 1 : 0) });
            this.namedPipe.WriteDataAsync(b, 0, b.Length);

            this._connectFunc(this._funcParam, bConnect);

        }


        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        static extern UInt32 DnsFlushResolverCache();


        public static void FlushCache()
        {
            DnsFlushResolverCache();
        }



        void PipeReceiveAsync(byte[] bytes, object param)
        {
            //DBG.MSG ("DnsProxyServer.PipeReceive, len={0} \n", bytes.Length);
            //DBG.DUMP(bytes, bytes.Length);

            Command cmd = new Command();

            if(cmd.Parse(bytes))
            {
                byte[] bytes_value = cmd.GetData();

                switch(cmd.GetCMD())
                {
                case CMD.NOP:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    }
                    break;

                case CMD.LOAD:
                    {
                        byte[] b = this.dataBase.Export();
                        b = Command.Create(CMD.LOAD, b);
                        this.namedPipe.WriteDataAsync(b, 0, b.Length);
                    }
                    break;

                case CMD.ADD:
                    {
                        Debug.Assert(false);

                        HistoryAdd((DataBase.FLAGS)(bytes_value[0] + (int)DataBase.FLAGS.SetNone), "", cmd.GetString(), "", "");
                    }
                    break;

                case CMD.SET:
                    {
                        if(bytes_value.Length != 1)
                        {
                            Debug.Assert(false);
                            break;
                        }

                        DBG.MSG("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                        bool bMod = false;
                        this.dataBase.SetFlags(cmd.GetString(), (DataBase.FLAGS)bytes_value[0], ref bMod);
                        if(bMod)
                        {
                            this.bModifyed = true;
                            DBG.MSG("DnsProxyServer.PipeReceive - {0}, {1}, {2} --> send\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
                        }

                        HistoryAdd((DataBase.FLAGS)(bytes_value[0] + (int)DataBase.FLAGS.SetNone), "", cmd.GetString(), "", "");
                    }
                    break;

                case CMD.DEL:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());

                        bool bMod = false;
                        dataBase.Del(cmd.GetString(), ref bMod);
                        if(bMod)
                        {
                            this.bModifyed = true;
                            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
                        }
                    }
                    break;

                case CMD.HISTORY:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());

                        HistoryData[] List;

                        lock(this.historyList)
                        {
                            using(MemoryStream ms = new MemoryStream())
                            {
                                byte[] b;

                                List = this.historyList.ToArray();
                                foreach(var v in List)
                                {
                                    b = Command.Create(CMD.HISTORY, new byte[1] { (byte)v.flags }, v.ToString());

                                    b = NamedPipe.CreateWriteData(b, 0, b.Length);
                                    ms.Write(b, 0, b.Length);
                                }

                                b = ms.ToArray();
                                this.namedPipe.WriteAsync(b, 0, b.Length);

                                ms.SetLength(0);

                                lock(this.SetHistoryList)
                                {
                                    List = this.SetHistoryList.ToArray();
                                    foreach(var v in List)
                                    {
                                        b = Command.Create(CMD.SET_HISTORY, new byte[1] { (byte)v.flags }, v.ToString());

                                        b = NamedPipe.CreateWriteData(b, 0, b.Length);
                                        ms.Write(b, 0, b.Length);
                                    }
                                }

                                b = ms.ToArray();
                                this.namedPipe.WriteAsync(b, 0, b.Length);
                            }
                        }

                        this.bHostoryEnable = true;
                    }
                    break;

                case CMD.ENABLE:
                    {
                        if(bytes_value.Length != 1)
                        {
                            Debug.Assert(false);
                            break;
                        }

                        DBG.MSG("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), bytes_value[0], cmd.GetString());

                        this.bProxyEnable = bytes_value[0] != 0;
                        this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
                    }
                    break;

                case CMD.DNS_CLEAR:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        FlushCache();
                    }
                    break;

                case CMD.COMMENT:
                    {
                        string comment = Encoding.Default.GetString(bytes_value, 0, bytes_value.Length);
                        bool bMod = false;
                        DataBase db = dataBase.Find(cmd.GetString(), true, ref bMod);

                        DBG.MSG("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), cmd.GetString(), comment);

                        if(db.SetComment(comment) || bMod)
                        {
                            this.bModifyed = true;
                            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
                        }
                    }
                    break;

                case CMD.DB_OPTIMIZATION:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        bool bMod = false;

                        this.dataBase.Optimization(ref bMod);
                        if(bMod)
                        {
                            this.bModifyed = true;

                            byte[] b = this.dataBase.Export();
                            b = Command.Create(CMD.LOAD, b);
                            this.namedPipe.WriteDataAsync(b, 0, b.Length);
                        }
                    }
                    break;

                case CMD.DEL_SET_HISTORY:
                    {
                        HistoryData data = new HistoryData();
                        data.FromString(cmd.GetString());

                        lock(this.SetHistoryList)
                        {
                            foreach(var v in this.SetHistoryList)
                            {
                                if(v.Equals(data))
                                {
                                    this.SetHistoryList.Remove(v);
                                    this.bSetHistoryModifyed = true;

                                    this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);

                                    break;
                                }
                            }
                        }
                    }
                    break;

                case CMD.DB_IMPORT:
                    {
                        this.dataBase.Import(cmd.GetString(), false);
                        this.bModifyed = true;

                        byte[] b = this.dataBase.Export();
                        b = Command.Create(CMD.LOAD, b);
                        this.namedPipe.WriteDataAsync(b, 0, b.Length);
                    }
                    break;

                default:
                    {
                        DBG.MSG("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        Debug.Assert(false);
                    }
                    break;
                }

                this._recvFunc(cmd, this._funcParam);

            }
        }

    }
}
