using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsProxyLibrary
{
    public class Statistics
    {
        public ulong query;
        public ulong answer;
        public ulong accept;
        public ulong reject;
        public ulong cache;
    }
}
