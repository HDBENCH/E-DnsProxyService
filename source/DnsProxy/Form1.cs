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
using System.CodeDom;

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


        public Form1 ()
        {
            InitializeComponent ();

            //string path = "D:\\_TOOL\\CDnsProxyServer\\Hosts\\";
            //string path = System.AppDomain.CurrentDomain.BaseDirectory;

            //dataBase.Import(path + "database");
            //dataBase.Export (path + "database");

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
            this.treeView1.ImageList = listViewImageList;


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
                                                listView1,
                                                new object[] { true });


            //Timer
            this.timer1Sec.Tick += new EventHandler(TimerEventProcessor);
            this.timer1Sec.Interval = 500;
            this.timer1Sec.Start ();


            //listViewItemList
            contextMenu_ViewAll(null, null);

            //Menu
            ProxyEnable(false);
            ViewScroll(true);
            MenuEnable(false);

            this.dnsProxyClient.Start (PipeConnect, PipeReceive, this);
        }

        private void Form1_FormClosed (object sender, FormClosedEventArgs e)
        {
            this.bIsClosing = true;
            dnsProxyClient.Stop();

        }


        private void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            string[] array = null;
            string[] array2 = null;

            // 1回仮想モードに表示されるデータ数を0にすることで、別のデータを入れたときに全て更新されるようになる
            lock (this.listViewItemListLock)
            {
                if (historyAddList.Count > 0)
                {
                    array = new string[historyAddList.Count];
                    historyAddList.CopyTo (array);
                    historyAddList.Clear ();
                }
                if (setHistoryAddList.Count > 0)
                {
                    array2 = new string[setHistoryAddList.Count];
                    setHistoryAddList.CopyTo (array2);
                    setHistoryAddList.Clear ();
                }
            }

            if (array != null)
            {
                for (int i = 0; i < array.Count (); i++)
                {
                    HistoryData data = new HistoryData();
                    data.FromString (array[i]);

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

                if (array2 != null)
                {
                    for (int i = 0; i < array2.Count (); i++)
                    {
                        HistoryData data = new HistoryData();
                        data.FromString (array2[i]);

                        ListViewItem item = new ListViewItem(data.ToArray());
                        item.Tag = data;

                        lock (this.listViewItemListLock)
                        {
                            this.listViewItemSetList.Add (item);
                        }
                    }
                }

                int max_count = 2000;
                if (listViewItemAllList.Count > max_count)
                {
                    listViewItemAllList.RemoveRange (0, listViewItemAllList.Count - max_count);
                }
                if (listViewItemAcceptList.Count > max_count)
                {
                    listViewItemAcceptList.RemoveRange (0, listViewItemAcceptList.Count - max_count);
                }
                if (listViewItemRejectList.Count > max_count)
                {
                    listViewItemRejectList.RemoveRange (0, listViewItemRejectList.Count - max_count);
                }
                if (listViewItemIgnoreList.Count > max_count)
                {
                    listViewItemIgnoreList.RemoveRange (0, listViewItemIgnoreList.Count - max_count);
                }
                if (listViewItemAnswerList.Count > max_count)
                {
                    listViewItemAnswerList.RemoveRange (0, listViewItemAnswerList.Count - max_count);
                }
                if (listViewItemSetList.Count > max_count)
                {
                    listViewItemSetList.RemoveRange (0, listViewItemSetList.Count - max_count);
                }


                UpdateListVew ();
            }
        }

        void UpdateListVew ()
        {
            this.listView1.VirtualListSize = 0;
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
                e.Item = this.listViewItemList[e.ItemIndex];
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
            list.Sort (CompareDatabase);

            foreach (var v in list)
            {
                nodes.Add(TreeInitialize(treeView1.TopNode, v.Value));
            }

            this.treeView1.Nodes.AddRange(nodes.ToArray());
            this.treeView1.TopNode?.Expand ();
        }

        private void TreeAdd (string host)
        {
            string[] hosts;
            DataBase db = this.dnsProxyClient.GetDataBase();
            TreeNode node = null;
            int index;

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
                    host = string.Format("{0}{1}{2}", host, string.IsNullOrEmpty(host)?"":".", h);

                    if (!this.hostNameToNodeDic.TryGetValue (host, out TreeNode n))
                    {
                        index = 0;

                        for (int j = 0; j < node.Nodes.Count; j++)
                        {
                            DBG.MSG("j={0}, {1}, {2}, {3}\n", j, node.Nodes[j].Text,  h, node.Nodes[j].Text.CompareTo (h));

                            if (node.Nodes[j].Text.CompareTo (h) > 0)
                            {
                                index = j;
                                break;
                            }
                        }

                        db = this.dnsProxyClient.FindDataBase (host);
                        if (db == null)
                        {
                            bool bModifyed = false;
                            db = this.dnsProxyClient.GetDataBase().SetFlags(host, DataBase.FLAGS.None, ref bModifyed);
                        }

                        n = new TreeNode(h);

                        this.hostNameToNodeDic.TryAdd(db.GetFullName(), n);
                        this.nodeToHostNameDic.TryAdd(n, db.GetFullName());

                        node.Nodes.Insert(index, n);
            
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

                TreeNode node = HostNameToNode (historyData.host);
                if (node == null)
                {
                    break;
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

                dnsProxyClient.DataBaseSet(flags, host);
            }
            while (false);
        }

        void contextMenu_Exit (object sender, EventArgs e)
        {
            Close ();
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
                dnsProxyClient.DataBaseSetComment(db.GetFullName(), form.comment);
            }

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
            dnsProxyClient.DnsCacheClear ();
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
        void contextMenu_ViewAll(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemAllList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Checked;
            acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            setToolStripMenuItem.CheckState = CheckState.Unchecked;

            toolStripStatusView.Text = "All View";
        }

        void contextMenu_ViewAccept(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemAcceptList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Unchecked;
            acceptToolStripMenuItem.CheckState = CheckState.Checked;
            rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            setToolStripMenuItem.CheckState = CheckState.Unchecked;

            toolStripStatusView.Text = "Accept View";
        }
        void contextMenu_ViewReject(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemRejectList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Unchecked;
            acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            rejectToolStripMenuItem.CheckState = CheckState.Checked;
            ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            setToolStripMenuItem.CheckState = CheckState.Unchecked;

            toolStripStatusView.Text = "Reject View";
        }
        void contextMenu_ViewIgnore(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemIgnoreList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Unchecked;
            acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            ignoreToolStripMenuItem.CheckState = CheckState.Checked;
            answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            setToolStripMenuItem.CheckState = CheckState.Unchecked;

            toolStripStatusView.Text = "Ignore View";
        }
        void contextMenu_ViewAnswer(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemAnswerList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Unchecked;
            acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            answerToolStripMenuItem.CheckState = CheckState.Checked;
            setToolStripMenuItem.CheckState = CheckState.Unchecked;

            toolStripStatusView.Text = "Answer View";
        }
        void contextMenu_ViewSet(object sender, EventArgs e)
        {
            this.listViewItemList = this.listViewItemSetList;
            UpdateListVew ();

            allToolStripMenuItem.CheckState = CheckState.Unchecked;
            acceptToolStripMenuItem.CheckState = CheckState.Unchecked;
            rejectToolStripMenuItem.CheckState = CheckState.Unchecked;
            ignoreToolStripMenuItem.CheckState = CheckState.Unchecked;
            answerToolStripMenuItem.CheckState = CheckState.Unchecked;
            setToolStripMenuItem.CheckState = CheckState.Checked;

            toolStripStatusView.Text = "Set View";
        }

        void contextMenu_ViewScroll (object sender, EventArgs e)
        {
            ViewScroll(!this.bViewScroll);
        }
        void ViewScroll (bool bEnable)
        {
            this.bViewScroll = bEnable;

            scrollToolStripMenuItem.Checked = this.bViewScroll;
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
            this.dnsProxyClient.ProxyEnable(!proxyEnableToolStripMenuItem.Checked);
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

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(proxyEnableToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (toolStripStatusEnabled.Bounds.X + e.X, toolStripStatusEnabled.Bounds.Y + e.Y));
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

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(allToolStripMenuItem), MenuCopy(acceptToolStripMenuItem), MenuCopy(rejectToolStripMenuItem), MenuCopy(ignoreToolStripMenuItem), MenuCopy(answerToolStripMenuItem), MenuCopy(setToolStripMenuItem) });
                Menu.Items.Add (new ToolStripSeparator ());
                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(logClearToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (toolStripStatusView.Bounds.X + e.X, toolStripStatusView.Bounds.Y + e.Y));
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

                Menu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(scrollToolStripMenuItem) });

                Menu.Show (this.statusStrip1, new System.Drawing.Point (toolStripStatusScroll.Bounds.X + e.X, toolStripStatusScroll.Bounds.Y + e.Y));
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

                if (index == null)
                {
                    break;
                }

                if (index.Count != 1)
                {
                    break;
                }

                ListViewItem item;
                HistoryData historyData;

                lock (this.listViewItemListLock)
                {
                    item = this.listViewItemList[index[0]];
                }

                historyData = item.Tag as HistoryData;
                DataBase db = dnsProxyClient.FindDataBase(historyData.host);

                ShowContextMenu(db, this.listView1, e.X, e.Y);
            }
            while (false);

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

            while (db != null)
            {
                //ContextMenu
                noneMenu = new ToolStripMenuItem ("&None");
                acceptMenu = new ToolStripMenuItem ("&Accept");
                rejectMenu = new ToolStripMenuItem ("&Reject");
                ignoreMenu = new ToolStripMenuItem ("&Ignore");
                copyMenu = new ToolStripMenuItem ("&Copy");
                commentMenu = new ToolStripMenuItem ("C&omment");

                noneMenu.Tag = db;
                acceptMenu.Tag = db;
                rejectMenu.Tag = db;
                ignoreMenu.Tag = db;
                copyMenu.Tag = db.GetFullName();
                commentMenu.Tag = db;

                noneMenu.Click += contextMenu_None;
                acceptMenu.Click += contextMenu_Accept;
                rejectMenu.Click += contextMenu_Reject;
                ignoreMenu.Click += contextMenu_Ignore;
                copyMenu.Click += contextMenu_Copy;
                commentMenu.Click += contextMenu_Comment;

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
                menu.DropDownItems.AddRange (new ToolStripMenuItem[] { commentMenu });
                menu.DropDownItems.Add (new ToolStripSeparator ());
                menu.DropDownItems.AddRange (new ToolStripMenuItem[] { copyMenu });

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

            rootMenu.Items.AddRange (new ToolStripMenuItem[] { MenuCopy(logClearToolStripMenuItem), MenuCopy(cacheClearToolStripMenuItem) });

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
                dnsProxyClient.Clear();
                Invoke (new Action<bool> (MenuEnable), true);
            }
            else
            {
                if (!this.bIsClosing)
                {
                    dnsProxyClient.Clear ();
                    Invoke (new Action<object, EventArgs> (contextMenu_LogClear), new object[] { null, null });
                    Invoke (new Action (TreeInitialize));
                    Invoke (new Action<bool> (MenuEnable), false);
                }
            }

        }


        void PipeReceive (Command cmd, object param)
        {
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
                    string host = cmd.GetString();

                    DBG.MSG("Form1.PipeReceive - {0}, {1}\n", cmd.GetCMD(), cmd.GetString());

                    TreeNode node = HostNameToNode(host);
                    if (node != null)
                    {
                        node.Parent.Nodes.Remove(node);

                        List<string> HostList = new List<string>();
                        List<TreeNode> nodeList = new List<TreeNode>();

                        foreach (var v in this.hostNameToNodeDic)
                        {
                            if (v.Key.IndexOf (host) == 0)
                            {
                                DBG.MSG("Form1.PipeReceive - {0}, DEL({1})\n", cmd.GetCMD(), host);

                                HostList.Add (v.Key);
                                nodeList.Add (v.Value);
                            }
                        }
                        foreach (var v in HostList)
                        {
                            this.hostNameToNodeDic.TryRemove(v, out TreeNode t);
                        }
                        foreach (var v in nodeList)
                        {
                            this.nodeToHostNameDic.TryRemove(v, out string h);
                        }
                    }
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
                        historyAddList.Add (cmd.GetString ());
                    }
                }
                break;

            case CMD.SET_HISTORY:
                {
                    lock (this.listViewItemListLock)
                    {
                        setHistoryAddList.Add (cmd.GetString ());
                    }
                }
                break;


            case CMD.DNS_CLEAR:
                {
                }
                break;

            case CMD.COMMENT:
                {
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
