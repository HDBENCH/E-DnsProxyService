using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DnsProxyLibrary
{
    public static class DBG
    {
        public static void MSG (string format, params object[] args)
        {
#if DEBUG
            _=Task.Run(() =>{
            
            Debug.Write (DateTime.Now.ToString ("HH:mm:ss:fff ## ") + string.Format (format, args));
            
            });
#endif
        }


        public static void DUMP (byte[] sender, int len = 0, int offset = 0, int width = 16)
        {
#if DEBUG
            _=Task.Run(() =>{

            string time = DateTime.Now.ToString();
            StringBuilder strDump = new StringBuilder ();
            StringBuilder strAscii= new StringBuilder ();

            if(len == 0)
            {
                len = sender.Length;
            }

            strDump.AppendFormat("{0} -----:-------------------------------------------------------------------\n", time);
            strDump.AppendFormat("{0}      : 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F : 0123456789ABCDEF\n", time);
            strDump.AppendFormat("{0} -----:-------------------------------------------------------------------\n", time);

            for (int i = 0; i < len; i++)
            {
                byte b = sender[offset + i];

                if((i % width) == 0)
                {
                    if (strAscii.Length > 0)
                    {
                        strDump.AppendFormat (": {0}\n", strAscii.ToString ());
                        strAscii.Clear ();
                    }

                    strDump.AppendFormat("{0} {1:X04} : ", time, i * width);
                }
                
                strDump.AppendFormat("{0:X02} ", b);
                strAscii.AppendFormat("{0}", ' ' <= b && b <= '~' ? (char)b : '.');
            }

            if (strAscii.Length > 0)
            {
                strDump.Append("".PadLeft(width - strAscii.Length));
                strDump.AppendFormat (": {0}\n", strAscii.ToString ());
            }

            string s = strDump.ToString ();
            Debug.Write (strDump.ToString ());

            });
#endif
        }
    }
}
