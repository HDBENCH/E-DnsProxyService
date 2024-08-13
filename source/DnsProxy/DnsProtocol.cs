using DnsProxyLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static DnsProxyLibrary.DnsProtocol;
using static System.Net.WebRequestMethods;

namespace DnsProxyLibrary
{
    public class DnsProtocol
    {
        public enum RRClass : ushort
        {
            None = 0,
            IN = 1,
            CS = 2,
            CH = 3,
            HS = 4
        }
        public enum RRType : ushort
        {
            A = 0x0001,
            NS = 0x0002,
            MD = 0x0003,
            MF = 0x0004,
            CNAME = 0x0005,
            SOA = 0x0006,
            MB = 0x0007,
            MG = 0x0008,
            MR = 0x0009,
            NULL = 0x000a,
            WKS = 0x000b,
            PTR = 0x000c,
            HINFO = 0x000d,
            MINFO = 0x000e,
            MX = 0x000f,
            TEXT = 0x0010,
            RP = 0x0011,
            AFSDB = 0x0012,
            X25 = 0x0013,
            ISDN = 0x0014,
            RT = 0x0015,
            NSAP = 0x0016,
            NSAPPTR = 0x0017,
            SIG = 0x0018,
            KEY = 0x0019,
            PX = 0x001a,
            GPOS = 0x001b,
            AAAA = 0x001c,
            LOC = 0x001d,
            NXT = 0x001e,
            EID = 0x001f,
            NIMLOC = 0x0020,
            SRV = 0x0021,
            ATMA = 0x0022,
            NAPTR = 0x0023,
            KX = 0x0024,
            CERT = 0x0025,
            A6 = 0x0026,
            DNAME = 0x0027,
            SINK = 0x0028,
            OPT = 0x0029,
            DS = 0x002B,
            RRSIG = 0x002E,
            NSEC = 0x002F,
            DNSKEY = 0x0030,
            DHCID = 0x0031,
            HTTPS = 0x0041,
            UINFO = 0x0064,
            UID = 0x0065,
            GID = 0x0066,
            UNSPEC = 0x0067,
            ADDRS = 0x00f8,
            TKEY = 0x00f9,
            TSIG = 0x00fa,
            IXFR = 0x00fb,
            AXFR = 0x00fc,
            MAILB = 0x00fd,
            MAILA = 0x00fe,
            ALL = 0x00ff,
            ANY = 0x00ff,
            WINS = 0xff01,
            WINSR = 0xff02,
            NBSTAT = WINSR
        }



        public class Header
        {
            public ushort TransactionID;
            public ushort Flags;
            public ushort questions;
            public ushort answers;
            public ushort authoritys;
            public ushort additionals;

            public int Parse (byte[] bytes, int offset)
            {
                if (bytes.Length < (offset + 12))
                {
                    throw new InvalidDataException (string.Format ("DnsProtocol.Header.Parse - invalid length.({0})", bytes.Length));
                }

                this.TransactionID = DnsProtocol.ToUInt16 (bytes, 0);
                this.Flags = DnsProtocol.ToUInt16 (bytes, 2);
                this.questions = DnsProtocol.ToUInt16 (bytes, 4);
                this.answers = DnsProtocol.ToUInt16 (bytes, 6);
                this.authoritys = DnsProtocol.ToUInt16 (bytes, 8);
                this.additionals = DnsProtocol.ToUInt16 (bytes, 10);

                DBG.MSG ("DnsProtocol.Question.Parse - TransactionID = 0x{0:X2}\n", this.TransactionID);
                DBG.MSG ("DnsProtocol.Question.Parse - Flags         = 0x{0:X2}\n", this.Flags);
                DBG.MSG ("DnsProtocol.Question.Parse - Questions     = {0}\n", this.questions);
                DBG.MSG ("DnsProtocol.Question.Parse - Answers       = {0}\n", this.answers);
                DBG.MSG ("DnsProtocol.Question.Parse - Authoritys    = {0}\n", this.authoritys);
                DBG.MSG ("DnsProtocol.Question.Parse - Additionals   = {0}\n", this.additionals);

                return (offset + 12);
            }

            public void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.TransactionID, stream);
                DnsProtocol.ToStream (this.Flags, stream);
                DnsProtocol.ToStream (this.questions, stream);
                DnsProtocol.ToStream (this.answers, stream);
                DnsProtocol.ToStream (this.authoritys, stream);
                DnsProtocol.ToStream (this.additionals, stream);
            }

            public bool IsQuery ()
            {
                return ((this.Flags & 0x80) == 0x00);
            }
            public bool IsResponse()
            {
                return ((this.Flags & 0x80) == 0x80);
            }

            public ushort GetOpecode ()
            {
                return (ushort)((this.Flags & 0x7800) >> 11);
            }
            public void SetOpecode (ushort value)
            {
                this.Flags = (ushort) ((this.Flags & ~0x7800) | (value << 11));
            }

            public void SetQuery ()
            {
                this.Flags = (ushort)(this.Flags & ~0x0080);
            }

            public void SetResponse()
            {
                this.Flags |= 0x80;
            }



    //.... ..0. .... .... = Truncated: Message is not truncated
    //.... ...1 .... .... = Recursion desired: Do query recursively
    //.... .... .0.. .... = Z: reserved (0)
    //.... .... ...0 .... = Non-authenticated data: Unacceptable


        }

        public class Question
        {
            public RRClass Class;
            public string Name;
            public RRType Type;

            public int Parse (byte[] bytes, int offset)
            {
                if (bytes.Length < (offset + 4))
                {
                    throw new InvalidDataException (string.Format ("DnsProtocol.Question.Parse - invalid length.({0})", bytes.Length));
                }

                offset = DnsProtocol.ParseString (bytes, offset, ref this.Name);

                this.Type = (RRType)(DnsProtocol.ToUInt16 (bytes, offset));
                offset += 2;

                this.Class = (RRClass)(DnsProtocol.ToUInt16 (bytes, offset));
                offset += 2;

                DBG.MSG ("DnsProtocol.Question.Parse - Name = {0}\n", this.Name);
                DBG.MSG ("DnsProtocol.Question.Parse - Type = {0}\n", this.Type);
                DBG.MSG ("DnsProtocol.Question.Parse - Class= {0}\n", this.Class);

                return offset;
            }
            public void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.Name, stream);
                DnsProtocol.ToStream ((ushort)this.Type, stream);
                DnsProtocol.ToStream ((ushort)this.Class, stream);
            }

            public void Setup (string host, RRClass rrClass = RRClass.IN, RRType type = RRType.A)
            {
                this.Class = rrClass;
                this.Type = type;
                this.Name = host;
            }
        }

        public abstract class typeBase
        {
            //public abstract void Dump();
            public abstract int Parse (byte[] bytes, int offset, int DataLength);
            public abstract void ToStream (Stream stream);
            public abstract string ToDetail();
        }

        public class typeA : typeBase
        {
            public IPAddress AName;
            public override int Parse (byte[] bytes, int offset, int DataLength)
            {
                uint addressBytes = BitConverter.ToUInt32(bytes, offset);
                this.AName = new IPAddress (addressBytes);

                offset += sizeof (uint);

                DBG.MSG ("DnsProtocol.ANAME.Parse - AName   = {0}\n", this.AName);

                return offset;
            }
            public override void ToStream (Stream stream)
            {
                byte[] bytes = this.AName.GetAddressBytes();
                DnsProtocol.ToStream (bytes, stream);
            }
            public override string ToDetail()
            {
                return string.Format("{0}", this.AName.ToString());
            }
            public void Setup (IPAddress aName)
            {
                this.AName = aName;
            }
        }

        public class typeCNAME : typeBase
        {
            public string CName;
            public override int Parse (byte[] bytes, int offset, int DataLength)
            {
                offset = DnsProtocol.ParseString (bytes, offset, ref this.CName);
                DBG.MSG ("DnsProtocol.CNAME.Parse - CName   = {0}\n", this.CName);

                return offset;
            }
            public override void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.CName, stream);
            }
            public override string ToDetail()
            {
                return string.Format("{0}", this.CName.ToString());
            }
            public void Setup (string cName)
            {
                this.CName = cName;
            }
        }
        public class typeSOA : typeBase
        {
            public string MName;
            public string RName;
            public uint Serial;
            public uint Refresh;
            public uint Retry;
            public uint Expire;
            public uint Minimum;

            public override int Parse (byte[] bytes, int offset, int DataLength)
            {
                offset = DnsProtocol.ParseString (bytes, offset, ref this.MName);
                offset = DnsProtocol.ParseString (bytes, offset, ref this.RName);
                this.Serial = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);
                this.Refresh = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);
                this.Retry = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);
                this.Expire = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);
                this.Minimum = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);

                DBG.MSG ("DnsProtocol.SOA.Parse - MName   = {0}\n", this.MName);
                DBG.MSG ("DnsProtocol.SOA.Parse - RName   = {0}\n", this.RName);
                DBG.MSG ("DnsProtocol.SOA.Parse - Serial  = {0}\n", this.Serial);
                DBG.MSG ("DnsProtocol.SOA.Parse - Refresh = {0}\n", this.Refresh);
                DBG.MSG ("DnsProtocol.SOA.Parse - Retry   = {0}\n", this.Retry);
                DBG.MSG ("DnsProtocol.SOA.Parse - Expire  = {0}\n", this.Expire);
                DBG.MSG ("DnsProtocol.SOA.Parse - Minimum = {0}\n", this.Minimum);

                return offset;
            }
            public override void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.MName, stream);
                DnsProtocol.ToStream (this.RName, stream);
                DnsProtocol.ToStream (this.Serial, stream);
                DnsProtocol.ToStream (this.Refresh, stream);
                DnsProtocol.ToStream (this.Retry, stream);
                DnsProtocol.ToStream (this.Expire, stream);
                DnsProtocol.ToStream (this.Minimum, stream);
            }
            public override string ToDetail()
            {
                return string.Format("{0}, {1}, {2}", this.MName.ToString(), this.RName.ToString(), this.Serial.ToString());
            }
            public void Setup (string mName, string rName, uint serial, uint refresh, uint retry, uint expire, uint minimum)
            {
                this.MName      = mName;
                this.RName      = rName;
                this.Serial     = serial;
                this.Refresh    = refresh;
                this.Retry      = retry;
                this.Expire     = expire;
                this.Minimum    = minimum;
            }

        }
        public class typeHTTPS : typeBase
        {
            public ushort SvcPriority;
            public string TName;
            public List<SvcParam> SvcParams = new List<SvcParam>();

            public override int Parse (byte[] bytes, int offset, int DataLength)
            {
                this.SvcPriority = DnsProtocol.ToUInt16 (bytes, offset);
                offset += sizeof (ushort);
                offset = DnsProtocol.ParseString (bytes, offset, ref this.TName);

                DBG.MSG ("DnsProtocol.RRecord.Parse - SvcPrio = {0}\n", this.SvcPriority);
                DBG.MSG ("DnsProtocol.RRecord.Parse - TName   = {0}\n", this.TName);

                int len = DataLength - sizeof (ushort) - this.TName.Length - 1;

                while (len > 0)
                {
                    SvcParam p = new SvcParam();

                    offset = p.Parse (bytes, offset);
                    this.SvcParams.Add (p);

                    len -= (p.Len + sizeof (ushort) + sizeof (ushort));

                    DBG.MSG ("DnsProtocol.HTTPS.Parse - SvcParam\n");
                    DBG.MSG ("DnsProtocol.HTTPS.Parse -   Key   = {0}\n", p.Key);
                    DBG.MSG ("DnsProtocol.HTTPS.Parse -   Len   = {0}\n", p.Len);
                }

                return offset;
            }
            public override void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.SvcPriority, stream);
                DnsProtocol.ToStream (this.TName, stream);

                foreach (var v in this.SvcParams)
                {
                    DnsProtocol.ToStream (v, stream);
                }
            }
            public override string ToDetail()
            {
                return string.Format("{0}, {1}", this.TName.ToString(), this.SvcPriority.ToString());
            }
            public void Setup (ushort svcPriority, string tName, List<SvcParam> svcParams)
            {
                this.SvcPriority    = svcPriority;
                this.TName          = tName;
                this.SvcParams      = new List<SvcParam>(svcParams);
            }

        }
        public class typeOther : typeBase
        {
            byte[] Data;

            public override int Parse (byte[] bytes, int offset, int DataLength)
            {
                Data = new  byte[DataLength];
                Buffer.BlockCopy(bytes, offset, this.Data, 0, DataLength);
                offset += DataLength;

                return offset;
            }
            public override void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.Data, stream);
            }
            public override string ToDetail()
            {
                return string.Format("");
            }
            public void Setup (byte[] data)
            {
                Data = new byte [data.Length];
                Buffer.BlockCopy (data, 0, Data, 0, data.Length);
            }
        }

        public class SvcParam
        {
            public ushort Key;
            public ushort Len;
            public byte[] Data;

            public int Parse (byte[] bytes, int offset)
            {
                this.Key = DnsProtocol.ToUInt16 (bytes, offset);
                offset += sizeof (ushort);
                this.Len = DnsProtocol.ToUInt16 (bytes, offset);
                offset += sizeof (ushort);

                this.Data = new byte[this.Len];
                Buffer.BlockCopy (bytes, offset, this.Data, 0, this.Len);
                offset += this.Len;

                return offset;
            }
            public void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.Key, stream);
                DnsProtocol.ToStream (this.Len, stream);
                DnsProtocol.ToStream (this.Data, stream);
            }
            public void Setup (ushort Key, ushort len, byte[] data)
            {
                this.Key = Key;
                this.Len = len;
                this.Data = new byte [data.Length];
                Buffer.BlockCopy (data, 0, this.Data, 0, data.Length);
            }

        }

        public class RRecord
        {
            public RRClass Class;
            public string Name;
            public RRType Type;
            public uint Ttl;
            public ushort DataLength;
            public typeBase typeData;

            public int Parse (byte[] bytes, int offset)
            {
                if (bytes.Length < (offset + 4))
                {
                    throw new InvalidDataException (string.Format ("DnsProtocol.Question.Parse - invalid length.({0})", bytes.Length));
                }

                offset = DnsProtocol.ParseString (bytes, offset, ref this.Name);

                this.Type = (RRType)(DnsProtocol.ToUInt16 (bytes, offset));
                offset += 2;

                this.Class = (RRClass)(DnsProtocol.ToUInt16 (bytes, offset));
                offset += 2;

                this.Ttl = DnsProtocol.ToUInt32 (bytes, offset);
                offset += sizeof (uint);

                this.DataLength = DnsProtocol.ToUInt16 (bytes, offset);
                offset += sizeof (ushort);

                DBG.MSG ("DnsProtocol.RRecord.Parse - Name    = {0}\n", this.Name);
                DBG.MSG ("DnsProtocol.RRecord.Parse - Type    = {0}\n", this.Type);
                DBG.MSG ("DnsProtocol.RRecord.Parse - Class   = {0}\n", this.Class);
                DBG.MSG ("DnsProtocol.RRecord.Parse - Ttl     = {0}\n", this.Ttl);
                DBG.MSG ("DnsProtocol.RRecord.Parse - Length  = {0}\n", this.DataLength);

                if ((Class == RRClass.IN) && (Type == RRType.A))
                {
                    this.typeData = new typeA ();
                }
                else if (Type == RRType.CNAME)
                {
                    this.typeData = new typeCNAME ();
                }
                else if (Type == RRType.SOA)
                {
                    this.typeData = new typeSOA ();
                }
                else if (Type == RRType.HTTPS)
                {
                    this.typeData = new typeHTTPS ();
                }
                else
                {
                    this.typeData = new typeOther ();
                }

                offset = this.typeData.Parse (bytes, offset, this.DataLength);

                return offset;
            }
            public void ToStream (Stream stream)
            {
                DnsProtocol.ToStream (this.Name, stream);
                DnsProtocol.ToStream ((ushort)this.Type, stream);
                DnsProtocol.ToStream ((ushort)this.Class, stream);
                DnsProtocol.ToStream (this.Ttl, stream);
                DnsProtocol.ToStream (this.DataLength, stream);

                this.typeData.ToStream (stream);
            }
            public void Setup (RRClass rrClass, string name, RRType type, uint ttl, ushort dataLength, typeBase tData)
            {
                this.Class      = rrClass;
                this.Name       = name;
                this.Type       = type;
                this.Ttl        = ttl;
                this.DataLength = dataLength;
                this.typeData   = tData;
            }

        }

        private static int ParseString (byte[] bytes, int offset, ref string value)
        {
            StringBuilder Sb = new StringBuilder();
            offset = ParseString (bytes, offset, ref Sb);
            value = Sb.ToString ();

            return offset;
        }

        private static int ParseString (byte[] bytes, int offset, ref StringBuilder value)
        {
            while (true)
            {
                int Length = bytes[offset];
                offset++;

                if (Length == 0x00)
                {
                    break;
                }

                if ((Length & 0xC0) == 0xC0)
                {
                    int new_offset = bytes[offset] | ((Length & 0x3F) << 8);
                    offset++;

                    ParseString (bytes, new_offset, ref value);
                    break;
                }

                if (value.Length != 0)
                {
                    value.Append (".");
                }
                string s = Encoding.Default.GetString (bytes, offset, Length);
                value.Append (Encoding.Default.GetString (bytes, offset, Length));

                offset += Length;
            }

            return offset;
        }

        private static ushort ToUInt16 (byte[] value, int offset)
        {
            return (ushort)((value[offset] << 8) | value[offset + 1]);
        }

        private static uint ToUInt32 (byte[] value, int offset)
        {
            return (uint)((value[offset] << 24) | (value[offset + 1] << 16) | (value[offset + 2] << 8) | value[offset + 3]);
        }

        private static void ToStream (ushort value, Stream stream)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.WriteByte (bytes[1]);
            stream.WriteByte (bytes[0]);
        }

        private static void ToStream (uint value, Stream stream)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.WriteByte (bytes[3]);
            stream.WriteByte (bytes[2]);
            stream.WriteByte (bytes[1]);
            stream.WriteByte (bytes[0]);
        }

        private static void ToStream (string value, Stream stream)
        {

            if (!string.IsNullOrWhiteSpace (value))
            {
                string[] values = value.Split(new char[] { '.' });
                foreach (string v in values)
                {
                    byte[] bytes = Encoding.Default.GetBytes(v);

                    stream.WriteByte ((byte)bytes.Length);
                    stream.Write (bytes, 0, bytes.Length);
                }
            }

            stream.WriteByte (0);
        }
        private static void ToStream (byte[] value, Stream stream)
        {
            stream.Write (value, 0, value.Length);
        }

        private static void ToStream (SvcParam value, Stream stream)
        {
            DnsProtocol.ToStream (value.Key, stream);
            DnsProtocol.ToStream (value.Len, stream);
            DnsProtocol.ToStream (value.Data, stream);
        }




        public Header header = new Header();
        public List<Question> questions = new List<Question>();
        public List<RRecord> answers = new List<RRecord>();
        public List<RRecord> authoritys = new List<RRecord>();
        public List<RRecord> additionals = new List<RRecord>();


        public int Parse (byte[] bytes)
        {
            int offset = 0;
            DBG.MSG ("DnsProtocol.Parse - START(bytes.Length={0}) \n", bytes.Length);

            DBG.MSG ("DnsProtocol.Parse - Header.Parse - offset={0} ----------------------------------\n", offset);
            try
            {
                offset = this.header.Parse (bytes, 0);

                for (int i = 0; i < this.header.questions; i++)
                {
                    Question q = new Question();

                    DBG.MSG ("DnsProtocol.Parse - Question {0}, offset={1} ------------------------------\n", i + 1, offset);
                    offset = q.Parse (bytes, offset);
                    this.questions.Add (q);
                }

                for (int i = 0; i < this.header.answers; i++)
                {
                    RRecord a = new RRecord();

                    DBG.MSG ("DnsProtocol.Parse - Answer {0}, offset={1} --------------------------------\n", i + 1, offset);
                    offset = a.Parse (bytes, offset);

                    this.answers.Add (a);
                }

                for (int i = 0; i < this.header.authoritys; i++)
                {
                    RRecord a = new RRecord();

                    DBG.MSG ("DnsProtocol.Parse - Authority {0}, offset={1} -----------------------------\n", i + 1, offset);
                    offset = a.Parse (bytes, offset);
                    this.authoritys.Add (a);
                }

                for (int i = 0; i < this.header.additionals; i++)
                {
                    RRecord a = new RRecord();

                    DBG.MSG ("DnsProtocol.Parse - Additional {0}, offset={1} ----------------------------\n", i + 1, offset);
                    offset = a.Parse (bytes, offset);
                    this.additionals.Add (a);
                }
            }
            catch (Exception e)
            {
                DBG.MSG ("DnsProtocol.Parse - Exception, {0} --------------------------------\n", e);
                Debug.Assert(false);
            }

            DBG.MSG ("DnsProtocol.Parse - END \n");

            return offset;
        }

        public byte[] GetBytes ()
        {
            byte[] result = null;

            using (MemoryStream stream = new MemoryStream (0))
            {
                this.header.questions = (ushort)this.questions.Count;
                this.header.answers = (ushort)this.answers.Count;
                this.header.authoritys = (ushort)this.authoritys.Count;
                this.header.additionals = (ushort)this.additionals.Count;

                this.header.ToStream (stream);

                foreach (var v in this.questions)
                {
                    v.ToStream (stream);
                }

                foreach (var v in this.answers)
                {
                    v.ToStream (stream);
                }

                foreach (var v in this.authoritys)
                {
                    v.ToStream (stream);
                }

                foreach (var v in this.additionals)
                {
                    v.ToStream (stream);
                }

                result = stream.ToArray ();
            }

            return result;
        }

        public byte[] SetupQuery (string host, RRType rrType)
        {
            this.header = new Header ();
            this.header.SetQuery ();

            this.questions.Clear ();
            this.answers.Clear();
            this.authoritys.Clear ();
            this.additionals.Clear ();

            Question q = new Question ();
            q.Setup (host, RRClass.IN, rrType);
            
            this.questions.Add (q);

            return GetBytes ();
        }

    }
}
