using System;
using DnsProxyLibrary;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Policy;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Xml.Linq;
using System.Collections;

namespace DnsProxyLibrary
{
    public class NamedPipe
    {
        private string pipe_name;
        private string type_name;
        private PipeStream pipeStream;

        private Thread recvThread;
        private Thread parseThread;
        private Thread sendThread;
        private CancellationTokenSource cancellationTokenSource;

        public delegate void ConnectFunc (object param, bool bConnect);
        public delegate void ReceiveFunc (byte[] bytes, object param);

        private ReceiveFunc _recvFunc;
        private ConnectFunc _connectFunc;
        private object _funcParam;

        private AutoResetEvent recvEvent;
        private AutoResetEvent writeEvent;
        private ManualResetEvent stopEvent;
        private ManualResetEvent recvThreadCompleteEvent;
        private MemoryStream writeStream;
        private MemoryStream recvStream;


        public bool IsConnected ()
        {
            bool result = false;

            try
            {
                if (this.pipeStream != null)
                {
                    result = this.pipeStream.IsConnected;
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("NamedPipe.IsConnected - [{0}]: Exception, {0}\n", this.type_name, e.Message);
                Debug.Assert(false);
            }

            return result;
        }


        public void StartServer (string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            DBG.MSG ("NamedPipe.StartServer - \"{0}\" START\n", name);
            this.type_name = "ServerStream";
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule("Everyone", PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));

            Start (new NamedPipeServerStream (name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, ps), name, connectFunc, recvFunc, funcParam);
            DBG.MSG ("NamedPipe.StartServer - \"{0}\" END\n", name);
        }

        public void StartClient (string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            DBG.MSG ("NamedPipe.StartClient - \"{0}\" START\n", name);
            this.type_name = "ClientStream";
            Start (new NamedPipeClientStream (".", name, PipeDirection.InOut, PipeOptions.Asynchronous, 
                        TokenImpersonationLevel.Impersonation), name, connectFunc, recvFunc, funcParam);
            DBG.MSG ("NamedPipe.StartClient - \"{0}\" END\n", name);
        }

        public void Start (PipeStream ps, string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            Stop ();

            this.pipe_name = name;
            this._connectFunc = connectFunc;
            this._recvFunc = recvFunc;
            this._funcParam = funcParam;
            this.pipeStream = ps;
            this.writeStream = new MemoryStream();
            this.recvStream = new MemoryStream ();
            this.recvEvent = new AutoResetEvent(false);
            this.writeEvent = new AutoResetEvent(false);
            this.stopEvent = new ManualResetEvent(false);
            this.recvThreadCompleteEvent = new ManualResetEvent(false);
            this.cancellationTokenSource = new CancellationTokenSource ();

            this.parseThread = new Thread (new ParameterizedThreadStart (ParseThread));
            this.parseThread.Name = "ParseThread";
            this.parseThread.Start ();

            this.recvThread = new Thread (new ParameterizedThreadStart (RecvThread));
            this.recvThread.Name = "RecvThread";
            this.recvThread.Start ();

            this.sendThread = new Thread (new ParameterizedThreadStart (SendThread));
            this.sendThread.Name = "SendThread";
            this.sendThread.Start ();
        }


        public void Stop ()
        {
            DBG.MSG ("NamedPipe.Stop - [{0}]: START\n", this.type_name);

            try
            {
                if (this.stopEvent != null)
                {
                    this.stopEvent.Set ();
                }

                if (this.cancellationTokenSource != null)
                {
                    this.cancellationTokenSource.Cancel ();
                }

                if (this.pipeStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - pipeStream.Close \n");
                    this.pipeStream.Close ();
                }


                if (this.recvThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvThread.Join \n");
                    this.recvThread.Join (100);
                    DBG.MSG ("NamedPipe.Stop - recvThreadCompleteEvent.WaitOne \n");
                    this.recvThreadCompleteEvent.WaitOne();
                }

                if (this.sendThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - sendThread.Join \n");
                    this.sendThread.Join (100);
                }

                if (this.parseThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - parseThread.Join \n");
                    this.parseThread.Join (100);
                }

                if (this.recvStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvStream.Close \n");
                    this.recvStream.Close ();
                }

                if (this.writeStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - writeStream.Close \n");
                    this.writeStream.Close ();
                }

                if (this.writeEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - writeEvent.Close \n");
                    this.writeEvent.Close ();
                }

                if (this.recvEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvEvent.Close \n");
                    this.recvEvent.Close ();
                }

                if (this.stopEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - stopEvent.Close \n");
                    this.stopEvent.Close ();
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("NamedPipe.Stop - [{0}]: Exception, {1}\n", this.type_name, e.Message);
                Debug.Assert(false);
            }

            this.pipeStream = null;
            this.parseThread = null;
            this.recvThread = null;
            this.sendThread = null;
            this.pipe_name = "";
            //this.type_name = "";
            this._recvFunc = null;
            this._funcParam = null;
            this.writeStream = null;
            this.recvStream = null;
            this.recvEvent = null;
            this.writeEvent = null;
            this.stopEvent = null;

            DBG.MSG ("NamedPipe.Stop - [{0}]: END\n", this.type_name);
        }


        public void SendThread (object param)
        {
            DBG.MSG ("NamedPipe.SendThread - [{0}]: START\n", type_name);

            WaitHandle[] handles = new WaitHandle[2] { stopEvent, writeEvent };

            while (!cancellationTokenSource.IsCancellationRequested)
            {
                int result = WaitHandle.WaitAny(handles);
                DBG.MSG("NamedPipe.SendThread - signal WaitHandle({0})\n", result);

                if (result == 0)
                {
                    break;
                }
                try
                {

                    switch (result)
                    {
                    case 1:
                        {
                            byte[] bytes = null;

                            lock (this.writeStream)
                            {
                                bytes = this.writeStream.ToArray ();
                                this.writeStream.SetLength (0);
                            }

                            //DBG.MSG("NamedPipe.SendThread - pipeStream.Write , len={0}\n", bytes.Length);
                            //DBG.DUMP(bytes, bytes.Length);
                            if (this.pipeStream.IsConnected)
                            {
                                this.pipeStream.Write (bytes, 0, bytes.Length);
                            }
                        }
                        break;

                    default:
                        {
                            Debug.Assert (false);
                        }
                        break;
                    }
                }
                catch (Exception e)
                {
                    DBG.MSG ("NamedPipe.SendThread - [{0}]: Exception, {1}\n", this.type_name, e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG ("NamedPipe.SendThread - [{0}]: END\n", this.type_name);
        }

        public async void RecvThread (object param)
        {
            DBG.MSG ("NamedPipe.RecvThread - [{0}]: START\n", this.type_name);

            while (!this.cancellationTokenSource.IsCancellationRequested)
            {
                NamedPipeClientStream clientStream = this.pipeStream as NamedPipeClientStream;
                NamedPipeServerStream serverStream = this.pipeStream as NamedPipeServerStream;
                byte[] bytes = new byte[1024 * 1024];

                try
                {
                    if (!this.pipeStream.IsConnected)
                    {
                        if (clientStream != null)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: clientStream.Connect()\n", this.type_name);
                            await clientStream.ConnectAsync (100, this.cancellationTokenSource.Token);
                        }
                        else if (serverStream != null)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: WaitForConnectionAsync\n", this.type_name);
                            await serverStream.WaitForConnectionAsync (this.cancellationTokenSource.Token);
                        }

                        if (this.pipeStream.IsConnected)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: CONNECTED\n", this.type_name);

                            this._connectFunc (this._funcParam, true);
                        }
                    }
                    else
                    {
                        Debug.Assert(false);
                    }

                    while (!this.cancellationTokenSource.IsCancellationRequested)
                    {
                        int len = await this.pipeStream.ReadAsync (bytes, 0, bytes.Length, this.cancellationTokenSource.Token);
                        DBG.MSG ("NamedPipe.RecvThread - [{0}]: ReadAsync, l={1}\n", this.type_name, len);
                        if (len == 0)
                        {
                            this._connectFunc (this._funcParam, false);

                            this.writeStream.SetLength(0);
                            this.recvStream.SetLength (0);

                            if (serverStream != null)
                            {
                                serverStream.Disconnect ();
                            }
                            else if (clientStream != null)
                            {
                                this.pipeStream.Close();
                                this.pipeStream = new NamedPipeClientStream (".", this.pipe_name, PipeDirection.InOut, PipeOptions.Asynchronous);
                            }

                            break;
                        }

                        lock (this.recvStream)
                        {
                            this.recvStream.Write (bytes, 0, len);
                        }
                        this.recvEvent.Set();
                    }
                }
                catch (ObjectDisposedException e)
                {
                    DBG.MSG ("NamedPipe.RecvThread - [{0}]: ObjectDisposedException, {1}\n", this.type_name, e.Message);
                    break;
                }
                catch (TimeoutException /*e*/)
                {
                    //DBG.MSG ("NamedPipe.RecvThread - [{0}]: TimeoutException, {1}\n", this.type_name, e.Message);
                }
                catch (IOException /*e*/)
                {
                    //DBG.MSG ("NamedPipe.RecvThread - [{0}]: IOException, {1}\n", this.type_name, e.Message);
                }
                catch (Exception e)
                {
                    DBG.MSG ("NamedPipe.RecvThread - [{0}]: Exception, {1}\n", this.type_name, e.Message);
                    //Debug.Assert(false);
                    Thread.Sleep(10);
                }
            }

            DBG.MSG ("NamedPipe.RecvThread - [{0}]: END\n", this.type_name);

            this.recvThreadCompleteEvent.Set();
        }

        public void ParseThread (object param)
        {
            DBG.MSG ("NamedPipe.ParseThread - [{0}]: START\n", this.type_name);
            WaitHandle[] handles = new WaitHandle[2] { this.stopEvent, this.recvEvent };
            List<byte[]> recvList;

            while (!this.cancellationTokenSource.IsCancellationRequested)
            {
                int result = WaitHandle.WaitAny(handles);
                DBG.MSG("NamedPipe.ParseThread - signal WaitHandle({0})\n", result);

                if (result == 0)
                {
                    break;
                }

                lock(this.recvStream)
                {
                    recvList = ParseData(this.recvStream);
                }

                DBG.MSG("NamedPipe.ParseThread - recvList.Count={0}\n", recvList.Count);

                foreach (var v in recvList)
                {
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    this._recvFunc (v, this._funcParam);
                }

                recvList.Clear();
            }

            DBG.MSG ("NamedPipe.ParseThread - [{0}]: END\n", this.type_name);
        }

        public static List<byte[]> ParseData (MemoryStream ms)
        {
            DBG.MSG ("NamedPipe.ParseData - START\n");
            List<byte[]> List = new List<byte[]>();

            while (ms.Length >= 4)
            {
                byte[] bytes = ms.ToArray ();

                //DBG.MSG ("NamedPipe.ParseThread @@@@@@@@@@@@@ \n");
                //DBG.DUMP(bytes, bytes.Length);

                int length = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

                if (bytes.Length < (length + 4))
                {
                    break;
                }

                byte[] b = new byte[length];

                Buffer.BlockCopy (bytes, 4, b, 0, length);

                ms.SetLength (0);
                ms.Write (bytes, 4 + length, bytes.Length - length - 4);

                List.Add (b);
            }

            DBG.MSG ("NamedPipe.ParseData - END\n");

            return List;
        }



        public static byte[] CreateWriteData (byte[] bytes, int offset, int length)
        {
            byte[] result = null;

            using (MemoryStream ms = new MemoryStream ())
            {
                ms.WriteByte ((byte)((length & 0xFF000000) >> 24));
                ms.WriteByte ((byte)((length & 0x00FF0000) >> 16));
                ms.WriteByte ((byte)((length & 0x0000FF00) >> 8));
                ms.WriteByte ((byte)((length & 0x000000FF) >> 0));
                ms.Write (bytes, offset, length);

                result = ms.ToArray ();
            }

            return result;
        }

        public bool WriteDataAsync (byte[] bytes, int offset, int length)
        {
            bytes = CreateWriteData(bytes, 0, length);

            return WriteAsync(bytes, 0, bytes.Length);
        }

        public bool WriteAsync (byte[] bytes, int offset, int length)
        {
            bool result = false;

            //DBG.MSG ("NamedPipe.WriteAsync - [{0}]: START\n", type_name);
            lock (this.writeStream)
            {
                this.writeStream.Write (bytes, offset, length);

                this.writeEvent.Set();

                result = true;
            }
            //DBG.MSG ("NamedPipe.WriteAsync - [{0}]: END\n", type_name);

            return result;
        }
    }
}
