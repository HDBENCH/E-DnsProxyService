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
        Thread parseThread;
        Thread oneSecThread;

        IPEndPoint serverEP;
        IPEndPoint clientEP;
        IPEndPoint remoteEP;

        ConcurrentDictionary<ushort, PacketData> reqDic = new ConcurrentDictionary<ushort, PacketData>();
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
                    this.namedPipe.StartServer(Common.pipeGuid, PipeConnect, PipeReceiveAsync, this);

                    this.serverEP = new IPEndPoint(IPAddress.Any, 53);
                    this.clientEP = new IPEndPoint(IPAddress.Any, 0);

                    this.serverRecvThread = new Thread(new ParameterizedThreadStart(ServerRecvThread));
                    this.serverRecvThread.Name = "ServerRecvThread";
                    this.serverRecvThread.Start();

                    this.clientRecvThread = new Thread(new ParameterizedThreadStart(ClientRecvThread));
                    this.clientRecvThread.Name = "clientRecvThread";
                    this.clientRecvThread.Start();

                    this.parseThread = new Thread(new ParameterizedThreadStart(ParseThread));
                    this.parseThread.Name = "ParseThread";
                    this.parseThread.Start();

                    this.oneSecThread = new Thread(new ParameterizedThreadStart(OneSecThread));
                    this.oneSecThread.Name = "OneSecThread";
                    this.oneSecThread.Start();
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.Start - Exception({0})\n", e.Message);
                    //Debug.Assert(false);
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
            this.parseThread?.Join();
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


        private async void ServerRecvThread(object obj)
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(0, 0);
            byte[] bytes;

            DBG.MSG("DnsProxyServer.ServerRecvThread - START, 0x{0:X}, {1}\n", this.serverRecvThread.ManagedThreadId, this.serverEP.ToString());


            for(; !this.cts.IsCancellationRequested;)
            {
                try
                {
                    if(this.udpServer == null)
                    {
                        this.udpServer = new UdpClient(this.serverEP);
                    }
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.ServerRecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);

                    if((uint)e.HResult == 0x80004005)
                    {
                        break;
                    }
                }

                if(this.udpServer == null)
                {
                    Thread.Sleep(1000);
                    continue;
                }


                try
                {
                    //データを受信する
                    UdpReceiveResult receivedResults = await this.udpServer.ReceiveAsync();
                    remoteEndPoint = receivedResults.RemoteEndPoint;
                    bytes = receivedResults.Buffer;

                    PacketData data = new PacketData();

                    data.Time = DateTime.Now;
                    data.senderEndPoint = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
                    data.bytes = new byte[bytes.Length];
                    bytes.CopyTo(data.bytes, 0);

                    lock(this.recvPacketList)
                    {
                        this.recvPacketList.Add(data);
                        this.eventRecv.Set();
                    }
                }
                catch(System.Net.Sockets.SocketException e)
                {
                    DBG.MSG("DnsProxyServer.ServerRecvThread - SocketException, HResult={0:X8}, {1}\n", e.HResult, e.ToString());
                }
                catch(ObjectDisposedException e)
                {
                    //すでに閉じている時は終了
                    DBG.MSG("DnsProxyServer.ServerRecvThread - ObjectDisposedException, {0}\n", e.Message);
                    break;
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.ServerRecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG("DnsProxyServer.ServerRecvThread - END, 0x{0:X}, {1}\n", this.serverRecvThread.ManagedThreadId, this.serverEP.ToString());
        }
        private async void ClientRecvThread(object obj)
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(0, 0);
            byte[] bytes;

            DBG.MSG("DnsProxyServer.ClientRecvThread - START, 0x{0:X}, {1}\n", this.clientRecvThread.ManagedThreadId, this.clientEP.ToString());


            for(; !this.cts.IsCancellationRequested;)
            {
                try
                {
                    this.udpClient = new UdpClient(this.clientEP);
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.ClientRecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                }

                if(this.udpClient == null)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                try
                {
                    //データを受信する
                    UdpReceiveResult receivedResults = await this.udpClient.ReceiveAsync();
                    remoteEndPoint = receivedResults.RemoteEndPoint;
                    bytes = receivedResults.Buffer;

                    //
                    PacketData data = new PacketData();

                    data.Time = DateTime.Now;
                    data.senderEndPoint = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
                    data.bytes = new byte[bytes.Length];
                    bytes.CopyTo(data.bytes, 0);

                    lock(this.recvPacketList)
                    {
                        this.recvPacketList.Add(data);
                        this.eventRecv.Set();
                    }
                }
                catch(System.Net.Sockets.SocketException e)
                {
                    DBG.MSG("DnsProxyServer.ClientRecvThread - SocketException, HResult={0:X8}, {1}\n", e.HResult, e.ToString());
                }
                catch(ObjectDisposedException e)
                {
                    //すでに閉じている時は終了
                    DBG.MSG("DnsProxyServer.ClientRecvThread - ObjectDisposedException, {0}\n", e.Message);
                    break;
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.ClientRecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG("DnsProxyServer.ClientRecvThread - END, 0x{0:X}\n", this.clientRecvThread.ManagedThreadId);
        }


        private void OneSecThread(object obj)
        {
            List<PacketData> recvPacketList = new List<PacketData>();

            DBG.MSG("DnsProxyServer.OneSecThread - START, 0x{0:X}, {1}\n", this.oneSecThread.ManagedThreadId, this.serverEP.ToString());
            for(; !this.cts.IsCancellationRequested;)
            {
                for(int i = 0; i < 10; i++)
                {
                    if(this.cts.IsCancellationRequested)
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }

                Save();

                //timeout
                lock(this.reqDic)
                {
                    DateTime timeNow = DateTime.Now;
                    TimeSpan timeSpan = new TimeSpan(0, 3, 0);
                    List<ushort> list = new List<ushort>();

                    foreach(var v in this.reqDic)
                    {
                        if((timeNow - v.Value.Time) > timeSpan)
                        {
                            list.Add(v.Key);
                        }
                    }

                    foreach(var v in list)
                    {
                        if(this.reqDic.TryRemove(v, out PacketData value))
                        {
                            DBG.MSG("DnsProxyServer.OneSecThread - Timeout, TransactionID=0x{0:X2}, TimeNow={1}, Time={2}\n", value.TransactionID, timeNow, value.Time);
                        }
                    }
                }
            }

            DBG.MSG("DnsProxyServer.OneSecThread - END, 0x{0:X}, {1}\n", this.oneSecThread.ManagedThreadId, this.serverEP.ToString());

        }


        private async void ParseThread(object obj)
        {
            List<PacketData> recvPacketList = new List<PacketData>();

            DBG.MSG("DnsProxyServer.ParseThread - START, 0x{0:X}, {1}\n", this.parseThread.ManagedThreadId, this.serverEP.ToString());

            for(; !this.cts.IsCancellationRequested;)
            {
                try
                {
                    if(!this.eventRecv.WaitOne(100))
                    {
                        continue;
                    }

                    lock(this.recvPacketList)
                    {
                        foreach(var v in this.recvPacketList)
                        {
                            recvPacketList.Add(v);
                        }
                        this.recvPacketList.Clear();
                    }

                    foreach(var data in recvPacketList)
                    {
                        if(this.cts.IsCancellationRequested)
                        {
                            break;
                        }

                        DBG.MSG("DnsProxyServer.ParseThread - RECV, {0}\n", data.senderEndPoint.ToString());
                        //DBG.DUMP(data.bytes, data.bytes.Length);


                        DnsProtocol dns = new DnsProtocol();
                        dns.Parse(data.bytes);

                        if(dns.header.IsQuery())
                        {
                            DBG.MSG("DnsProxyServer.ParseThread - Query {0}\n", data.senderEndPoint);
                            if(!IsAcceptAsync(dns, data.senderEndPoint.ToString()))
                            {
                                data.bytes[0x02] |= 0x80;
                                data.bytes[0x03] |= 0x03;
                                await this.udpServer.SendAsync(data.bytes, data.bytes.Length, data.senderEndPoint);

                                break;
                            }

                            lock(this.reqDic)
                            {
                                if(this.reqDic.ContainsKey(this.TransactionID))
                                {
                                    Debug.Assert(false);
                                }

                                data.Time = DateTime.Now;
                                data.TransactionID = this.TransactionID;
                                this.reqDic.TryAdd(this.TransactionID, data);
                            }

                            Buffer.BlockCopy(data.bytes, 0, data.originalTransactionID, 0, 2);

                            data.bytes[0] = (byte)((this.TransactionID & 0xFF00) >> 8);//TransactionID
                            data.bytes[1] = (byte)((this.TransactionID & 0x00FF) >> 0);//TransactionID

                            DBG.MSG("DnsProxyServer.ParseThread - Query TransactionID=0x{0:X2}\n", this.TransactionID);
                            this.TransactionID++;

                            if(this.udpClient != null)
                            {
                                await this.udpClient.SendAsync(data.bytes, data.bytes.Length, this.remoteEP);
                            }
                            else
                            {
                                await this.udpServer.SendAsync(data.bytes, data.bytes.Length, this.remoteEP);
                            }
                        }
                        else
                        {
                            DBG.MSG("DnsProxyServer.ParseThread - Response\n");

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

                            lock(this.reqDic)
                            {
                                if(this.reqDic.TryRemove(dns.header.TransactionID, out PacketData value))
                                {
                                    Buffer.BlockCopy(value.originalTransactionID, 0, data.bytes, 0, 2);
                                    this.udpServer.SendAsync(data.bytes, data.bytes.Length, value.senderEndPoint);
                                    DBG.MSG("DnsProxyServer.ParseThread - TryRemove, TransactionID=0x{0:X2}\n", dns.header.TransactionID);
                                }
                                else
                                {
                                    DBG.MSG("DnsProxyServer.ParseThread - TryRemove, TransactionID=0x{0:X2} not found\n", dns.header.TransactionID);
                                    //Debug.Assert (false);
                                }
                            }
                        }
                    }
                    recvPacketList.Clear();
                }
                catch(Exception e)
                {
                    DBG.MSG("DnsProxyServer.ParseThread - Exception, {0}\n", e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG("DnsProxyServer.ParseThread - END, 0x{0:X}, {1}\n", this.parseThread.ManagedThreadId, this.serverEP.ToString());
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
