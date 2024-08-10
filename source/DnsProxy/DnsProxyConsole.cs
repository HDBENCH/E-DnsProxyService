using DnsProxyLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace ConsoleApp
{
    internal class DnsProxyConsole
    {
        static bool bRunning = true;

        static void Main (string[] args)
        {
            string basePath = System.AppDomain.CurrentDomain.BaseDirectory;

            DnsProxyServer dnsProxyServer = new DnsProxyServer();
            dnsProxyServer.Start (basePath, ServerConnectFunc, ServerReceiveFunc, ServerStopFunc, null);

            Console.CancelKeyPress += new ConsoleCancelEventHandler (Ctrl_C_Pressed);

            while (bRunning)
            {
                Thread.Sleep (100);

                //dnsProxyServer.Save (); 
            }

            dnsProxyServer.Stop ();
        }

        // ［Ctrl］＋［C］キーが押されたときに呼び出される
        static void Ctrl_C_Pressed (object sender, ConsoleCancelEventArgs e)
        {
            if (bRunning)
            {
                bRunning = false;

                e.Cancel = true;
                //Thread.Sleep(1000);
            }

            //Environment.Exit(0);
        }


        static void ServerReceiveFunc (Command cmd, object param)
        {
            //DBG.MSG ("ServerReceiveFunc, {0}, {1} \n", cmd.GetCMD (), cmd.GetString ());
        }

        static void ServerConnectFunc (object param, bool bConnect)
        {
            //DBG.MSG ("ServerConnectFunc - bConnect={0}\n", bConnect);
        }

        static void ServerStopFunc (object param)
        {
            //DBG.MSG ("ServerStopFunc - bConnect={0}\n", bConnect);
        }
    }
}
