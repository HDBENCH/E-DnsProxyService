using DnsProxyLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DnsProxyLibrary
{
    public class DataBase
    {
        public delegate void ImportCallBack(string host);  

        static private readonly Guid data_guid = new Guid("3EAA715A-CC2B-403A-AA85-0A80D2CF8D70");
        static private readonly byte data_version = 0x00;

        private string name = "";
        private string comment = "";
        private DateTime datetime = DateTime.Now;
        private FLAGS flags = FLAGS.Reject;
        private DataBase parent = null;
        private ConcurrentDictionary<string, DataBase> dataBase = new ConcurrentDictionary<string, DataBase>();

        //[Flags]
        public enum FLAGS : byte
        {
            None,
            Accept,
            Reject,
            Ignore,
            Disable,


            Answer = 0x80,

            SetNone = 0x90,
            SetAccept,
            SetReject,
            SetIgnore,

        }

        public DataBase ()
        {
        }

        private static int CompareDatabase (KeyValuePair<string, DataBase> x, KeyValuePair<string, DataBase> y)
        {
            return x.Key.CompareTo (y.Key);
        }


        public void DUMP ()
        {
            foreach (var v in this.dataBase)
            {
                DUMP (v.Value, 0);
            }
        }
        static void DUMP (DataBase db, int index)
        {
            string space = "";

            space = space.PadLeft (index * 2, ' ');

            DBG.MSG ("{0}:{1}//{2}\n", db.flags.ToString().PadRight(7), (space + db.name).PadRight(20), db.comment);

            foreach (var v in db.dataBase)
            {
                DUMP (v.Value, index + 1);
            }
        }


        public void Clear ()
        {
            this.name = "";
            this.comment = "";
            this.flags = FLAGS.Reject;
            this.datetime = DateTime.Now;
            this.dataBase.Clear ();
        }

        public ConcurrentDictionary<string, DataBase> GetDataBase()
        {
            return this.dataBase;
        }
        public List<KeyValuePair<string, DataBase>> ToArray()
        {
            List<KeyValuePair<string, DataBase>> list = new List<KeyValuePair<string, DataBase>>();

            list = this.dataBase.ToList ();
            list.Sort (CompareDatabase);

            return list;
        }

        public DataBase GetParent ()
        {
            return this.parent;
        }

        public DataBase.FLAGS GetFlags ()
        {
            return this.flags;
        }
        public string GetName()
        {
            return this.name;
        }
        
        public string GetFullName()
        {
            string result = this.name;
            DataBase current = this.parent;

            while (current != null)
            {
                result = string.Format("{0}{1}{2}", result, string.IsNullOrEmpty(current.GetName())?"":".", current.GetName());

                current = current.parent;
            }

            return result;
        }
        
        public string GetComment()
        {
            return this.comment;
        }
        
        public DateTime GetDatetime()
        {
            return this.datetime;
        }
        public void UpdateDatetime()
        {
            DataBase db = this;
            DateTime timeNow = DateTime.Now;

            while (db != null)
            {
                db.datetime = timeNow;
                db = db.parent;
            }
        }
        
        public bool SetComment(string value)
        {
            bool result = this.comment != value;

            if (result)
            {
                this.comment = value; ;
            }

            return result;
        }


        public DataBase Find (string host, bool bCreate, ref bool bModifyed)
        {
            DataBase current = this;
            DataBase data;
            string[] hosts = host.Split('.');

            lock (this)
            {
            for (int i = 0; i < hosts.Length; i++)
            {
                string n = hosts[hosts.Length - i - 1];

                if (!current.dataBase.TryGetValue (n, out data))
                {
                    if (!bCreate)
                    {
                        current = null;
                        break;
                    }

                    data = new DataBase ();
                    data.parent = current;
                    data.flags = FLAGS.None;
                    data.name = n;
                    data.comment = "";
                        data.datetime = DateTime.Now;
                    current.dataBase.TryAdd (n, data);

                    bModifyed = true;
                }

                current = data;
            }
            }

            return current;
        }


        public DataBase SetFlags (string host, FLAGS flags, ref bool bModifyed)
        {
            DataBase db = Find (host, true, ref bModifyed);

            if (db.flags != flags)
            {
                db.flags = flags;
                bModifyed = true;
            }

            return db;
        }

        public FLAGS GetFlags (string host, bool bCreate, ref bool bModifyed)
        {
            DataBase db = Find (host, bCreate, ref bModifyed);

            return db.flags;
        }
        public bool Del (string host, ref bool bModifyed)
        {
            bool result = true;

            DataBase current = this;
            DataBase data;
            string[] hosts = host.Split('.');


            lock (this)
            {
            for (int i = 0; i < hosts.Length; i++)
            {
                string n = hosts[hosts.Length - i - 1];

                if (!current.dataBase.TryGetValue (n, out data))
                {
                    result = false;
                    break;
                }

                current = data;
            }


            if (result)
            {
                    DataBase p = current.parent;
                    if (p.dataBase.TryRemove (current.name, out data))
                    {
                        DBG.MSG("DataBase.Del - success.{0}\n", current.name);
                    }
                    else
                    {
                        DBG.MSG("DataBase.Del - failed.{0}\n", current.name);
                        Debug.Assert (false);
                    }

                    bModifyed = true;
#if false
                    
                current = current.parent;

                for (int i = 0; i < hosts.Length; i++)
                {
                        string n = hosts[i];

                    if (!current.dataBase.TryRemove (n, out data))
                    {
                        Debug.Assert(false);
                    }
                    
                    if (!current.dataBase.IsEmpty)
                    {
                        break;
                    }

                    current = current.parent;
                }

#endif
                }
            }


            return result;
        }

        public void ImportFolder(string directoryName, string hostName, ImportCallBack callback)
        {
            string filename1;
            string filename2;
            byte[] bytes = new byte[1];
            bool bModifyed = false;
            DirectoryInfo dirInfo = new DirectoryInfo(directoryName);
            if (!dirInfo.Exists)
                throw new InvalidOperationException ("Directory does not exist : " + directoryName);

            foreach (var subDirectory in Directory.GetDirectories (directoryName))
            {
                FLAGS flags = (hostName.Length > 0) ? FLAGS.None : FLAGS.Reject;


                string fullHostName = hostName;

                if (!string.IsNullOrEmpty (fullHostName))
                {
                    fullHostName = "." + fullHostName;
                }

                dirInfo = new DirectoryInfo(subDirectory);
                fullHostName = dirInfo.Name + fullHostName;

                if (callback != null)
                {
                    callback(fullHostName);
                }

                filename1 = subDirectory + "\\config.dat";
                filename2 = subDirectory + "\\config.ini";
                if (File.Exists(filename1))
                {
                    using (FileStream stream = new FileStream (filename1, FileMode.Open, FileAccess.Read))
                    {
                        if (stream.Read (bytes, 0, bytes.Length) == 1)
                        {
                            Debug.Assert(bytes[0] < 3);
                            flags = (FLAGS)bytes[0];
                        }
                    }
                }
                else if (File.Exists(filename2))
                {
                    using (FileStream stream = new FileStream (filename2, FileMode.Open, FileAccess.Read))
                    {
                        byte[] bytes2 = new byte[stream.Length];

                        stream.Read (bytes2, 0, bytes2.Length);

                        string str = Encoding.Default.GetString (bytes2, 0, bytes2.Length);
                        string rejection = "Rejection=";
                        int index = str.IndexOf (rejection);
                        if (index > 0)
                        {
                            index += rejection.Length;

                            str = str.Substring (index, 1);

                            if (str == "0")
                            {
                                flags = FLAGS.None;
                            }
                            else if (str == "1")
                            {
                                flags = FLAGS.Accept;
                            }
                            else if (str == "2")
                            {
                                flags = FLAGS.Reject;
                            }
                        }
                    }
                }

                SetFlags(fullHostName, flags, ref bModifyed);

                ImportFolder (subDirectory, fullHostName, callback);
            }
        }


        public void ImportFolder (string path, ImportCallBack callback = null)
        {
            Clear ();

            ImportFolder(path, "", callback);
        }

        public void Import (Stream stream, bool bClear = true)
        {
            byte[] guid_header = new byte[16];
            byte verion;
            byte[] reserved = new byte[15];

            if (bClear)
            {
                Clear ();
            }

            do
            {
                stream.Read (guid_header, 0, guid_header.Length);

                if (!DataBase.data_guid.ToByteArray ().SequenceEqual<byte> (guid_header))
                {
                    break;
                }

                verion = (byte)stream.ReadByte ();
                DBG.MSG ("DataBase.Import - verion={0}\n", verion);

                stream.Read (reserved, 0, reserved.Length);

                FromStream (stream, verion);
            }
            while (false);

#if DEBUG
            //DUMP();
#endif
        }

        public void Import (string path, bool bClear = true)
        {
            DBG.MSG ("DataBase.Import - {0}\n", path);
            lock (this)
            {
                if (bClear)
                {
                    Clear ();
                }

            do
            {
                if (!File.Exists (path))
                {
                    DBG.MSG ("DataBase.Import - path not found, {0}\n", path);
                    break;
                }

                using (FileStream stream = new FileStream (path, FileMode.Open, FileAccess.Read))
                {
                    Import(stream, bClear);
                }
            }
            while (false);
            }

#if DEBUG
            //DUMP();
#endif

        }

        private void FromStream (Stream stream, byte verion)
        {
            byte[] bytes;

            do
            {
                //member data
                if (!ReadStream (stream, out bytes))
                {
                    break;
                }

                using (MemoryStream ms = new MemoryStream (bytes))
                {
                    //name
                    if (ReadStream (ms, out bytes))
                    {
                this.name = Encoding.Default.GetString (bytes, 0, bytes.Length);
                    }

                //comment
                    if (ReadStream (ms, out bytes))
                {
                this.comment = Encoding.Default.GetString (bytes, 0, bytes.Length);
                    }

                //flags
                    this.flags = (FLAGS)ms.ReadByte ();

                    //datetime
                    if (ReadStream (ms, out bytes))
                    {
                        this.datetime = DateTime.FromBinary (BitConverter.ToInt64 (bytes, 0));
                    }
                }


                //DBG.MSG ("DataBase.FromStream - name={0}\n", this.name);
                //DBG.MSG ("DataBase.FromStream - flags={0}\n", this.flags);
                //DBG.MSG ("DataBase.FromStream - comment={0}\n", this.comment);

                //Dictionary
                while (ReadStream (stream, out bytes))
                {
                    if ((bytes == null) || (bytes.Length == 0))
                    {
                        break;
                    }

                    DataBase data = new DataBase ();
                    data.parent = this;

                    using (MemoryStream ms = new MemoryStream (bytes))
                    {
                        data.FromStream (ms, verion);
                    }

                    this.dataBase.TryAdd (data.name, data);
                }
            }
            while (false);
        }


        public byte[] Export ()
        {
            DBG.MSG ("DataBase.Export \n");
            byte[] guid_header = DataBase.data_guid.ToByteArray();
            byte[] reserved = new byte[15];
            byte[] result = null;

            using (MemoryStream stream = new MemoryStream ())
            {
                stream.SetLength (0);

                stream.Write (guid_header, 0, guid_header.Length);
                stream.WriteByte (DataBase.data_version);
                stream.Write (reserved, 0, reserved.Length);

                ToStream (stream);
           
                result = stream.ToArray ();
            }

            return result;
        }

        public void Export (string path)
        {
            DBG.MSG ("DataBase.Export - {0} \n", path);
            byte[] bytes = null;

            lock (this)
            {
            using (FileStream stream = new FileStream (path, FileMode.OpenOrCreate, FileAccess.Write))
            {
                bytes = Export();

                    stream.SetLength (0);
                stream.Write (bytes, 0, bytes.Length);
            }
        }
        }

        private void ToStream (Stream stream)
        {
            byte[] bytes;

            using (MemoryStream ms = new MemoryStream ())
            {
                WriteStream (ms, this.name);
                WriteStream (ms, this.comment);
                ms.WriteByte ((byte)this.flags);
                WriteStream (ms, this.datetime.ToBinary ());
                
                WriteStream (stream, ms.ToArray());
            }

            foreach (var v in this.dataBase)
            {
                using (MemoryStream ms = new MemoryStream (0))
                {
                    v.Value.ToStream (ms);

                    bytes = ms.ToArray ();
                    WriteStream (stream, bytes);
                }
            }

            stream.WriteByte (0);
        }


        static bool ReadStream (Stream stream, out byte[] bytes)
        {
            bool result = false;

            bytes = null;

            try
            {
                do
                {
                    int len = stream.ReadByte();
                    if (len == -1)
                    {
                        break;
                    }

                    int count = len >> 4;

                    len = (len & 0x0F);

                    for (int i = 0; i < count; i++)
                    {
                        len = (len << 8) | stream.ReadByte ();
                    }

                    bytes = new byte[len];
                    stream.Read (bytes, 0, len);

                    result = true;
                }
                while (false);
            }
            catch (Exception e)
            {
                DBG.MSG ("DataBase.ReadStream - Exception, {0}\n", e.Message);
                Debug.Assert(false);
            }

            return result;
        }
        static void WriteStream (Stream stream, string value)
        {
            byte[] bytes;

            if (string.IsNullOrEmpty (value))
            {
                stream.WriteByte (0);
            }
            else
            {
                bytes = Encoding.Default.GetBytes (value);
                WriteStream (stream, bytes);
            }
        }
        static void WriteStream (Stream stream, long value)
        {
            byte[] bytes;

            bytes = BitConverter.GetBytes(value);
            WriteStream (stream, bytes);
        }

        static void WriteStream (Stream stream, byte[] bytes)
        {
            ulong size;

            if (bytes.Length <= 0xF)
            {
                size = 0x00 | (uint)bytes.Length;
                stream.WriteByte ((byte)(size >> 0));
            }
            else if (bytes.Length <= 0xFFF)
            {
                size = 0x1000 | (uint)bytes.Length;
                stream.WriteByte ((byte)(size >> 8));
                stream.WriteByte ((byte)(size >> 0));
            }
            else if (bytes.Length <= 0xFFFFF)
            {
                size = 0x200000 | (uint)bytes.Length;
                stream.WriteByte ((byte)(size >> 16));
                stream.WriteByte ((byte)(size >> 8));
                stream.WriteByte ((byte)(size >> 0));
            }
            else if (bytes.Length <= 0xFFFFFFF)
            {
                size = 0x30000000 | (uint)bytes.Length;
                stream.WriteByte ((byte)(size >> 24));
                stream.WriteByte ((byte)(size >> 16));
                stream.WriteByte ((byte)(size >> 8));
                stream.WriteByte ((byte)(size >> 0));
            }
            else
            {
                size = 0x4000000000ul | (uint)bytes.Length;
                stream.WriteByte ((byte)(size >> 32));
                stream.WriteByte ((byte)(size >> 24));
                stream.WriteByte ((byte)(size >> 16));
                stream.WriteByte ((byte)(size >> 8));
                stream.WriteByte ((byte)(size >> 0));
            }
            stream.Write (bytes, 0, bytes.Length);
        }


        public void Optimization (ref bool bModifyed)
        {
            lock (this)
            {
                List<string> list = new List<string> ();

                foreach (var v in this.dataBase)
                {
                    if (!string.IsNullOrEmpty (v.Value.comment))
                    {   continue;
                    }

                    if (Optimization (v.Value, ref bModifyed))
                    {
                        list.Add(v.Key);
                    }
                }

                foreach (var v in list)
                {
                    this.dataBase.TryRemove(v, out DataBase dbTmp);
                    bModifyed = true;
                }
            }
        }
        public bool Optimization (DataBase db, ref bool bModifyed)
        {
            List<string> list = new List<string> ();

            foreach (var v in db.dataBase)
            {
                if (!string.IsNullOrEmpty (v.Value.comment))
                {   continue;
                }

                Optimization (v.Value, ref bModifyed);

                if (v.Value.flags != FLAGS.None)
                {
                    continue;
                }

                if (v.Value.dataBase.Count == 0)
                {
                    list.Add (v.Key);
                }
            }

            foreach (var v in list)
            {
                db.dataBase.TryRemove(v, out DataBase dbTmp);
                bModifyed = true;
            }

            return (db.dataBase.Count == 0);
        }


    }
}
