using System;
using System.Collections.Generic;
using System.Text;
using System.Data;

namespace ConflictConsole.StaticHelpers
{
    internal static class DataTableHelper
    {
        public static void PrintDataTable(DataTable table)
        {
            // Print column headers
            foreach (DataColumn col in table.Columns)
            {
                Console.Write($"{col.ColumnName}\t");
            }
            Console.WriteLine();

            // Print rows
            foreach (DataRow row in table.Rows)
            {
                foreach (var item in row.ItemArray)
                {
                    Console.Write($"{item}\t");
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Dump out a 
        /// </summary>
        /// <param name="table"></param>
        /// <param name="filePath"></param>
        /// <remarks>AI generated code; Copilot; prompt: "Export a datatable to a CSV file in c#"</remarks>
        public static void DataTableToCsv(DataTable table, string filePath)
        {
            var sb = new StringBuilder();

            // Write headers
            for (int i = 0; i < table.Columns.Count; i++)
            {
                sb.Append(Escape(table.Columns[i].ColumnName));
                if (i < table.Columns.Count - 1)
                    sb.Append(",");
            }
            sb.AppendLine();

            // Write rows
            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    sb.Append(Escape(row[i]?.ToString() ?? ""));
                    if (i < table.Columns.Count - 1)
                        sb.Append(",");
                }
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            // If the value contains a comma, quote, or newline, wrap it in quotes
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                value = value.Replace("\"", "\"\""); // escape quotes
                return $"\"{value}\"";
            }

            return value;
        }
    }
}
