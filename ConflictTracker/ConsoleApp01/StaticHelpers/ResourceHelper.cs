using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace ConflictConsole.StaticHelpers
{
    static class ResourceHelper
    {


        public static Dictionary<string, string> GetResourceStrings(string? FileExtensionfilter = null)
        {

            string[] _extensionExlcusions = { ".dbf", ".shx", ".prj" }; //for shapefile resources (geoborders), we only care about the .shp file, so we exclude the others.  This can be modified for other resource types as needed.

            Dictionary<string, string> resourceDict = new Dictionary<string, string>();
            var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            Dictionary<string, string> resources = new Dictionary<string, string>();
            foreach (string n in names)
            {
                if (!_extensionExlcusions.Any(e => n.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                {
                    if (FileExtensionfilter is null || n.ToUpper().Contains(FileExtensionfilter.ToUpper()))
                    {
                        string resourceName = n.Split(".")[n.Split(".").Count() - 2];
                        Console.WriteLine(resourceName);
                        resourceDict.Add(resourceName, n);
                    }
                }
            }

            return resourceDict;
        }
    }
}
