using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration.Install;
using System.ServiceProcess;
using Microsoft.SqlServer.Server;
using System.IO;
using System.Threading;
using DnsProxyLibrary;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;

namespace DnsProxyInstaller
{
    internal class DnsProxyInstaller
    {
        [STAThread]
        static void Main (string[] args)
        {
            string basePath = System.AppDomain.CurrentDomain.BaseDirectory;
            string servicePath = basePath + "DnsProxyService.exe";
            string serviceName = "DnsProxyServiceC#";

            try
            {
                if (args.Length == 0)
                {
                    bool bLoop = true;


                    while (bLoop)
                    {
                        //Console.Clear();
                        Console.WriteLine ("");
                        if (IsServiceExists (serviceName))
                        {
                            Console.WriteLine ("{0} : {1}", serviceName, GetServiceStatus(serviceName));
                        }
                        else
                        {
                            Console.WriteLine ("{0} : Not Installed", serviceName);
                        }
                        Console.WriteLine ("");
                        Console.WriteLine ("-----------------------");
                        Console.WriteLine ("1: Install");
                        Console.WriteLine ("2: Uninstall");
                        Console.WriteLine ("3: Start");
                        Console.WriteLine ("4: Stop");
                        Console.WriteLine ("-----------------------");
                        Console.WriteLine ("5: Database Convert");
                        Console.WriteLine ("-----------------------");
                        Console.WriteLine ("6: Refresh");
                        Console.WriteLine ("7: Exit");

                        while (!Console.KeyAvailable)
                        {
                            Thread.Sleep (10);
                        }

                        Console.Clear();

                        var key = Console.ReadKey(true);
                        
                        switch (key.KeyChar)
                        {
                        case '1':
                            {
                                DoServiceInstall (serviceName, servicePath);
                            }
                            break;
                        case '2':
                            {
                                DoServiceUninstall (serviceName, servicePath);
                            }
                            break;
                        case '3':
                            {
                                StartService (serviceName);
                            }
                            break;
                        case '4':
                            {
                                StopService (serviceName);
                            }
                            break;
                        case '5':
                            {
                                DataBaseConvert(basePath);
                            }
                            break;
                        case '6':
                            {
                            }
                            break;
                        case '7':
                            {
                                bLoop = false;
                            }
                            break;
                        }
                    }

                    return;
                }


                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                    case "-Install":
                    case "/Install":
                        {
                            DoServiceInstall (serviceName, servicePath);
                            //DoServiceInstall (args[i + 1], args[i + 2]);
                            //i+=2;
                        }
                        break;

                    case "-Uninstall":
                    case "/Uninstall":
                        {
                            DoServiceUninstall (serviceName, servicePath);
                            //DoServiceUninstall (args[i + 1], args[i + 2]);
                            //i+=2;
                        }
                        break;

                    case "-Start":
                    case "/Start":
                        {
                            //i++;
                            //StartService (args[i]);
                            StartService (serviceName);
                        }
                        break;

                    case "-Stop":
                    case "/Stop":
                        {
                            //i++;
                            //StopService (args[i]);
                            StopService (serviceName);
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine ("Main Exception, {0}", e.ToString ());
            }

        }

        static void DataBaseConvert (string basePath)
        {
            //Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

            //FolderBrowserDialogクラスのインスタンスを作成
            FolderBrowserDialog fbd = new FolderBrowserDialog();

            fbd.Description = "Select the Hosts folder";
            fbd.RootFolder = Environment.SpecialFolder.Desktop;
            fbd.SelectedPath = @"C:\Windows";
            fbd.ShowNewFolderButton = false;

            do
            {
                //ダイアログを表示する
                if (fbd.ShowDialog () != DialogResult.OK)
                {
                    break;
                }

                //選択されたフォルダを表示する
                Console.WriteLine (fbd.SelectedPath);

                DataBase data = new DataBase ();

                Console.WriteLine ("Converting, please wait...");
                
                counter = 0;
                sw.Reset();
                sw.Start ();

                data.ImportFolder (fbd.SelectedPath, DataBaseConvertCallBack);
                Console.Write ("{0}     \r", counter);

                string out_path =basePath + "database";
                while (File.Exists (out_path))
                {
                    out_path = string.Format("{0}{1}_{2}{3}{4}_{5}{6}{7}", basePath, "database", DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
                }

                data.Export (out_path);
            }
            while (false);
        }

        static int counter = 0;
        static Stopwatch sw = new Stopwatch(); 

        static void DataBaseConvertCallBack (string host)
        {
            counter++;

            if (sw.ElapsedMilliseconds > 100)
            {
                Console.Write ("{0}     \r", counter);
                sw.Restart ();
            }

        }


        /// <summary>
        /// サービスをインストール
        /// </summary>
        /// <returns></returns>
        static bool DoServiceInstall (string ServiceName, string path)
        {
            bool result = false;        // 結果

            try
            {
                do
                {
                    if (!File.Exists (path))
                    {
                        Console.WriteLine ("{0} not found.", path);
                        break;
                    }

                    if (IsServiceExists (ServiceName))
                    {
                        Console.WriteLine ("{0} is already installed.", ServiceName);
                        break;
                    }

                    ManagedInstallerClass.InstallHelper (new string[] { path });
                    result = true;
                }
                while (false);
            }
            catch (Exception e)
            {
                Console.WriteLine ("DoServiceInstall Exception, {0}", e.ToString ());
            }

            return result;
        }

        /// <summary>
        /// サービスをアンインストール
        /// </summary>
        /// <returns></returns>
        static bool DoServiceUninstall (string ServiceName, string path)
        {
            bool result = false;        // 結果

            try
            {
                do
                {
                    if (!File.Exists (path))
                    {
                        Console.WriteLine ("{0} not found.", path);
                        break;
                    }

                    if (!IsServiceExists (ServiceName))
                    {
                        Console.WriteLine ("{0} not found.", ServiceName);
                        break;
                    }

                    ManagedInstallerClass.InstallHelper (new string[] { "/u", path });
                    result = true;
                }
                while (false);
            }
            catch (Exception e)
            {
                Console.WriteLine ("DoServiceUninstall Exception, {0}", e.ToString ());
            }

            return result;
        }

        /// <summary>
        /// サービスがインストールされているか確認
        /// </summary>
        /// <returns>インストールされているか (false=インストールされていない/true=インストールされている)</returns>
        static bool IsServiceExists (string ServiceName)
        {
            bool result = false;

            try
            {
                ServiceController[] services = ServiceController.GetServices();      // 全てのサービス

                foreach (var v in services)
                {
                    if (v.ServiceName == ServiceName)
                    {
                        result = true;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine ("IsServiceExists Exception, {0}", e.ToString ());
            }

            return result;
        }

        /// <summary>
        /// サービス開始
        /// </summary>
        static ServiceControllerStatus GetServiceStatus (string ServiceName)
        {
            ServiceControllerStatus result = ServiceControllerStatus.Stopped;

            try
            {
                using (ServiceController service_controller = new ServiceController (ServiceName))
                {
                    result = service_controller.Status;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine ("GetServiceStatus Exception, {0}", e.ToString ());
            }

            return result;
        }

        /// <summary>
        /// サービス開始
        /// </summary>
        static bool StartService (string ServiceName)
        {
            bool result = false;

            try
            {
                using (ServiceController service_controller = new ServiceController (ServiceName))
                {
                    if (service_controller.Status == ServiceControllerStatus.Stopped)
                    {
                        service_controller.Start ();
                        service_controller.WaitForStatus (ServiceControllerStatus.Running);
                    }

                    result = service_controller.Status == ServiceControllerStatus.Running;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine ("StartService Exception, {0}", e.ToString ());
            }

            return result;
        }

        /// <summary>
        /// サービス停止
        /// </summary>
        static bool StopService (string ServiceName)
        {
            bool result = false;

            try
            {
                using (ServiceController service_controller = new ServiceController (ServiceName))
                {
                    service_controller.Refresh ();

                    if (service_controller.Status == ServiceControllerStatus.Running)
                    {
                        service_controller.Stop ();
                        service_controller.WaitForStatus (ServiceControllerStatus.Stopped);
                    }
                    result = service_controller.Status == ServiceControllerStatus.Stopped;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine ("StopService Exception, {0}", e.ToString ());
            }

            return result;
        }
    }
}
