using DnsProxyLibrary;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsProxyLibrary
{
    public enum CMD : ushort
    {
        NOP = 0,
        LOAD,
        ADD,
        SET,
        DEL,
        HISTORY,
        SET_HISTORY,
        ENABLE,
        DNS_CLEAR,
        COMMENT,
    }

    public class Command
    {
        CMD cmd;
        byte[] byte_data;
        string str_data;

        public Command ()
        {
            this.cmd = CMD.NOP;
            this.byte_data = new byte[0];
            this.str_data = "";
        }

        public Command (CMD cmd, byte[] byte_value)
        {
            this.cmd = cmd;
            SetData(byte_value);
            SetString("");
        }

        public Command (CMD cmd, byte[] byte_value, string str_value)
        {
            this.cmd = cmd;
            SetData(byte_value);
            SetString(str_value);
        }

        public byte[] ToBytes ()
        {
            return Create(this.cmd, this.byte_data, this.str_data);
        }


        public static byte[] Create (CMD cmd, byte[] byte_value = null, string str_value = null)
        {
            byte[] result = null;
            byte[] str_bytes;

            if (byte_value == null)
            {
                byte_value = new byte[0];
            }

            if (str_value == null)
            {
                str_value = "";
            }

            str_bytes = Encoding.UTF8.GetBytes(str_value);

            using (MemoryStream ms = new MemoryStream ())
            {
                ms.WriteByte((byte)(((ushort)cmd & 0xFF00) >> 8));
                ms.WriteByte((byte)(((ushort)cmd & 0x00FF) >> 0));
                
                ms.WriteByte((byte)((byte_value.Length & 0xFF000000) >> 24));
                ms.WriteByte((byte)((byte_value.Length & 0x00FF0000) >> 16));
                ms.WriteByte((byte)((byte_value.Length & 0x0000FF00) >> 8));
                ms.WriteByte((byte)((byte_value.Length & 0x000000FF) >> 0));
                ms.Write (byte_value, 0, byte_value.Length);

                ms.WriteByte((byte)((str_bytes.Length & 0xFF00) >> 8));
                ms.WriteByte((byte)((str_bytes.Length & 0x00FF) >> 0));
                ms.Write (str_bytes, 0, str_bytes.Length);

                result = ms.ToArray ();
            }

            return result;
        }


        public bool Parse (byte[] bytes)
        {
            bool result = false;
            int len;

            do
            {
                if (bytes.Length < 2)
                {
                    break;
                }

                this.cmd = (CMD)(bytes[0] << 8);
                this.cmd |= (CMD)(bytes[1] << 0);

                len  = bytes[2] << 24;
                len |= bytes[3] << 16;
                len |= bytes[4] << 8;
                len |= bytes[5] << 0;

                this.byte_data = new byte[len];

                Buffer.BlockCopy (bytes, 2 + 4, this.byte_data, 0, len);

                len  = bytes[this.byte_data.Length + 2 + 4 + 0] << 8;
                len |= bytes[this.byte_data.Length + 2 + 4 + 1] << 0;
                
                this.str_data = Encoding.UTF8.GetString (bytes, this.byte_data.Length + 2 + 4 + 2, len);
                
                result = true;
            }
            while (false);

            return result;
        }

        public CMD GetCMD()
        {
            return this.cmd;
        }
        public void SetCMD(CMD cmd)
        {
            this.cmd = cmd;
        }

        public byte[] GetData()
        { 
            return this.byte_data;
        }

        public void SetData(byte[] value)
        {
            if (value == null)
            {
                value = new byte[0];
            }

            this.byte_data = new byte[value.Length];
            Buffer.BlockCopy(value, 0, this.byte_data, 0, value.Length);
        }

        public string GetString()
        { 
            return this.str_data;
        }

        public void SetString(string value)
        {
            if (value == null)
            {
                value = "";
            }

            this.str_data = value;
        }


    }
}
