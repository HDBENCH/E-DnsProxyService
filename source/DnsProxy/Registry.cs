using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsProxyLibrary
{
    public class ERegistry
    {
        private RegistryKey regkey = null;

        public ERegistry (RegistryKey key, string subkey)
        {
            this.regkey = key.CreateSubKey (subkey);
        }

        ~ERegistry ()
        {
            Close ();
        }


        public void Close ()
        {
            this.regkey.Close ();
        }


        public void SetValue (string name, object value)
        {
            TypeConverter typeConverter = TypeDescriptor.GetConverter(value.GetType());

            this.regkey.SetValue (name, typeConverter.ConvertToString (value), RegistryValueKind.String);
        }

        public object GetValue (string name, object def_value)
        {
            object result;
            string value;
            TypeConverter typeConverter = TypeDescriptor.GetConverter(def_value.GetType());

            try
            {
                value = (string)this.regkey.GetValue (name);
                if (value == null)
                {
                    result = def_value;
                }
                else
                {
                    result = typeConverter.ConvertFromString (value);
                    
                }
            }
            catch (Exception)
            {
                result = def_value;
            }

            return result;
        }


    }
}
