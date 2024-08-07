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
                if (pipeStream != null)
                {
                    result = pipeStream.IsConnected;
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("NamedPipe.IsConnected - [{0}]: Exception, {0}\n", type_name, e.Message);
                Debug.Assert(false);
            }

            return result;
        }


        public void StartServer (string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            DBG.MSG ("NamedPipe.StartServer - \"{0}\" START\n", name);
            type_name = "ServerStream";
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule("Everyone", PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));

            Start (new NamedPipeServerStream (name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024, 1024, ps), name, connectFunc, recvFunc, funcParam);
            DBG.MSG ("NamedPipe.StartServer - \"{0}\" END\n", name);
        }

        public void StartClient (string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            DBG.MSG ("NamedPipe.StartClient - \"{0}\" START\n", name);
            type_name = "ClientStream";
            Start (new NamedPipeClientStream (".", name, PipeDirection.InOut, PipeOptions.Asynchronous, 
                        TokenImpersonationLevel.Impersonation), name, connectFunc, recvFunc, funcParam);
            DBG.MSG ("NamedPipe.StartClient - \"{0}\" END\n", name);
        }

        public void Start (PipeStream ps, string name, ConnectFunc connectFunc, ReceiveFunc recvFunc, object funcParam)
        {
            Stop ();

            pipe_name = name;
            _connectFunc = connectFunc;
            _recvFunc = recvFunc;
            _funcParam = funcParam;
            pipeStream = ps;
            writeStream = new MemoryStream();
            recvStream = new MemoryStream ();
            recvEvent = new AutoResetEvent(false);
            writeEvent = new AutoResetEvent(false);
            stopEvent = new ManualResetEvent(false);
            recvThreadCompleteEvent = new ManualResetEvent(false);
            cancellationTokenSource = new CancellationTokenSource ();

            parseThread = new Thread (new ParameterizedThreadStart (ParseThread));
            parseThread.Name = "ParseThread";
            parseThread.Start ();

            recvThread = new Thread (new ParameterizedThreadStart (RecvThread));
            recvThread.Name = "RecvThread";
            recvThread.Start ();

            sendThread = new Thread (new ParameterizedThreadStart (SendThread));
            sendThread.Name = "SendThread";
            sendThread.Start ();
        }


        public void Stop ()
        {
            DBG.MSG ("NamedPipe.Stop - [{0}]: START\n", type_name);

            try
            {
                if (stopEvent != null)
                {
                    stopEvent.Set ();
                }

                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel ();
                }

                if (pipeStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - pipeStream.Close \n");
                    pipeStream.Close ();
                }


                if (recvThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvThread.Join \n");
                    recvThread.Join ();
                    DBG.MSG ("NamedPipe.Stop - recvThreadCompleteEvent.WaitOne \n");
                    recvThreadCompleteEvent.WaitOne();
                }

                if (sendThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - sendThread.Join \n");
                    sendThread.Join ();
                }

                if (parseThread != null)
                {
                    DBG.MSG ("NamedPipe.Stop - parseThread.Join \n");
                    parseThread.Join ();
                }

                if (recvStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvStream.Close \n");
                    recvStream.Close ();
                }

                if (writeStream != null)
                {
                    DBG.MSG ("NamedPipe.Stop - writeStream.Close \n");
                    writeStream.Close ();
                }

                if (writeEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - writeEvent.Close \n");
                    writeEvent.Close ();
                }

                if (recvEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - recvEvent.Close \n");
                    recvEvent.Close ();
                }

                if (stopEvent != null)
                {
                    DBG.MSG ("NamedPipe.Stop - stopEvent.Close \n");
                    stopEvent.Close ();
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("NamedPipe.Stop - [{0}]: Exception, {1}\n", type_name, e.Message);
                Debug.Assert(false);
            }

            pipeStream = null;
            parseThread = null;
            recvThread = null;
            sendThread = null;
            pipe_name = "";
            //type_name = "";
            _recvFunc = null;
            _funcParam = null;
            writeStream = null;
            recvStream = null;
            recvEvent = null;
            writeEvent = null;
            stopEvent = null;

            DBG.MSG ("NamedPipe.Stop - [{0}]: END\n", type_name);
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

                            lock (writeStream)
                            {
                                bytes = writeStream.ToArray ();
                                writeStream.SetLength (0);
                            }

                            //DBG.MSG("NamedPipe.SendThread - pipeStream.Write , len={0}\n", bytes.Length);
                            //DBG.DUMP(bytes, bytes.Length);
                            if (pipeStream.IsConnected)
                            {
                                pipeStream.Write (bytes, 0, bytes.Length);
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
                    DBG.MSG ("NamedPipe.SendThread - [{0}]: Exception, {1}\n", type_name, e.Message);
                    Debug.Assert(false);
                }
            }

            DBG.MSG ("NamedPipe.SendThread - [{0}]: END\n", type_name);
        }

        public async void RecvThread (object param)
        {
            DBG.MSG ("NamedPipe.RecvThread - [{0}]: START\n", type_name);

            while (!cancellationTokenSource.IsCancellationRequested)
            {
                NamedPipeClientStream clientStream = pipeStream as NamedPipeClientStream;
                NamedPipeServerStream serverStream = pipeStream as NamedPipeServerStream;
                byte[] bytes = new byte[1024];

                try
                {
                    if (!pipeStream.IsConnected)
                    {
                        if (clientStream != null)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: clientStream.Connect()\n", type_name);
                            await clientStream.ConnectAsync (100, cancellationTokenSource.Token);
                        }
                        else if (serverStream != null)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: WaitForConnectionAsync\n", type_name);
                            await serverStream.WaitForConnectionAsync (cancellationTokenSource.Token);
                        }

                        if (pipeStream.IsConnected)
                        {
                            DBG.MSG ("NamedPipe.RecvThread - [{0}]: CONNECTED\n", type_name);

                            this._connectFunc (_funcParam, true);
                        }
                    }
                    else
                    {
                        Debug.Assert(false);
                    }

                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        int len = await pipeStream.ReadAsync (bytes, 0, bytes.Length, cancellationTokenSource.Token);
                        //DBG.MSG ("NamedPipe.RecvThread - [{0}]: ReadAsync, l={1}\n", type_name, len);
                        if (len == 0)
                        {
                            this._connectFunc (_funcParam, false);

                            writeStream.SetLength(0);
                            recvStream.SetLength (0);

                            if (serverStream != null)
                            {
                                serverStream.Disconnect ();
                            }
                            else if (clientStream != null)
                            {
                                pipeStream.Close();
                                pipeStream = new NamedPipeClientStream (".", pipe_name, PipeDirection.InOut, PipeOptions.Asynchronous);
                            }

                            break;
                        }

                        lock (recvStream)
                        {
                            recvStream.Write (bytes, 0, len);
                        }
                        recvEvent.Set();
                    }
                }
                catch (ObjectDisposedException e)
                {
                    DBG.MSG ("NamedPipe.RecvThread - [{0}]: ObjectDisposedException, {1}\n", type_name, e.Message);
                    break;
                }
                catch (TimeoutException /*e*/)
                {
                    //DBG.MSG ("NamedPipe.RecvThread - [{0}]: TimeoutException, {1}\n", type_name, e.Message);
                }
                catch (IOException /*e*/)
                {
                    //DBG.MSG ("NamedPipe.RecvThread - [{0}]: IOException, {1}\n", type_name, e.Message);
                }
                catch (Exception e)
                {
                    DBG.MSG ("NamedPipe.RecvThread - [{0}]: Exception, {1}\n", type_name, e.Message);
                    //Debug.Assert(false);
                    Thread.Sleep(100);
                }
            }

            DBG.MSG ("NamedPipe.RecvThread - [{0}]: END\n", type_name);

            recvThreadCompleteEvent.Set();
        }

        public void ParseThread (object param)
        {
            DBG.MSG ("NamedPipe.ParseThread - [{0}]: START\n", type_name);
            WaitHandle[] handles = new WaitHandle[2] { stopEvent, recvEvent };
            List<byte[]> recvList;

            while (!cancellationTokenSource.IsCancellationRequested)
            {
                int result = WaitHandle.WaitAny(handles);
                DBG.MSG("NamedPipe.ParseThread - signal WaitHandle({0})\n", result);

                if (result == 0)
                {
                    break;
                }

                lock(recvStream)
                {
                    recvList = ParseData(recvStream);
                }

                foreach (var v in recvList)
                {
                    this._recvFunc (v, _funcParam);
                }

                recvList.Clear();
            }

            DBG.MSG ("NamedPipe.ParseThread - [{0}]: END\n", type_name);
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
            lock (writeStream)
            {
                writeStream.Write (bytes, offset, length);

                writeEvent.Set();

                result = true;
            }
            //DBG.MSG ("NamedPipe.WriteAsync - [{0}]: END\n", type_name);

            return result;
        }
    }
}
