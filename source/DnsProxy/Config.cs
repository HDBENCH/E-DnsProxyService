using DnsProxyLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Win32;
using System.ComponentModel;

namespace DnsProxyLibrary
{
    public class Config
    {
        public class Name
        {
            public static readonly string server_dns_server         ="server_dns_server";
            public static readonly string server_base_path          ="server_base_path";

            public static readonly string admin_FormBounds          ="admin_FormBounds";
            public static readonly string admin_SplitterDistance    ="admin_SplitterDistance";
            public static readonly string admin_columnTime          ="admin_columnTime";
            public static readonly string admin_columnType          ="admin_columnType";
            public static readonly string admin_columnIp            ="admin_columnIp";
            public static readonly string admin_columnHost          ="admin_columnHost";
            public static readonly string admin_columnInfo          ="admin_columnInfo";
            public static readonly string admin_columnComment       ="admin_columnComment";
            public static readonly string admin_ViewScroll          ="admin_ViewScroll";
            public static readonly string admin_ViewMode            ="admin_ViewMode";

        }

        private Dictionary<string, string> config = new Dictionary<string, string>();
        private List<string> list = new List<string>();
        private bool bModifyed = false;

        public virtual void Initialize ()
        {
            this.config.Clear ();
            this.list.Clear ();

            this.bModifyed = false;
        }

        public virtual void Load (string path)
        {
            Initialize ();

            try
            {
                if (File.Exists (path))
                {
                    using (FileStream fs = new FileStream (path, FileMode.Open, FileAccess.Read))
                    {
                        using (StreamReader reader = new StreamReader (fs))
                        {
                            lock (this.config)
                            {
                                string s;

                                while ((s =reader.ReadLine()) != null)
                                {
                                    s = s.TrimStart ();

                                    if (s.IndexOf (';') == 0)
                                    {
                                        this.list.Add (s);
                                        continue;
                                    }

                                    string[] ss = s.Split ('=');
                                    if (ss.Length != 2)
                                    {
                                        continue;
                                    }

                                    this.config.Add (ss[0], ss[1]);
                                    this.list.Add (ss[0]);
                                    
                                    DBG.MSG ("Config.Load - Add, {0}: {1}\n", ss[0], ss[1]);

                                }
                            }
                        }

                    }
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("Config.Load - Exception({0})\n", e.Message);
                Debug.Assert (false);
            }
        }

        public virtual void Save (string path, bool bForce = false)
        {
            if (!bModifyed && !bForce)
            {
                return;
            }

            bModifyed = false;

            try
            {
                using (FileStream fs = new FileStream (path, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    fs.SetLength (0);

                    using (StreamWriter writer = new StreamWriter (fs))
                    {
                        lock (this.config)
                        {
                            foreach (var v in this.list)
                            {
                                if (v.IndexOf (';') == 0)
                                {
                                    writer.WriteLine (v);
                                    continue;
                                }

                                if (this.config.TryGetValue (v, out string s))
                                {
                                    writer.WriteLine (string.Format ("{0}={1}", v, s));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("Config.Save - Exception({0})\n", e.Message);
                Debug.Assert (false);
            }
        }

        public bool ContainsKey (string key)
        {
            bool result;
            lock (config)
            {
                result = config.ContainsKey (key);
            }
            return result;
        }

        public void SetValue (string key, object value)
        {
            TypeConverter typeConverter = TypeDescriptor.GetConverter(value.GetType());

            lock (this.config)
            {
                if (this.config.ContainsKey (key))
                {
                    this.config.Remove (key);
                }
                else
                {
                    this.list.Add (key);
                }

                if (typeConverter.CanConvertTo (typeof(string)))
                {
                    this.config.Add (key, typeConverter.ConvertToString (value));
                }
                else
                {
                    this.config.Add (key, value.ToString());
                    Debug.Assert (false);
                }

                this.bModifyed = true;
            }
        }

        public object GetValue (string key, object def_value)
        {
            TypeConverter typeConverter = TypeDescriptor.GetConverter(def_value.GetType());
            object result = null;
            bool r;
            string value;


            try
            {
                lock (this.config)
                {
                    if (r = this.config.TryGetValue (key, out value))
                    {
                        result = typeConverter.ConvertFromString (value);
                    }
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("Config.GetValue - Exception({0})\n", e.Message);
                Debug.Assert (false);
                r = false;
            }

            if (!r)
            {
                SetValue (key, def_value);
                result = def_value;
            }

            return result;
        }

        public void DelValue (string key)
        {
            lock (this.config)
            {
                if (this.config.ContainsKey (key))
                {
                    this.config.Remove (key);
                    //this.list.Remove (key);

                    this.bModifyed = true;
                }
            }
        }

    }
}
