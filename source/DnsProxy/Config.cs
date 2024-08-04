using DnsProxyLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DnsProxyLibrary
{
    public class Config
    {
        public class Name
        {
            public static readonly string base_path = "base_path";
            public static readonly string dns_server = "dns_server";
        }

        ConcurrentDictionary<string, string> config = new ConcurrentDictionary<string, string>();
        bool bModifyed = false;

        public virtual void Initialize ()
        {
            this.config.Clear ();

            Set (Name.dns_server, "8.8.8.8");
            Set (Name.base_path, "");

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
                            while (true)
                            {
                                string s =reader.ReadLine();
                                if (s == null)
                                {
                                    break;
                                }

                                string[] ss = s.Split ('=');
                                if (ss.Length != 2)
                                {
                                    continue;
                                }

                                Set (ss[0], ss[1]);
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

        public virtual void Save (string path)
        {
            if (!bModifyed)
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

                        foreach (var v in this.config)
                        {
                            writer.WriteLine (string.Format ("{0}={1}", v.Key, v.Value));
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
            return config.ContainsKey (key);
        }

        public bool Set (string key, string value)
        {
            if (this.config.ContainsKey (key))
            {
                this.config.TryRemove (key, out string v);
            }

            this.bModifyed = true;
            return this.config.TryAdd (key, value);
        }

        public bool Get (string key, out string value)
        {
            this.bModifyed = true;
            return this.config.TryGetValue (key, out value);
        }

        public bool Del (string key, out string value)
        {
            this.bModifyed = true;
            return this.config.TryRemove (key, out value);
        }
    }
}
