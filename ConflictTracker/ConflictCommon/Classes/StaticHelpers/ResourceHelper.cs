using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace ConflictCommon.Classes.StaticHelpers
{
    static class ResourceHelper
    {
        public static Dictionary<string, string> GetResourceStrings(string? FileExtensionfilter = null)
        {
            Dictionary<string, string> resourceDict = new Dictionary<string, string>();
            var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            Dictionary<string, string> resources = new Dictionary<string, string>();
            foreach (string n in names)
            {
                if (FileExtensionfilter is null || n.ToUpper().Contains(FileExtensionfilter.ToUpper()))
                {
                    string resourceName = n.Split(".")[n.Split(".").Count() - 2];
                    Console.WriteLine(resourceName);
                    resourceDict.Add(resourceName, n);
                }
            }

            return resourceDict;
        }
    }
}