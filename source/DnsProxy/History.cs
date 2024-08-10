using DnsProxyLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DnsProxyLibrary.DataBase;

namespace DnsProxyLibrary
{
    public class HistoryData
    {
        public DateTime time;
        public FLAGS flags = FLAGS.None;
        public string ip;
        public string host;
        public string info;
        public string comment;

        public bool Equals (HistoryData other)
        {
            return (this.ToString () == other.ToString ());
        }

        public override string ToString ()
        {
            string result = "";
           
            result += string.Format ("{0}\t", this.time);
            result += string.Format ("{0}\t", this.flags);
            result += string.Format ("{0}\t", this.ip);
            result += string.Format ("{0}\t", this.host);
            result += string.Format ("{0}\t", this.info);
            result += string.Format ("{0}\t", this.comment);

            return result;
        }

        public string[] ToArray ()
        {
            string[] result = new string [6];

            result[0] = time.ToString ();
            result[1] = flags.ToString ();
            result[2] = ip.ToString ();
            result[3] = host.ToString ();
            result[4] = info.ToString ();
            result[5] = comment.ToString ();

            return result;
        }


        public bool FromString (string str)
        {
            bool result = false;

            try
            {
                string[] strs = str.Split('\t');

                for (int i = 0; i < strs.Length; i++)
                {
                    switch (i)
                    {
                    case 0:
                        {
                            if (!DateTime.TryParse (strs[i], out this.time))
                            {
                                //Debug.Assert (false);
                                throw new Exception ("DateTime.TryParse failed.");
                            }
                        }
                        break;

                    case 1:
                        {
                            if (!Enum.TryParse (strs[i], out this.flags))
                            {
                                //Debug.Assert (false);
                                throw new Exception ("Enum.TryParse failed.");
                            }
                        }
                        break;

                    case 2:
                        {
                            this.ip = strs[i];
                        }
                        break;

                    case 3:
                        {
                            this.host = strs[i];
                        }
                        break;

                    case 4:
                        {
                            this.info = strs[i];
                        }
                        break;

                    case 5:
                        {
                            this.comment = strs[i];
                        }
                        break;

                    default:
                        {
                            //Debug.Assert(false);
                        }
                        break;
                    }

                }

                result = true;
            }
            catch (Exception e)
            {
                DBG.MSG ("HistoryData.FromString - Exception, {0}, {1}\n", e.HResult, e.Message);
            }

            return result;
        }


        public void Set (DateTime time, FLAGS flags, string ip, string host, string info, string comment)
        {
            this.time = time;
            this.flags = flags;
            this.ip = ip;
            this.host = host;
            this.info = info;
            this.comment = comment;
        }

    }
}
