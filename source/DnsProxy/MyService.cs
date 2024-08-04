using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using DnsProxyLibrary;

namespace DnsProxyService
{
    public partial class MyService : ServiceBase
    {
        DnsProxyServer server = new DnsProxyServer();

        public MyService ()
        {
            InitializeComponent ();
        }

        protected override void OnStart (string[] args)
        {
            string basePath = System.AppDomain.CurrentDomain.BaseDirectory;

            server.Start(basePath, ServerConnectFunc, ServerReceiveFunc, null);
        }

        protected override void OnStop ()
        {
            server.Stop();
        }

        static void ServerReceiveFunc (Command cmd, object param)
        {
            //DBG.MSG ("ServerReceiveFunc, {0}, {1} \n", cmd.GetCMD(), cmd.GetString());
        }

        static void ServerConnectFunc (object param, bool bConnect)
        {
            //DBG.MSG ("ServerConnectFunc \n");
        }
    }
}
