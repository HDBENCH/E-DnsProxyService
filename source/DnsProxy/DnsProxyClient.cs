﻿using DnsProxyLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using static DnsProxyLibrary.NamedPipe;
using System.IO;
using static DnsProxyLibrary.DataBase;

namespace DnsProxyLibrary
{
    public class DnsProxyClient
    {   
        public delegate void ConnectFunc (object param, bool bConnect);
        public delegate void ReceiveFunc (Command cmd, object param);

        private NamedPipe namedPipe = new NamedPipe();
        private readonly DataBase dataBase = new DataBase();
        private bool bModifyed = false;

        private ReceiveFunc _recvFunc;
        private ConnectFunc _connectFunc;
        private object _funcParam;





        public void Clear()
        {
            this.dataBase.Clear();
        }

        public NamedPipe GetNamedPipe()
        {
            return this.namedPipe;
        }
        public DataBase GetDataBase()
        {
            return this.dataBase;
        }

        public DataBase FindDataBase(string host)
        {
            return dataBase.Find(host, false, ref bModifyed);
        }

        public void DataBaseImport (string path)
        {
            byte[] bytes = Command.Create(CMD.DB_IMPORT, null, path);
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }

        public void DataBaseOptimization ()
        {
            byte[] bytes = Command.Create(CMD.DB_OPTIMIZATION, null, "");
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }
        public void DataBaseSet (DataBase.FLAGS flags, string host)
        {
            byte[] bytes = Command.Create(CMD.SET, new byte[1] { (byte)flags}, host);
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }
        public void DataBaseDel (string host)
        {
            byte[] bytes = Command.Create(CMD.DEL, null, host);
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }
        public void DataBaseDelSetlog (HistoryData data)
        {
            byte[] bytes = Command.Create(CMD.DEL_SET_HISTORY, null, data.ToString());
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }

        public void DataBaseSetComment (string host, string comment)
        {
            byte[] byte_comment = Encoding.Default.GetBytes (comment);
            byte[] bytes = Command.Create(CMD.COMMENT, byte_comment, host);
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }
        public void DnsCacheClear ()
        {
            byte[] bytes = Command.Create(CMD.DNS_CLEAR);
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }

        public void ProxyEnable (bool value)
        {
            byte[] bytes = Command.Create(CMD.ENABLE, new byte[1] { (byte)(value?1:0)});
            this.namedPipe.WriteDataAsync(bytes, 0, bytes.Length);
        }




        public void Start (ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            this._connectFunc = connectFunc;
            this._recvFunc = recvFunc;
            this._funcParam = funcParam;

            Clear();

            _ = Task.Run (() =>
            {
                this.bModifyed = false;
                this.namedPipe.StartClient (Common.pipeGuid, PipeConnectAsync, PipeReceive, this);
            });
        }


        public void Stop ()
        {
            //this.dataBase.Export (dataBasePath);
            this.namedPipe.Stop();
            Clear();
        }

        
        void PipeConnectAsync (object param, bool bConnect)
        {
            DBG.MSG ("DnsProxyClient.PipeConnect \n");

            if (bConnect)
            {
                byte[] bytes;

                bytes = Command.Create (CMD.LOAD);
                this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);

                bytes = Command.Create (CMD.HISTORY);
                this.namedPipe.WriteDataAsync (bytes, 0, bytes.Length);
            }

            this._connectFunc(this._funcParam, bConnect);
        }

        void PipeReceive (byte[] bytes, object param)
        {
            //DBG.MSG ("DnsProxyClient.PipeReceive, len={0} \n", bytes.Length);
            //DBG.DUMP(bytes, bytes.Length);

            Command cmd = new Command ();
            byte[] bytes_value;

            if (cmd.Parse (bytes))
            {
                bytes_value = cmd.GetData ();
                
                switch (cmd.GetCMD ())
                {
                case CMD.NOP:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    }
                    break;

                case CMD.LOAD:
                    {
                        using (MemoryStream ms = new MemoryStream (bytes_value))
                        {
                            this.dataBase.Import (ms);
                        }
                    }
                    break;

                case CMD.ADD:
                    {
                        if (bytes_value.Length != 1)
                        {
                            Debug.Assert(false);
                            break;
                        }

                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                        this.dataBase.SetFlags(cmd.GetString(), (DataBase.FLAGS)bytes_value[0], ref this.bModifyed);
                    }
                    break;

                case CMD.SET:
                    {
                        if (bytes_value.Length != 1)
                        {
                            Debug.Assert(false);
                            break;
                        }

                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                        this.dataBase.SetFlags(cmd.GetString(), (DataBase.FLAGS)bytes_value[0], ref this.bModifyed);
                    }
                    break;

                case CMD.DEL:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        this.dataBase.Del(cmd.GetString(), ref this.bModifyed);
                    }
                    break;

                case CMD.HISTORY:
                    {
                        if (bytes_value.Length != 1)
                        {
                            Debug.Assert (false);
                            break;
                        }

                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                    }
                    break;

                case CMD.SET_HISTORY:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    }
                    break;

                case CMD.ENABLE:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    }
                    break;

                case CMD.DNS_CLEAR:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
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
                        }
                    }
                    break;

                case CMD.DB_OPTIMIZATION:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    }
                    break;

                case CMD.DATETIME:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        bool bMod = false;
                        DataBase db = dataBase.Find (cmd.GetString (), false, ref bMod);
                        if (db != null)
                        {
                            db.UpdateDatetime();
                        }
                    }
                    break;

                case CMD.DEL_SET_HISTORY:
                    {
                    }
                    break;

                default:
                    {
                        DBG.MSG("DnsProxyClient.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                        Debug.Assert (false);
                    }
                    break;
                }
                
                this._recvFunc(cmd, this._funcParam);

            }
        }

    }
}
