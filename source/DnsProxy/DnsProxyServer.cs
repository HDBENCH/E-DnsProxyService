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


        class ThreadData
        {
            public CancellationTokenSource cts = new CancellationTokenSource();
            public UdpClient udpClient;
            public Thread recvThread;
            public Thread parseThread;
            public Thread oneSecThread;
            public ManualResetEvent recvThreadComplete = new ManualResetEvent(false);
            public ManualResetEvent parseThreadComplete = new ManualResetEvent(false);

            public IPEndPoint localEP;
            public IPEndPoint remoteEP;
            public ConcurrentDictionary<ushort, PacketData> reqDic = new ConcurrentDictionary<ushort, PacketData>();
            public AutoResetEvent eventRecv = new AutoResetEvent(false);
            public List<PacketData> recvPacketList = new List<PacketData>();
            public ushort TransactionID = 1;

            public void Stop ()
            {
                cts.Cancel ();
                udpClient.Close ();
                eventRecv.Set ();
            }

            public void WaitAll ()
            {
                recvThreadComplete.WaitOne ();
                parseThreadComplete.WaitOne ();
            }

            public void Dispose ()
            {
                udpClient.Dispose ();
                cts.Dispose ();
            }

        }

        private readonly List<HistoryData> SetHistoryList = new List<HistoryData>();
        private readonly List<HistoryData> historyList = new List<HistoryData>();
        private readonly List<ThreadData> threadList = new List<ThreadData>();
        private readonly DataBase dataBase = new DataBase();
        private string configPath;
        private string basePath;
        private IPAddress dnsAddr;
        private int dnsPort = 53;
        private bool bModifyed = false;
        private bool bSetHistoryModifyed = false;
        private NamedPipe namedPipe = new NamedPipe();

        public delegate void ConnectFunc (object param, bool bConnect);
        public delegate void ReceiveFunc (Command cmd, object param);
        private ReceiveFunc _recvFunc;
        private ConnectFunc _connectFunc;
        private object _funcParam;
        private bool bProxyEnable = true;
        private bool bHostoryEnable = false;
        private Config config = new Config ();


        public void Start (string base_path, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            this.basePath = base_path;
            this._connectFunc = connectFunc;
            this._recvFunc = recvFunc;
            this._funcParam = funcParam;

            if (this.basePath.Substring (this.basePath.Length - 1, 1) != "\\")
            {
                this.basePath += "\\";
            }
            this.configPath = this.basePath + "config.ini";

            _ = Task.Run (() =>
            {
                Load();

                this.namedPipe.StartServer (Common.pipeGuid, PipeConnect, PipeReceiveAsync, this);
#if false
                IPHostEntry hosts = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in hosts.AddressList)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    try
                    {
                        ThreadData td = new ThreadData ();

                        td.remoteEP = new IPEndPoint (this.dnsAddr, this.dnsPort);
                        td.localEP = new IPEndPoint (ip, this.dnsPort);
                        //td.udpClient = new UdpClient (td.localEP);

                        td.recvThread = new Thread (new ParameterizedThreadStart (RecvThread));
                        td.recvThread.Name = "RecvThread";
                        td.recvThread.Start (td);

                        td.parseThread = new Thread (new ParameterizedThreadStart (ParseThread));
                        td.parseThread.Name = "ParseThread";
                        td.parseThread.Start (td);

                        td.oneSecThread = new Thread (new ParameterizedThreadStart (OneSecThread));
                        td.oneSecThread.Name = "OneSecThread";
                        td.oneSecThread.Start (td);

                        this.threadList.Add(td);
                    }
                    catch (Exception)
                    {
                        int c=0;
                    }
                }
#else

                try
                {
                    ThreadData td = new ThreadData ();

                    td.remoteEP = new IPEndPoint (this.dnsAddr, this.dnsPort);
                    td.localEP = new IPEndPoint (IPAddress.Any, this.dnsPort);
                    //td.udpClient = new UdpClient (td.localEP);

                    td.recvThread = new Thread (new ParameterizedThreadStart (RecvThread));
                    td.recvThread.Name = "RecvThread";
                    td.recvThread.Start (td);

                    td.parseThread = new Thread (new ParameterizedThreadStart (ParseThread));
                    td.parseThread.Name = "ParseThread";
                    td.parseThread.Start (td);

                    td.oneSecThread = new Thread (new ParameterizedThreadStart (OneSecThread));
                    td.oneSecThread.Name = "OneSecThread";
                    td.oneSecThread.Start (td);

                    this.threadList.Add (td);
                }
                catch (Exception e)
                {
                    DBG.MSG ("DnsProxyServer.Start - Exception({0})\n", e.Message);
                    Debug.Assert(false);
                }
#endif
            });
        }

        public void Stop ()
        {
            foreach (var td in this.threadList)
            {
                td.Stop();
            }

            foreach (var td in this.threadList)
            {
                td.WaitAll();
                td.Dispose ();
            }

            Save();

            this.namedPipe.Stop ();
        }

        void LoadSetHistory (string path)
        {
            lock(this.SetHistoryList)
            {
                this.SetHistoryList.Clear ();
                
                do
                {
                    if (!File.Exists (path))
                    {
                        break;
                    }

                    using (FileStream fs = new FileStream (path, FileMode.Open, FileAccess.Read))
                    {
                        using (StreamReader reader = new StreamReader (fs))
                        {
                            while (true)
                            {
                                string s =reader.ReadLine();
                                if (s == null)
                                {
                                    break;
                                }

                                HistoryData data = new HistoryData();
                                if (data.FromString (s))
                                {
                                    this.SetHistoryList.Add (data);
                                }
                            }
                        }

                    }
                }
                while (false);
            }
        }

        void SaveSetHistory (string path)
        {
            lock (this.SetHistoryList)
            {
                using (FileStream fs = new FileStream (path, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    using (StreamWriter writer = new StreamWriter (fs))
                    {

                        foreach (var v in this.SetHistoryList.ToArray ())
                        {
                            writer.WriteLine (v.ToString());
                        }
                    }
                }
            }
        }


        private async void RecvThread (object obj)
        {
            ThreadData td = (ThreadData)obj;
            IPEndPoint remoteEndPoint = new IPEndPoint(0, 0);
            byte[] bytes;

            DBG.MSG ("DnsProxyServer.RecvThread - START, 0x{0:X}, {1}\n", td.recvThread.ManagedThreadId, td.localEP.ToString ());


            for (; !td.cts.IsCancellationRequested;)
            {
                try
                {
                    if (td.udpClient == null)
                    {
                        td.udpClient = new UdpClient (td.localEP);
                    }
                }
                catch (Exception e)
                {
                    DBG.MSG ("DnsProxyServer.RecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                    uint c = (uint)e.HResult;
                    if((uint)e.HResult == 0x80004005)
                    {
                        break;
                    }
                }

                if (td.udpClient == null)
                {
                    Thread.Sleep (1000);
                    continue;
                }


                try
                {
                    //データを受信する
#if true
                    UdpReceiveResult receivedResults = await td.udpClient.ReceiveAsync ();
                    remoteEndPoint = receivedResults.RemoteEndPoint;
                    bytes = receivedResults.Buffer;
#else
                    bytes = td.udpClient.Receive (ref remoteEndPoint);
#endif

                    //
                    PacketData data = new PacketData ();

                    data.Time = DateTime.Now;
                    data.senderEndPoint = new IPEndPoint (remoteEndPoint.Address, remoteEndPoint.Port);
                    data.bytes = new byte[bytes.Length];
                    bytes.CopyTo (data.bytes, 0);

                    lock (td.recvPacketList)
                    {
                        td.recvPacketList.Add (data);
                        td.eventRecv.Set ();
                    }
                }
                catch (System.Net.Sockets.SocketException e)
                {
                    DBG.MSG ("DnsProxyServer.RecvThread - SocketException, HResult={0:X8}, {1}\n", e.HResult, e.ToString ());
                }
                catch (ObjectDisposedException e)
                {
                    //すでに閉じている時は終了
                    DBG.MSG ("DnsProxyServer.RecvThread - ObjectDisposedException, {0}\n", e.Message);
                    break;
                }
                catch (Exception e)
                {
                    DBG.MSG ("DnsProxyServer.RecvThread - Exception, {0}, {1}\n", e.HResult, e.Message);
                    Debug.Assert(false);
                }


            }

            DBG.MSG ("DnsProxyServer.RecvThread - END, 0x{0:X}, {1}\n", td.recvThread.ManagedThreadId, td.localEP.ToString ());
            td.recvThreadComplete.Set();
        }


        private void OneSecThread (object obj)
        {
            ThreadData td = (ThreadData)obj;
            List<PacketData> recvPacketList = new List<PacketData>();

            DBG.MSG ("DnsProxyServer.OneSecThread - START, 0x{0:X}, {1}\n", td.oneSecThread.ManagedThreadId, td.localEP.ToString ());
            for (; !td.cts.IsCancellationRequested;)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (td.cts.IsCancellationRequested)
                    {
                        break;
                    }

                    Thread.Sleep (100);
                }

                Save ();

                //timeout
                lock (td.reqDic)
                {
                    DateTime timeNow = DateTime.Now;
                    TimeSpan timeSpan = new TimeSpan(0, 3, 0);
                    List<ushort> list = new List<ushort>();

                    foreach (var v in td.reqDic)
                    {
                        if ((timeNow - v.Value.Time) > timeSpan)
                        {
                            list.Add(v.Key);
                        }
                    }

                    foreach (var v in list)
                    {
                        if (td.reqDic.TryRemove (v, out PacketData value))
                        {
                            DBG.MSG ("DnsProxyServer.OneSecThread - Timeout, TransactionID=0x{0:X2}, TimeNow={1}, Time={2}\n", value.TransactionID, timeNow, value.Time);
                        }
                    }
                }
            }

            DBG.MSG ("DnsProxyServer.OneSecThread - END, 0x{0:X}, {1}\n", td.oneSecThread.ManagedThreadId, td.localEP.ToString ());

        }


        private async void ParseThread (object obj)
        {
            ThreadData td = (ThreadData)obj;
            List<PacketData> recvPacketList = new List<PacketData>();

            DBG.MSG ("DnsProxyServer.ParseThread - START, 0x{0:X}, {1}\n", td.parseThread.ManagedThreadId, td.localEP.ToString ());

            for (; !td.cts.IsCancellationRequested;)
            {
                try
                {
                    if (!td.eventRecv.WaitOne (100))
                    {
                        continue;
                    }

                    lock (td.recvPacketList)
                    {
                        foreach (var v in td.recvPacketList)
                        {
                            recvPacketList.Add (v);
                        }
                        td.recvPacketList.Clear ();
                    }

                    foreach (var data in recvPacketList)
                    {
                        if (td.cts.IsCancellationRequested)
                        {
                            break;
                        }

                        DBG.MSG ("DnsProxyServer.ParseThread - RECV, {0}\n", data.senderEndPoint.ToString ());
                        //DBG.DUMP(data.bytes, data.bytes.Length);


                        DnsProtocol dns = new DnsProtocol();
                        dns.Parse (data.bytes);

                        if (dns.header.IsQuery ())
                        {
                            DBG.MSG ("DnsProxyServer.ParseThread - Query {0}\n", data.senderEndPoint);
                            if (!IsAcceptAsync (dns, data.senderEndPoint.ToString ()))
                            {
                                data.bytes[0x02] |= 0x80;
                                data.bytes[0x03] |= 0x03;
                                await td.udpClient.SendAsync (data.bytes, data.bytes.Length, data.senderEndPoint);

                                break;
                            }

                            lock (td.reqDic)
                            {
                                if (td.reqDic.ContainsKey (td.TransactionID))
                                {
                                    Debug.Assert (false);
                                }

                                data.Time = DateTime.Now;
                                data.TransactionID = td.TransactionID;
                                td.reqDic.TryAdd (td.TransactionID, data);
                            }

                            Buffer.BlockCopy (data.bytes, 0, data.originalTransactionID, 0, 2);

                            data.bytes[0] = (byte)((td.TransactionID & 0xFF00) >> 8);//TransactionID
                            data.bytes[1] = (byte)((td.TransactionID & 0x00FF) >> 0);//TransactionID

                            DBG.MSG ("DnsProxyServer.ParseThread - Query TransactionID=0x{0:X2}\n", td.TransactionID);
                            td.TransactionID++;
                            await td.udpClient.SendAsync (data.bytes, data.bytes.Length, td.remoteEP);

                        }
                        else
                        {
                            DBG.MSG ("DnsProxyServer.ParseThread - Response\n");

                            for (int i=0;i<dns.Answers.Count;i++)
                            {
                                var v = dns.Answers[i];

                                bool bMod = false;
                                string comment = "";
                                DataBase db = this.dataBase.Find(v.Name, false, ref bMod);
                                while (db != null)
                                {
                                    comment = db.GetComment();
                                    break;
                                    //if (!string.IsNullOrEmpty (comment))
                                    //{
                                    //    break;
                                    //}

                                    //db = db.GetParent();
                                }

                                HistoryAdd (FLAGS.Answer, data.senderEndPoint.Address.ToString (), v.Name, string.Format ("{0}: {1}, {2}", i + 1, v.Type.ToString (), v.typeData.ToDetail ()), comment);
                            }

                            lock (td.reqDic)
                            {
                                if (td.reqDic.TryRemove (dns.header.TransactionID, out PacketData value))
                                {
                                    Buffer.BlockCopy (value.originalTransactionID, 0, data.bytes, 0, 2);
                                    td.udpClient.SendAsync (data.bytes, data.bytes.Length, value.senderEndPoint);
                                    DBG.MSG ("DnsProxyServer.ParseThread - TryRemove, TransactionID=0x{0:X2}\n", dns.header.TransactionID);
                                }
                                else
                                {
                                    DBG.MSG ("DnsProxyServer.ParseThread - TryRemove, TransactionID=0x{0:X2} not found\n", dns.header.TransactionID);
                                    //Debug.Assert (false);
                                }
                            }
                        }
                    }
                    recvPacketList.Clear ();
                }
                catch (Exception e)
                {
                    DBG.MSG ("DnsProxyServer.ParseThread - Exception, {0}\n", e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG ("DnsProxyServer.ParseThread - END, 0x{0:X}, {1}\n", td.parseThread.ManagedThreadId, td.localEP.ToString ());
            td.parseThreadComplete.Set();
        }

        private bool IsAcceptAsync (DnsProtocol dns, string ip)
        {
            bool result = false;

            for (int i=0;i<dns.questions.Count;i++)
            {
                var v = dns.questions[i];

                DataBase.FLAGS flags = DataBase.FLAGS.None;
                bool bMod= false;
                DataBase db = this.dataBase.Find(v.Name, true, ref bMod);
                string comment = db.GetComment();

                if (bMod)
                {
                    this.bModifyed = true;
                    byte[] b = Command.Create(CMD.ADD, new byte[] { (byte)db.GetFlags() }, db.GetFullName());
                    this.namedPipe.WriteDataAsync (b, 0, b.Length);
                }


                while (db != null)
                {
                    flags = db.GetFlags ();

                    if (flags != DataBase.FLAGS.None)
                    {
                        break;
                    }

                    db = db.GetParent ();
                }

                if (!this.bProxyEnable)
                {
                    DBG.MSG ("DnsProxyServer.IsAcceptAsync - host={0}, {1} --> Accept(Proxy Disable)\n", v.Name, flags);
                    HistoryAdd (FLAGS.Disable, ip, v.Name, string.Format ("{0}: {1}", i + 1, v.Type.ToString ()), comment);

                    result = true;
                }
                else
                {
                    DBG.MSG ("DnsProxyServer.IsAcceptAsync - host={0}, {1}\n", v.Name, flags);
                    HistoryAdd (flags, ip, v.Name, string.Format ("{0}: {1}", i + 1, v.Type.ToString ()), comment);

                    if (flags == DataBase.FLAGS.Accept)
                    {
                        result = true;
                    }
                    else if ((flags == DataBase.FLAGS.Reject) || (flags == DataBase.FLAGS.Ignore))
                    {
                        //1つでもREJECT/IGNOREがあれば許可しない
                        result = false;
                        break;
                    }
                }
            }

            return result;
        }

        void HistoryAdd (DataBase.FLAGS flags, string ip, string host, string info, string comment)
        {
            HistoryData data = new HistoryData ();
            data.time = DateTime.Now;
            data.flags = flags;
            data.ip = ip;
            data.host = host;
            data.info = info;
            data.comment = comment;

            switch (flags)
            {
            case DataBase.FLAGS.SetNone:
            case DataBase.FLAGS.SetAccept:
            case DataBase.FLAGS.SetReject:
            case DataBase.FLAGS.SetIgnore:
                {
                    lock (this.SetHistoryList)
                    {

                        this.SetHistoryList.Add (data);
                        if (this.SetHistoryList.Count > 3000)
                        {
                            this.SetHistoryList.RemoveAt (0); 
                        }
                        this.bSetHistoryModifyed = true;
                    }
                }
                break;
            }


            lock (this.historyList)
            {
                this.historyList.Add (data);
                if (this.historyList.Count > 1000)
                {
                    this.historyList.RemoveRange (0, this.historyList.Count - 1000);
                }
            }

            if (this.bHostoryEnable)
            {
                HistorySend (data);
            }
        }

        void HistorySend(HistoryData data)
        {
            byte[] b = Command.Create(CMD.HISTORY, new byte[] { (byte)data.flags }, data.ToString());
            this.namedPipe.WriteDataAsync (b, 0, b.Length);
        }

        void Load ()
        {   
            this.config.Load(this.configPath);

            if (this.config.Get (Config.Name.base_path, out string base_path))
            {
                if (Directory.Exists (base_path))
                {
                    this.basePath = base_path;
                    if (this.basePath.Substring (this.basePath.Length - 1, 1) != "\\")
                    {
                        this.basePath += "\\";
                        this.config.Set (Config.Name.base_path, this.basePath);
                    }
                }
                else
                {
                    this.config.Set (Config.Name.base_path, this.basePath);
                }
            }

            //Database
            this.dataBase.Import (this.basePath + "database");
            this.bModifyed = false;

            //History
            this.historyList.Clear ();
            LoadSetHistory(this.basePath + "set_history.txt");
            this.bSetHistoryModifyed = false;

            //DNS Server
            this.dnsAddr = IPAddress.Parse("8.8.8.8");
            this.dnsPort = 53;

            if (this.config.Get (Config.Name.dns_server, out string dns_server))
            {
                if (IPAddress.TryParse (dns_server, out IPAddress ipaddress))
                {
                    this.dnsAddr = ipaddress;
                }
            }


        }

        public void Save ()
        {
            this.config.Save(this.configPath);

            lock (this.historyList)
            {
                if (this.bModifyed)
                {
                    DBG.MSG ("DnsProxyServer.Save \n");
                    this.dataBase.Export (this.basePath + "database");

                    this.bModifyed = false;
                }

                if (this.bSetHistoryModifyed)
                {
                    DBG.MSG ("DnsProxyServer.Save \n");
                    SaveSetHistory (this.basePath + "set_history.txt");
                    this.bSetHistoryModifyed = false;
                }
            }
        }

        void PipeConnect (object param, bool bConnect)
        {
            DBG.MSG ("DnsProxyServer.PipeConnect \n");

            byte[] b;
            b = Command.Create (CMD.ENABLE, new byte[1] { (byte)(this.bProxyEnable ? 1:0) });
            this.namedPipe.WriteDataAsync (b, 0, b.Length);

            this._connectFunc (this._funcParam, bConnect);

        }


        [DllImport("dnsapi.dll", EntryPoint="DnsFlushResolverCache")]
        static extern UInt32 DnsFlushResolverCache();


        public static void FlushCache()
        {
            DnsFlushResolverCache();
        }



        void PipeReceiveAsync (byte[] bytes, object param)
        {
            //DBG.MSG ("DnsProxyServer.PipeReceive, len={0} \n", bytes.Length);
            //DBG.DUMP(bytes, bytes.Length);

            Command cmd = new Command ();

            if (cmd.Parse (bytes))
            {
                byte[] bytes_value = cmd.GetData ();

                switch (cmd.GetCMD ())
                {
                case CMD.NOP:
                    {
                        DBG.MSG ("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD (), cmd.GetString ());
                    }
                    break;

                case CMD.LOAD:
                    {
                        byte[] b = this.dataBase.Export();
                        b = Command.Create (CMD.LOAD, b);
                        this.namedPipe.WriteDataAsync (b, 0, b.Length);

                    }
                    break;

                case CMD.ADD:
                    {
                        Debug.Assert (false);

                        HistoryAdd((DataBase.FLAGS)(bytes_value[0] + (int)DataBase.FLAGS.SetNone), "", cmd.GetString (), "", "");
                    }
                    break;

                case CMD.SET:
                    {
                        if (bytes_value.Length != 1)
                        {
                            Debug.Assert (false);
                            break;
                        }

                        DBG.MSG ("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD (), (DataBase.FLAGS)bytes_value[0], cmd.GetString ());
                        bool bMod = false;
                        this.dataBase.SetFlags (cmd.GetString (), (DataBase.FLAGS)bytes_value[0], ref bMod);
                        if (bMod)
                        {
                            this.bModifyed = true;
                            DBG.MSG ("DnsProxyServer.PipeReceive - {0}, {1}, {2} --> send\n", cmd.GetCMD (), (DataBase.FLAGS)bytes_value[0], cmd.GetString ());
                            this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);
                        }

                        HistoryAdd((DataBase.FLAGS)(bytes_value[0] + (int)DataBase.FLAGS.SetNone), "", cmd.GetString (), "", "");
                    }
                    break;

                case CMD.DEL:
                    {
                        DBG.MSG ("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD (), cmd.GetString ());

                        bool bMod = false;
                        dataBase.Del (cmd.GetString (), ref bMod);
                        if (bMod)
                        {
                            this.bModifyed = true;
                            this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);
                        }
                    }
                    break;

                case CMD.HISTORY:
                    {
                        DBG.MSG ("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD (), cmd.GetString ());

                        HistoryData[] List;

                        lock (this.historyList)
                        {
                            using (MemoryStream ms = new MemoryStream ())
                            {
                                byte[] b;

                                List = this.historyList.ToArray ();
                                foreach (var v in List)
                                {
                                    b = Command.Create (CMD.HISTORY, new byte[1] { (byte)v.flags }, v.ToString());

                                    b = NamedPipe.CreateWriteData (b, 0, b.Length);
                                    ms.Write (b, 0, b.Length);
                                }

                                b = ms.ToArray ();
                                this.namedPipe.WriteAsync (b, 0, b.Length);

                                ms.SetLength (0);

                                lock (this.SetHistoryList)
                                {
                                    List = this.SetHistoryList.ToArray ();
                                    foreach (var v in List)
                                    {
                                        b = Command.Create (CMD.SET_HISTORY, new byte[1] { (byte)v.flags }, v.ToString ());

                                        b = NamedPipe.CreateWriteData (b, 0, b.Length);
                                        ms.Write (b, 0, b.Length);
                                    }
                                }

                                b = ms.ToArray ();
                                this.namedPipe.WriteAsync (b, 0, b.Length);
                            }
                        }

                        this.bHostoryEnable = true;
                    }
                    break;

                case CMD.ENABLE:
                    {
                        if (bytes_value.Length != 1)
                        {
                            Debug.Assert (false);
                            break;
                        }

                        DBG.MSG ("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD (), bytes_value[0], cmd.GetString ());

                        this.bProxyEnable = bytes_value[0] != 0;
                        this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);
                    }
                    break;

                case CMD.DNS_CLEAR:
                    {
                        DBG.MSG ("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD (), cmd.GetString ());
                        FlushCache();
                    }
                    break;

                case CMD.COMMENT:
                    {
                        string comment = Encoding.Default.GetString (bytes_value, 0, bytes_value.Length);
                        bool bMod = false;
                        DataBase db = dataBase.Find (cmd.GetString (), true, ref bMod);

                        DBG.MSG ("DnsProxyServer.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD (), cmd.GetString (), comment);

                        if (db.SetComment (comment) || bMod)
                        {
                            this.bModifyed = true;
                            this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);
                        }
                    }
                    break;

                default:
                    {
                        DBG.MSG ("DnsProxyServer.PipeReceive, {0}, {1}\n", cmd.GetCMD (), cmd.GetString ());
                        Debug.Assert (false);
                    }
                    break;
                }

                this._recvFunc (cmd, this._funcParam);

            }
        }

    }
}
