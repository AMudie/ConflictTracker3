using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictConsole.Classes
{
    internal class CIAFactDTO
    {
        public  int Year { get; set; }

        public  string Country { get; set; }

        public  string Key { get; set; }
        public  string SubKey { get; set; }

        public  List<string> Values { get; set; }
    }
}
