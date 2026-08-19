using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ConflictCommon.Classes.StaticHelpers
{
    public static class FileHelper
    {
        /// <summary>
        /// Wrapper method for opening a file with the default application associated with its file type.
        /// </summary>
        /// <param name="filePath">Path of file to open. </param>
        public static void OpenFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };

                Process.Start(psi);

            }
        }
    }
}
