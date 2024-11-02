using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DnsProxyLibrary;
using System.Diagnostics;
using System.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Collections.Concurrent;
using System.Reflection;
using System.Timers;
using System.Xml.Linq;
using static DnsProxyLibrary.DnsProtocol;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolBar;
using Microsoft.Win32;
using System.IO;

namespace DnsProxyAdmin
{
    public partial class Form1 : Form
    {
        private DnsProxyClient dnsProxyClient = new DnsProxyClient();
        private ImageList listViewImageList = new ImageList();
        private ConcurrentDictionary<string, TreeNode> hostNameToNodeDic = new ConcurrentDictionary<string, TreeNode>();
        private ConcurrentDictionary<TreeNode, string> nodeToHostNameDic = new ConcurrentDictionary<TreeNode, string>();
        private ColumnHeader columnTime = new ColumnHeader();
        private ColumnHeader columnType = new ColumnHeader();
        private ColumnHeader columnIp = new ColumnHeader();
        private ColumnHeader columnHost = new ColumnHeader();
        private ColumnHeader columnInfo = new ColumnHeader();
        private ColumnHeader columnComment = new ColumnHeader();
        private object listViewItemListLock = new object();
        private List<string> historyAddList = new List<string>();
        private List<string> setHistoryAddList = new List<string>();
        private List<string> setHistoryDelList = new List<string>();
        private List<ListViewItem> listViewItemList;
        private List<ListViewItem> listViewItemAllList = new List<ListViewItem>();
        private List<ListViewItem> listViewItemAcceptList = new List<ListViewItem>();
        private List<ListViewItem> listViewItemRejectList = new List<ListViewItem>();
        private List<ListViewItem> listViewItemIgnoreList = new List<ListViewItem>();
        private List<ListViewItem> listViewItemAnswerList = new List<ListViewItem>();
        private List<ListViewItem> listViewItemSetList = new List<ListViewItem>();
        private System.Windows.Forms.Timer timer1Sec = new System.Windows.Forms.Timer();
        private bool bProxyEnable = false;
        private bool bIsClosing = false;
        private bool bViewScroll = true;
        private string basePath;
        private string configPath;
        private VIEW_MODE viewMode = VIEW_MODE.All;

        enum VIEW_MODE
        {
            All,
            Accept,
            Reject,
            Ignore,
            Answer,
            Set,
        }

        public Form1 ()
        {
            InitializeComponent ();
        }

        private void Form1_Load (object sender, EventArgs e)
        {
            //TreeView
            this.listViewImageList.ColorDepth = ColorDepth.Depth32Bit;
            this.listViewImageList.Images.Add (Properties.Resources.None);
            this.listViewImageList.Images.Add (Properties.Resources.LightNone);
            this.listViewImageList.Images.Add (Properties.Resources.Accept);
            this.listViewImageList.Images.Add (Properties.Resources.LightAccept);
            this.listViewImageList.Images.Add (Properties.Resources.Reject);
            this.listViewImageList.Images.Add (Properties.Resources.LightReject);
            this.listViewImageList.Images.Add (Properties.Resources.Ignore);
            this.listViewImageList.Images.Add (Properties.Resources.LightIgnore);
            this.treeView1.ImageList = this.listViewImageList;
            this.treeView1.Sorted = true;

            //ListView
            this.columnTime.Text = "Time";
            this.columnTime.Width = 130;
            this.columnType.Text = "Type";
            this.columnType.Width = 80;
            this.columnIp.Text = "IP";
            this.columnIp.Width = 110;
            this.columnHost.Text = "Host";
            this.columnHost.Width = 250;
            this.columnInfo.Text = "Info";
            this.columnInfo.Width = 250;
            this.columnComment.Text = "Comment";
            this.columnComment.Width = 300;
            this.listView1.Columns.AddRange(new ColumnHeader[] { this.columnTime, this.columnType, this.columnIp, this.columnHost, this.columnInfo, this.columnComment });

            this.listView1.GetType ().InvokeMember ("DoubleBuffered",
                                                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                                                null,
                                                this.listView1,
                                                new object[] { true });


            //Timer
            this.timer1Sec.Tick += new EventHandler(TimerEventProcessor);
            this.timer1Sec.Interval = 500;
            this.timer1Sec.Start ();


            //Menu
            ProxyEnable(false);
            MenuEnable(false);

            this.basePath = System.AppDomain.CurrentDomain.BaseDirectory;
            if(this.basePath.Substring(this.basePath.Length - 1, 1) != "\\")
            {
                this.basePath += "\\";
            }
            this.configPath = this.basePath + "config.ini";

            Config config = new Config();
            config.Load(this.configPath);

            this.Bounds                 = (Rectangle)config.GetValue (Config.Name.admin_FormBounds  , this.Bounds);
            this.splitContainer1.SplitterDistance
                                        = (int)config.GetValue (Config.Name.admin_SplitterDistance  , this.splitContainer1.SplitterDistance);
            this.columnTime.Width       = (int)config.GetValue (Config.Name.admin_columnTime        , this.columnTime.Width);
            this.columnType.Width       = (int)config.GetValue (Config.Name.admin_columnType        , this.columnType.Width);
            this.columnIp.Width         = (int)config.GetValue (Config.Name.admin_columnIp          , this.columnIp.Width);
            this.columnHost.Width       = (int)config.GetValue (Config.Name.admin_columnHost        , this.columnHost.Width);
            this.columnInfo.Width       = (int)config.GetValue (Config.Name.admin_columnInfo        , this.columnInfo.Width);
            this.columnComment.Width    = (int)config.GetValue (Config.Name.admin_columnComment     , this.columnComment.Width);
            this.bViewScroll            = (bool)config.GetValue(Config.Name.admin_ViewScroll        , this.bViewScroll);
            this.viewMode               = (VIEW_MODE)config.GetValue(Config.Name.admin_ViewMode     , this.viewMode);
            config.Save(this.configPath);

            ViewScroll(this.bViewScroll);
            contextMenu_View(this.viewMode);

            this.dnsProxyClient.Start (PipeConnect, PipeReceive, this);
        }

        private void Form1_FormClosed (object sender, FormClosedEventArgs e)
        {
            Config config = new Config();
            config.Load(this.configPath);

            config.SetValue (Config.Name.admin_FormBounds       , (this.WindowState == FormWindowState.Normal) ? this.Bounds : this.RestoreBounds);
            config.SetValue (Config.Name.admin_SplitterDistance , this.splitContainer1.SplitterDistance);
            config.SetValue (Config.Name.admin_columnTime       , this.columnTime.Width);
            config.SetValue (Config.Name.admin_columnType       , this.columnType.Width);
            config.SetValue (Config.Name.admin_columnIp         , this.columnIp.Width);
            config.SetValue (Config.Name.admin_columnHost       , this.columnHost.Width);
            config.SetValue (Config.Name.admin_columnInfo       , this.columnInfo.Width);
            config.SetValue (Config.Name.admin_columnComment    , this.columnComment.Width);
            config.SetValue (Config.Name.admin_ViewScroll       , this.bViewScroll);
            config.SetValue (Config.Name.admin_ViewMode         , this.viewMode);
            config.Save(this.configPath);

            this.bIsClosing = true;
            this.dnsProxyClient.Stop();

        }


        private void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            string[] historyAddArray = null;
            string[] setHistoryAddArray = null;
            string[] setHistoryDelArray = null;
            int count = this.listViewItemList.Count;
            bool bUpdate = false;

            lock (this.listViewItemListLock)
            {
                if (this.historyAddList.Count > 0)
                {
                    historyAddArray = new string[this.historyAddList.Count];
                    this.historyAddList.CopyTo (historyAddArray);
                    this.historyAddList.Clear ();
                }
                if (this.setHistoryAddList.Count > 0)
                {
                    setHistoryAddArray = new string[this.setHistoryAddList.Count];
                    this.setHistoryAddList.CopyTo (setHistoryAddArray);
                    this.setHistoryAddList.Clear ();
                }
                if (this.setHistoryDelList.Count > 0)
                {
                    setHistoryDelArray = new string[this.setHistoryDelList.Count];
                    this.setHistoryDelList.CopyTo (setHistoryDelArray);
                    this.setHistoryDelList.Clear ();
                }
            }

            if (historyAddArray != null)
            {
                for (int i = 0; i < historyAddArray.Count (); i++)
                {
                    HistoryData data = new HistoryData();
                    data.FromString (historyAddArray[i]);

                    ListViewItem item = new ListViewItem(data.ToArray());
                    item.Tag = data;
                    lock (this.listViewItemListLock)
                    {
                        switch (data.flags)
                        {
                        case DataBase.FLAGS.None:
                        case DataBase.FLAGS.Disable:
                            {
                                this.listViewItemAllList.Add (item);
                            }
                            break;
                        case DataBase.FLAGS.Accept:
                            {
                                this.listViewItemAllList.Add (item);
                                this.listViewItemAcceptList.Add (item);
                            }
                            break;
                        case DataBase.FLAGS.Reject:
                            {
                                this.listViewItemAllList.Add (item);
                                this.listViewItemRejectList.Add (item);
                            }
                            break;
                        case DataBase.FLAGS.Ignore:
                            {
                                this.listViewItemAllList.Add (item);
                                this.listViewItemIgnoreList.Add (item);
                            }
                            break;
                        case DataBase.FLAGS.Answer:
                            {
                                this.listViewItemAllList.Add (item);
                                this.listViewItemAnswerList.Add (item);
                            }
                            break;
                        case DataBase.FLAGS.SetNone:
                        case DataBase.FLAGS.SetAccept:
                        case DataBase.FLAGS.SetReject:
                        case DataBase.FLAGS.SetIgnore:
                            {
                                this.listViewItemAllList.Add (item);
                                this.listViewItemSetList.Add (item);
                            }
                            break;
                        default:
                            {
                                Debug.Assert (false);
                            }
                            break;
                        }

                    }
                }
            }

            if (setHistoryAddArray != null)
            {
                for (int i = 0; i < setHistoryAddArray.Count (); i++)
                {
                    HistoryData data = new HistoryData();
                    data.FromString (setHistoryAddArray[i]);

                    ListViewItem item = new ListViewItem(data.ToArray());
                    item.Tag = data;

                    lock (this.listViewItemListLock)
                    {
                        this.listViewItemSetList.Add (item);
                    }
                }
            }

            if (setHistoryDelArray != null)
            {
                List<ListViewItem> list = new List<ListViewItem>();
                Dictionary<string, HistoryData> map = new Dictionary<string, HistoryData>();

                for (int i = 0; i < setHistoryDelArray.Count (); i++)
                {
                    HistoryData data = new HistoryData();
                    data.FromString (setHistoryDelArray[i]);

                    map.Add (data.ToString (), data);
                }

                lock (this.listViewItemListLock)
                {
                    foreach (var v in this.listViewItemSetList)
                    {
                        HistoryData d = v.Tag as HistoryData;
                        if (d != null)
                        {
                            if (map.ContainsKey(d.ToString()))
                            {
                                list.Add (v);
                            }
                        }
                    }

                    foreach (var v in list)
                    {
                        this.listViewItemSetList.Remove (v);
                    }

                }
            }

            bUpdate = (count != this.listViewItemList.Count);

            int max_count = 2000;
            if (this.listViewItemAllList.Count > max_count)
            {
                this.listViewItemAllList.RemoveRange (0, this.listViewItemAllList.Count - max_count);
            }
            if (this.listViewItemAcceptList.Count > max_count)
            {
                this.listViewItemAcceptList.RemoveRange (0, this.listViewItemAcceptList.Count - max_count);
            }
            if (this.listViewItemRejectList.Count > max_count)
            {
                this.listViewItemRejectList.RemoveRange (0, this.listViewItemRejectList.Count - max_count);
            }
            if (this.listViewItemIgnoreList.Count > max_count)
            {
                this.listViewItemIgnoreList.RemoveRange (0, this.listViewItemIgnoreList.Count - max_count);
            }
            if (this.listViewItemAnswerList.Count > max_count)
            {
                this.listViewItemAnswerList.RemoveRange (0, this.listViewItemAnswerList.Count - max_count);
            }
            if (this.listViewItemSetList.Count > max_count)
            {
                this.listViewItemSetList.RemoveRange (0, this.listViewItemSetList.Count - max_count);
            }

            if (bUpdate)
            {
                UpdateListVew ();
            }
        }

        void UpdateListVew ()
        {
            if ((this.listViewItemList.Count > 0) && (this.listView1.VirtualListSize == this.listViewItemList.Count))
            {
                this.listView1.VirtualListSize = this.listViewItemList.Count - 1; 
            }
        
            this.listView1.VirtualListSize = this.listViewItemList.Count;
            
            if (this.bViewScroll && (this.listViewItemList.Count > 0))
            {
                this.listView1.EnsureVisible (this.listViewItemList.Count - 1);
            }
        }

        private void listView1_RetrieveVirtualItem (object sender, RetrieveVirtualItemEventArgs e)
        {
            lock (this.listViewItemListLock)
            {
                try
                {
                    e.Item = this.listViewItemList[e.ItemIndex];
                }
                catch (Exception ex)
                {
                    DBG.MSG("Form1.listView1_RetrieveVirtualItem - Exception, {0}\n", ex.Message);
                }

            }
        }





        void treeImageIndexUpdate (TreeNode node)
        {
            string host = NodeToHostName (node);
            DataBase db = this.dnsProxyClient.FindDataBase(host);

            if (db == null)
            {
                Debug.Assert(false);
                return; 
            }

            switch (db.GetFlags ())
            {
            case DataBase.FLAGS.None:
                {
                    DataBase d = db.GetParent();

                    while (d != null)
                    {
                        if (d.GetFlags () != DataBase.FLAGS.None)
                        {
                            switch (d.GetFlags ())
                            {
                            case DataBase.FLAGS.Accept:
                                {
                                    node.ImageIndex = node.SelectedImageIndex = 3;
                                }
                                break;
                            case DataBase.FLAGS.Reject:
                                {
                                    node.ImageIndex = node.SelectedImageIndex = 5;
                                }
                                break;
                            case DataBase.FLAGS.Ignore:
                                {
                                    node.ImageIndex = node.SelectedImageIndex = 7;
                                }
                                break;
                            default:
                                {
                                    Debug.Assert (false);
                                }
                                break;
                            }

                            break;
                        }

                        d = d.GetParent();
                    }
                }
                break;
            case DataBase.FLAGS.Accept:
                {
                    node.ImageIndex = node.SelectedImageIndex = 2;
                }
                break;
            case DataBase.FLAGS.Reject:
                {
                    node.ImageIndex = node.SelectedImageIndex = 4;
                }
                break;
            case DataBase.FLAGS.Ignore:
                {
                    node.ImageIndex = node.SelectedImageIndex = 6;
                }
                break;
            default:
                {
                    Debug.Assert (false);
                }
                break;
            }
        }
        
        private static int CompareDatabase (KeyValuePair<string, DataBase> x, KeyValuePair<string, DataBase> y)
        {
            return x.Key.CompareTo (y.Key);
        }


        TreeNode TreeInitialize (TreeNode node, DataBase db)
        {
            //DBG.MSG ("Form1.TreeInitialize - {0}:{1}//{2}\n", db.GetFlags().ToString().PadRight(7), db.GetName().PadRight(20), db.GetComment());
            TreeNode newNode = new TreeNode(db.GetName());

            if (string.IsNullOrEmpty (db.GetComment ()))
            {
                newNode.ToolTipText = string.Format ("{0}", db.GetDatetime ());
            }
            else
            {
                newNode.ToolTipText = string.Format ("{0} : {1}", db.GetDatetime (), db.GetComment ());
            }

            this.hostNameToNodeDic.TryAdd(db.GetFullName(), newNode);
            this.nodeToHostNameDic.TryAdd(newNode, db.GetFullName());

            node?.Nodes.Add (newNode);
            
            treeImageIndexUpdate(newNode);

            List<KeyValuePair<string, DataBase>> list = new List<KeyValuePair<string, DataBase>>();
            list = db.GetDataBase().ToList ();
            list.Sort (CompareDatabase);

            foreach (var v in list)
            {
                TreeInitialize(newNode, v.Value);
            }

            return newNode;
        }

        private void TreeInitialize ()
        {
            List<TreeNode> nodes = new List<TreeNode> ();

            this.treeView1.Nodes.Clear();
            this.hostNameToNodeDic.Clear();
            this.nodeToHostNameDic.Clear();

            DataBase db = dnsProxyClient.GetDataBase ();
            List<KeyValuePair<string, DataBase>> list = new List<KeyValuePair<string, DataBase>>();
            list = db.GetDataBase().ToList ();
            //list.Sort (CompareDatabase);

            foreach (var v in list)
            {
                nodes.Add(TreeInitialize(this.treeView1.TopNode, v.Value));
            }

            this.treeView1.Nodes.AddRange(nodes.ToArray());
            this.treeView1.TopNode?.Expand ();
        }

        private void TreeAdd (string host)
        {
            string[] hosts;
            DataBase db = this.dnsProxyClient.GetDataBase();
            TreeNode node = this.treeView1.TopNode;

            do
            {
                hosts = host.Split ('.');
                if ((hosts == null) || (hosts.Length == 0))
                {
                    break;
                }

                host = "";

                for (int i = 0; i < hosts.Length; i++)
                {
                    string h = hosts[hosts.Length - i - 1];
                    host = string.Format("{0}{1}{2}", h, string.IsNullOrEmpty(host)?"":".", host);

                    if (!this.hostNameToNodeDic.TryGetValue (host, out TreeNode n))
                    {
                        db = this.dnsProxyClient.FindDataBase (host);
                        if (db == null)
                        {
                            bool bModifyed = false;
                            db = this.dnsProxyClient.GetDataBase().SetFlags(host, DataBase.FLAGS.None, ref bModifyed);
                        }

                        n = new TreeNode(h);
                        if (string.IsNullOrEmpty (db.GetComment ()))
                        {
                            n.ToolTipText = string.Format ("{0}", db.GetDatetime ());
                        }
                        else
                        {
                            n.ToolTipText = string.Format ("{0} : {1}", db.GetDatetime (), db.GetComment ());
                        }
                        node.Nodes.Add(n);

                        this.hostNameToNodeDic.TryAdd(db.GetFullName(), n);
                        this.nodeToHostNameDic.TryAdd(n, db.GetFullName());
            
                        treeImageIndexUpdate(n);
                    }

                    node = n;
                }
            }
            while (false);

        }


        string NodeToHostName (TreeNode node)
        {
            string result = "";

            if (!this.nodeToHostNameDic.TryGetValue (node, out result))
            {
                Debug.Assert(false);
            }

            return result;
        }

        TreeNode HostNameToNode (string host)
        {
            TreeNode node = null;

            if (!this.hostNameToNodeDic.TryGetValue (host, out node))
            {
                Debug.Assert(false);
            }

            return node;
        }

        void ProxyEnable (bool enable)
        {
            this.bProxyEnable = enable;
            if (this.bProxyEnable)
            {
                this.toolStripStatusEnabled.Text = "Proxy Enable";
                this.toolStripStatusEnabled.BackColor = Color.LightGreen;
                this.proxyEnableToolStripMenuItem.Checked = true;
            }
            else
            {
                this.toolStripStatusEnabled.Text = "Proxy Disable";
                this.toolStripStatusEnabled.BackColor = Color.LightPink;
                this.proxyEnableToolStripMenuItem.Checked = false;
            }
        }

        
        private void listView1_MouseDoubleClick (object sender, MouseEventArgs e)
        {
            ListViewItem item;
            HistoryData historyData;
            string host = "";
            string[] hosts;
            TreeNode node = null;

            do
            {
                item = this.listView1.GetItemAt(e.X, e.Y);
                if (item == null)
                {
                    break;
                }

                historyData = item.Tag as HistoryData;
                if (historyData == null)
                {
                    break;
                }

                hosts = historyData.host.Split('.');
                for (int i = 0; i < hosts.Length; i++)
                {
                    string h = hosts[hosts.Length - i - 1];
                    host = string.Format ("{0}{1}{2}", h, string.IsNullOrEmpty (host) ? "" : ".", host);
                    
                    TreeNode n = HostNameToNode (host);
                    if (n == null)
                    {
                        break;
                    }

                    node = n;
                }

                this.treeView1.SelectedNode = node;
            }
            while (false);
        }


        void change_DataBaseFlags (ToolStripMenuItem item, DataBase.FLAGS flags)
        {
            do
            {
                if (item == null)
                {
                    break;
                }

                DataBase db = item.Tag as DataBase;
                if (db == null)
                {
                    break;
                }

                string host = db.GetFullName();

                this.dnsProxyClient.DataBaseSet(flags, host);
            }
            while (false);
        }

        void contextMenu_Exit (object sender, EventArgs e)
        {
            Close ();
        }

        void contextMenu_DB_Optimization (object sender, EventArgs e)
        {
            this.dnsProxyClient.DataBaseOptimization();
        }

        void contextMenu_Comment (object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            DataBase db = item.Tag as DataBase;

            CommentForm form = new CommentForm();

            form.comment = db.GetComment();
            DialogResult result = form.ShowDialog();
            if (result == DialogResult.OK)
            {
                this.dnsProxyClient.DataBaseSetComment(db.GetFullName(), form.comment);
            }

        }

        void contextMenu_DeleteSetlog (object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            HistoryData data = item.Tag as HistoryData;

            this.dnsProxyClient.DataBaseDelSetlog(data);
        }
        void contextMenu_Delete (object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            string host = item.Tag as string;

            this.dnsProxyClient.DataBaseDel(host);
        }

        void contextMenu_None (object sender, EventArgs e)
        {
            change_DataBaseFlags(sender as ToolStripMenuItem, DataBase.FLAGS.None);
        }
        void contextMenu_Accept (object sender, EventArgs e)
        {
            change_DataBaseFlags(sender as ToolStripMenuItem, DataBase.FLAGS.Accept);
        }
        void contextMenu_Reject (object sender, EventArgs e)
        {
            change_DataBaseFlags(sender as ToolStripMenuItem, DataBase.FLAGS.Reject);
        }
        void contextMenu_Ignore (object sender, EventArgs e)
        {
            change_DataBaseFlags(sender as ToolStripMenuItem, DataBase.FLAGS.Ignore);
        }
        void contextMenu_Copy (object sender, EventArgs e)
        {
            ToolStripMenuItem item;
            string host;

            do
            {
                item = sender as ToolStripMenuItem;
                if (item == null)
                {
                    break;
                }

                host = item.Tag as string;
                if (host == null)
                {
                    break;
                }

                Clipboard.SetData(DataFormats.Text, host);

            }
            while (false);
        }
        void contextMenu_DnsClear(object sender, EventArgs e)
        {
            this.dnsProxyClient.DnsCacheClear ();
        }
        void contextMenu_LogClear(object sender, EventArgs e)
        {
            lock (this.listViewItemListLock)
            {
                this.listViewItemAllList.Clear ();
                this.listViewItemAcceptList.Clear ();
                this.listViewItemRejectList.Clear ();
                this.listViewItemIgnoreList.Clear ();
                this.listViewItemAnswerList.Clear ();
                this.listViewItemSetList.Clear ();
                this.listView1.VirtualListSize = 0;
            }
        }

        void contextMenu_View(VIEW_MODE mode)
        {
            this.viewMode = mode;

            this.allToolStripMenuItem.CheckState = CheckState.Unchecked;
            this.acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            this.rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            this.ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            this.answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            this.setToolStripMenuItem.CheckState = CheckState.Unchecked;

            switch (mode)
            {
            case VIEW_MODE.All:
                {
                    this.allToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemAllList;
                    this.toolStripStatusView.Text = "All View";
                }
                break;

            case VIEW_MODE.Accept:
                {
                    this.acceptToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemAcceptList;
                    this.toolStripStatusView.Text = "Accept View";
                }
                break;

            case VIEW_MODE.Reject:
                {
                    this.rejectToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemRejectList;
                    this.toolStripStatusView.Text = "Reject View";
                }
                break;

            case VIEW_MODE.Ignore:
                {
                    this.ignoreToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemIgnoreList;
                    this.toolStripStatusView.Text = "Ignore View";
                }
                break;

            case VIEW_MODE.Answer:
                {
                    this.answerToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemIgnoreList;
                    this.toolStripStatusView.Text = "Answer View";
                }
                break;

            case VIEW_MODE.Set:
                {
                    this.setToolStripMenuItem.CheckState = CheckState.Checked;
                    this.listViewItemList = this.listViewItemSetList;
                    this.toolStripStatusView.Text = "Set View";
                }
                break;

            default:
                {
                    Debug.Assert (false);
                }
                break;
            }
            UpdateListVew ();
        }

        void contextMenu_ViewAll(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.All);
        }

        void contextMenu_ViewAccept(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.Accept);
        }
        void contextMenu_ViewReject(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.Reject);
        }
        void contextMenu_ViewIgnore(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.Ignore);
        }
        void contextMenu_ViewAnswer(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.Answer);
        }
        void contextMenu_ViewSet(object sender, EventArgs e)
        {
            contextMenu_View (VIEW_MODE.Set);
        }

        void contextMenu_ViewScroll (object sender, EventArgs e)
        {
            ViewScroll(!this.bViewScroll);
        }
        void ViewScroll (bool bEnable)
        {
            this.bViewScroll = bEnable;

            this.scrollToolStripMenuItem.Checked = this.bViewScroll;
            if (this.bViewScroll)
            {
                this.toolStripStatusScroll.BackColor = Color.LightGreen;
                //this.toolStripStatusScroll.Text = "Scroll";
            }
            else
            {
                this.toolStripStatusScroll.BackColor = Color.LightPink;
                //this.toolStripStatusScroll.Text = "      ";
            }
        }

        void contextMenu_ProxyEnable(object sender, EventArgs e)
        {
            this.dnsProxyClient.ProxyEnable(!this.proxyEnableToolStripMenuItem.Checked);
        }

#if false
        private object ObjectDeepCopy (object obj)
        {
            object result = null;

            try
            {
                do
                {
                    if (obj == null)
                    {
                        break;
                    }

                    var type = obj.GetType();

                    if ((type == typeof (String)) || (type.IsValueType & type.IsPrimitive))
                    {
                        result = obj;
                        break;
                    }

                    if (typeof (Delegate).IsAssignableFrom (type))
                    {
                        break;
                    }

                    MethodInfo CloneMethod = typeof(Object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
                    result = CloneMethod?.Invoke (obj, null);

                    if (type.IsArray)
                    {
                        var arrayType = type.GetElementType();
                        if ((arrayType == typeof (String)) || (arrayType.IsValueType & arrayType.IsPrimitive))
                        {
                        }
                        else
                        {
                            Array clonedArray = (Array)result;
                            clonedArray.ForEach((array, indices) => array.SetValue(ObjectDeepCopy(clonedArray.GetValue(indices)), indices));
                        }
                    }

                    foreach (FieldInfo fieldInfo in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy))
                    {
                        if ((fieldInfo.FieldType == typeof (String)) || (fieldInfo.FieldType.IsValueType & fieldInfo.FieldType.IsPrimitive))
                        {
                            continue;
                        }

                        var originalFieldValue = fieldInfo.GetValue(obj);
                        var clonedFieldValue = ObjectDeepCopy(originalFieldValue);
                        fieldInfo.SetValue(result, clonedFieldValue);
                    }

                }
                while (false);
            }
            catch (Exception e)
            {
                DBG.MSG("Form1.ObjectDeepCopy - Exception, {0}\n", e.Message);
            }

            return result;
        }
#endif
        private ToolStripMenuItem MenuCopy (ToolStripMenuItem menuSrc)
        {
#if false
            ToolStripMenuItem menuDst    = new ToolStripMenuItem (menuSrc.Text, null);

            PropertyInfo InfoSrc = menuSrc.GetType().GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
            EventHandlerList listSrc = (EventHandlerList)InfoSrc.GetValue(menuSrc, null);

            PropertyInfo InfoDst = menuDst.GetType().GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
            EventHandlerList listDst = (EventHandlerList)InfoDst.GetValue(menuDst, null);
           
            listDst.AddHandlers(listSrc);
            menuDst.CheckState = menuSrc.CheckState;
#else
            ToolStripMenuItem menuDst = null;

            try
            {
                MethodInfo CloneMethod = typeof(Object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
                menuDst = (ToolStripMenuItem)CloneMethod?.Invoke (menuSrc, null);
            }
            catch (Exception e)
            {
                DBG.MSG("Form1.MenuCopy - Exception, {0}\n", e.Message);
            }
#endif

            return menuDst;
        }

        private void MenuEnable (bool bEnable)
        {
            this.optionToolStripMenuItem.Enabled = bEnable;
            this.viewToolStripMenuItem.Enabled = bEnable;

            foreach (var v in this.optionToolStripMenuItem.DropDownItems)
            {
                ToolStripMenuItem item = v as ToolStripMenuItem;
                if (item != null)
                {
                    item.Enabled = bEnable;
                }
            }

            foreach (var v in this.viewToolStripMenuItem.DropDownItems)
            {
                ToolStripMenuItem item = v as ToolStripMenuItem;
                if (item != null)
                {
                    item.Enabled = bEnable;
                }
            }

            this.toolStripStatusEnabled.Enabled = bEnable;
            this.toolStripStatusView.Enabled = bEnable;
            this.toolStripStatusScroll.Enabled = bEnable;

        }



        private void proxyEnableToolStripMouseUp (object sender, MouseEventArgs e)
        {
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }
                

                ContextMenuStrip Menu = new ContextMenuStrip ();

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(this.proxyEnableToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (this.toolStripStatusEnabled.Bounds.X + e.X, this.toolStripStatusEnabled.Bounds.Y + e.Y));
            }
            while (false);
        }

        private void viewToolStripMouseUp (object sender, MouseEventArgs e)
        {
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }

                ContextMenuStrip Menu = new ContextMenuStrip ();

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(this.allToolStripMenuItem), MenuCopy(this.acceptToolStripMenuItem), MenuCopy(this.rejectToolStripMenuItem), MenuCopy(this.ignoreToolStripMenuItem), MenuCopy(this.answerToolStripMenuItem), MenuCopy(this.setToolStripMenuItem) });
                Menu.Items.Add (new ToolStripSeparator ());
                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(this.logClearToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (this.toolStripStatusView.Bounds.X + e.X, this.toolStripStatusView.Bounds.Y + e.Y));
            }
            while (false);
        }

        private void scrollToolStripMouseUp (object sender, MouseEventArgs e)
        {
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }

                ContextMenuStrip Menu = new ContextMenuStrip ();

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(this.scrollToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (this.toolStripStatusScroll.Bounds.X + e.X, this.toolStripStatusScroll.Bounds.Y + e.Y));
            }
            while (false);
        }



        private void treeviewMouseDown (object sender, MouseEventArgs e)
        {
            TreeNode node = this.treeView1.GetNodeAt(e.X, e.Y);
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }

                if (node == null)
                {
                    break;
                }

                this.treeView1.SelectedNode = node;
            }
            while (false);
        }

        private void treeviewMouseUp (object sender, MouseEventArgs e)
        {
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }

                if (this.treeView1.SelectedNode == null)
                {
                    break;
                }

                string host = NodeToHostName(this.treeView1.SelectedNode);

                DataBase db = dnsProxyClient.FindDataBase(host);
                if (db == null)
                {
                    break;
                }

                ShowContextMenu(db, this.treeView1, e.X, e.Y);
            }
            while (false);
        }

        private void listView1_MouseUp (object sender, MouseEventArgs e)
        {
            do
            {
                if (e.Button != MouseButtons.Right)
                {
                    break;
                }

                var index = this.listView1.SelectedIndices;

                if ((index == null) || (index.Count != 1))
                {
                    ShowContextMenu (null, this.listView1, e.X, e.Y);
                    break;
                }

                ListViewItem item;
                HistoryData historyData;

                lock (this.listViewItemListLock)
                {
                    item = this.listViewItemList[index[0]];
                }

                historyData = item.Tag as HistoryData;

                if (this.listViewItemList == this.listViewItemSetList)
                {
                    ShowContextMenuSetView (historyData, this.listView1, e.X, e.Y);
                }
                else
                {
                    DataBase db = this.dnsProxyClient.FindDataBase(historyData.host);
                    ShowContextMenu (db, this.listView1, e.X, e.Y);
                }
            }
            while (false);

        }

        void ShowContextMenuSetView (HistoryData data, Control control, int x, int y)
        {
            ContextMenuStrip rootMenu = new ContextMenuStrip ();
            ToolStripMenuItem deleteMenu;

            deleteMenu = new ToolStripMenuItem ("&Delete");
            deleteMenu.Tag = data;
            deleteMenu.Click += contextMenu_DeleteSetlog;
            rootMenu.Items.AddRange (new ToolStripMenuItem[] { deleteMenu });
            rootMenu.Show (control, new System.Drawing.Point (x, y));
        }

        void ShowContextMenu (DataBase db, Control control, int x, int y)
        {
            ContextMenuStrip rootMenu = new ContextMenuStrip ();
            ToolStripMenuItem noneMenu;
            ToolStripMenuItem acceptMenu; 
            ToolStripMenuItem rejectMenu;
            ToolStripMenuItem ignoreMenu;
            ToolStripMenuItem copyMenu;
            ToolStripMenuItem commentMenu;
            ToolStripMenuItem deleteMenu;

            while (db != null)
            {
                //ContextMenu
                noneMenu = new ToolStripMenuItem ("&None");
                acceptMenu = new ToolStripMenuItem ("&Accept");
                rejectMenu = new ToolStripMenuItem ("&Reject");
                ignoreMenu = new ToolStripMenuItem ("&Ignore");
                copyMenu = new ToolStripMenuItem ("&Copy");
                commentMenu = new ToolStripMenuItem ("&Edit Comment");
                deleteMenu = new ToolStripMenuItem ("&Delete");

                noneMenu.Tag = db;
                acceptMenu.Tag = db;
                rejectMenu.Tag = db;
                ignoreMenu.Tag = db;
                copyMenu.Tag = db.GetFullName();
                commentMenu.Tag = db;
                deleteMenu.Tag = db.GetFullName();

                noneMenu.Click += contextMenu_None;
                acceptMenu.Click += contextMenu_Accept;
                rejectMenu.Click += contextMenu_Reject;
                ignoreMenu.Click += contextMenu_Ignore;
                copyMenu.Click += contextMenu_Copy;
                commentMenu.Click += contextMenu_Comment;
                deleteMenu.Click += contextMenu_Delete;

                switch (db.GetFlags ())
                {
                case DataBase.FLAGS.None:
                    {
                        noneMenu.Checked = true;
                    }
                    break;
                case DataBase.FLAGS.Accept:
                    {
                        acceptMenu.Checked = true;
                    }
                    break;
                case DataBase.FLAGS.Reject:
                    {
                        rejectMenu.Checked = true;
                    }
                    break;
                case DataBase.FLAGS.Ignore:
                    {
                        ignoreMenu.Checked = true;
                    }
                    break;
                default:
                    {
                        Debug.Assert (false);
                    }
                    break;
                }

                ToolStripMenuItem menu = new ToolStripMenuItem (db.GetFullName());

                menu.DropDownItems.AddRange (new ToolStripMenuItem[] { noneMenu, acceptMenu, rejectMenu, ignoreMenu});
                menu.DropDownItems.Add (new ToolStripSeparator ());
                menu.DropDownItems.AddRange (new ToolStripMenuItem[] { copyMenu, commentMenu });
                menu.DropDownItems.Add (new ToolStripSeparator ());
                menu.DropDownItems.AddRange (new ToolStripMenuItem[] { deleteMenu });

                rootMenu.Items.AddRange (new ToolStripMenuItem[] { menu });

                db = db.GetParent();

                if ((db != null) && (db.GetFullName().IndexOf('.') == -1))
                {
                    break;
                }
            }

            if (rootMenu.Items.Count > 0)
            {
                rootMenu.Items.Add (new ToolStripSeparator ());
            }

            rootMenu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(this.logClearToolStripMenuItem) });

            rootMenu.Show (control, new System.Drawing.Point (x, y));
        }


        void refreshTreeIcon (TreeNode treeNode)
        {
            treeImageIndexUpdate(treeNode);

            foreach (var v in treeNode.Nodes)
            {
                refreshTreeIcon(v as TreeNode);
            }
        }




        void PipeConnect (object param, bool bConnect)
        {
            DBG.MSG ("Form1.PipeConnect - bConnect={0}\n", bConnect);

            //dnsProxyClient.DataBaseSet(DataBase.FLAGS.ACCEPT, "icom.co.jp");

            if (bConnect)
            {
                this.dnsProxyClient.Clear();
                Invoke (new Action<bool> (MenuEnable), true);
            }
            else
            {
                if (!this.bIsClosing)
                {
                    this.dnsProxyClient.Clear ();
                    Invoke (new Action<object, EventArgs> (contextMenu_LogClear), new object[] { null, null });
                    Invoke (new Action (TreeInitialize));
                    Invoke (new Action<bool> (MenuEnable), false);
                }
            }
        }

        void treeEnumNode (TreeNode node, ref List<string> hostList, ref List<TreeNode> nodeList)
        {
            nodeList.Add(node);
            hostList.Add (NodeToHostName(node));

            foreach (var v in node.Nodes)
            {
                TreeNode n = v as TreeNode;
                if (n != null)
                {
                    treeEnumNode(n, ref hostList, ref nodeList);
                }
            }
        }

        void treeDelete (string host)
        {
            TreeNode node = HostNameToNode(host);
            string[] hosts = host.Split ('.');
            List<string> hostList = new List<string>();
            List<TreeNode> nodeList = new List<TreeNode>();
            
            if (node != null)
            {
                treeEnumNode(node, ref hostList, ref nodeList);

                foreach (var v in hostList)
                {
                    if (!this.hostNameToNodeDic.TryRemove (v, out TreeNode t))
                    {
                        Debug.Assert(false);
                    }
                }
                foreach (var v in nodeList)
                {
                    if (!this.nodeToHostNameDic.TryRemove (v, out string h))
                    {
                        Debug.Assert (false);
                    }
                }
                if (node.Parent != null)
                {
                    TreeNode nodeParent = node.Parent;
                    nodeParent.Nodes.Remove (node);
                }
                else
                {
                    this.treeView1.Nodes.Remove (node);
                }
            }
        }

        private void importDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog() { FileName = "database", Filter = "DataBase file | database", CheckFileExists = true })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    dnsProxyClient.DataBaseImport(ofd.FileName);
                }
            }
        }

        void PipeReceive (Command cmd, object param)
        {
            if (this.bIsClosing)
            {
                return;
            }

            byte[] bytes_value = cmd.GetData ();

            //DBG.MSG ("Form1.PipeReceive - {0}\n", cmd.GetCMD());

            switch (cmd.GetCMD ())
            {
            case CMD.NOP:
                {
                }
                break;

            case CMD.LOAD:
                {
                    Invoke( new Action(TreeInitialize));
                }
                break;

            case CMD.ADD:
                {
                    if (bytes_value.Length != 1)
                    {
                        Debug.Assert(false);
                        break;
                    }

                    string host = cmd.GetString();

                    DBG.MSG("Form1.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());
                    
                    Invoke( new Action<string>(TreeAdd), host);

                    //if (hostNameToNodeDic.TryGetValue (host, out TreeNode treeNode))
                    //{
                    //    Invoke( new Action<TreeNode>(refreshTreeIcon), treeNode);
                    //}
                    //else
                    //{
                    //    Debug.Assert (false);
                    //}
                }
                break;

            case CMD.SET:
                {
                    if (bytes_value.Length != 1)
                    {
                        Debug.Assert(false);
                        break;
                    }

                    string host = cmd.GetString();

                    DBG.MSG("Form1.PipeReceive - {0}, {1}, {2}\n", cmd.GetCMD(), (DataBase.FLAGS)bytes_value[0], cmd.GetString());

                    if (this.hostNameToNodeDic.TryGetValue (host, out TreeNode treeNode))
                    {
                        Invoke( new Action<TreeNode>(refreshTreeIcon), treeNode);
                    }
                    else
                    {
                        Debug.Assert (false);
                    }
                }
                break;

            case CMD.DEL:
                {

                    DBG.MSG("Form1.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());
                    Invoke (new Action<string> (treeDelete), cmd.GetString());

                }
                break;

            case CMD.ENABLE:
                {
                    if (bytes_value.Length != 1)
                    {
                        Debug.Assert(false);
                        break;
                    }

                    Invoke(new Action<bool>(ProxyEnable), (bytes_value[0] == 1));
                }
                break;

            case CMD.HISTORY:
                {
                    lock (this.listViewItemListLock)
                    {
                        this.historyAddList.Add (cmd.GetString ());
                    }
                }
                break;

            case CMD.SET_HISTORY:
                {
                    lock (this.listViewItemListLock)
                    {
                        this.setHistoryAddList.Add (cmd.GetString ());
                    }
                }
                break;


            case CMD.DNS_CLEAR:
                {
                }
                break;

            case CMD.COMMENT:
            case CMD.DATETIME:
                {
                    string host = cmd.GetString ();

                    if (!this.hostNameToNodeDic.TryGetValue (host, out TreeNode node))
                    {
                        break;
                    }

                    string comment = Encoding.Default.GetString (bytes_value, 0, bytes_value.Length);

                    DataBase db = this.dnsProxyClient.FindDataBase (host);
                    if (db == null)
                    {
                        break;
                    }

                    if (string.IsNullOrEmpty (db.GetComment ()))
                    {
                        node.ToolTipText = string.Format ("{0}", db.GetDatetime ());
                    }
                    else
                    {
                        node.ToolTipText = string.Format ("{0} : {1}", db.GetDatetime (), db.GetComment ());
                    }
                }
                break;

            case CMD.DB_OPTIMIZATION:
                {
                }
                break;

            case CMD.DEL_SET_HISTORY:
                {
                    this.setHistoryDelList.Add (cmd.GetString ());
                }
                break;

            default:
                {
                    Debug.Assert (false);
                }
                break;
            }

        }

    }
}
