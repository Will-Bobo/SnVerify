using System.IO;
using OfficeOpenXml;

namespace SnVerify.Tests.Services
{
    public static class Yme009ExpectedWorkbookFiles
    {
        public static readonly string[] RequiredHeaders = { "SN", "WIFIMAC", "IMEI1", "IMEI2" };

        public static void Write(string path, string[] headers, params string[][] rows)
        {
            WriteSheets(path, new Sheet("Expected", headers, rows));
        }

        public static void WriteSheets(string path, params Sheet[] sheets)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using (var package = new ExcelPackage())
            {
                foreach (var sheet in sheets)
                {
                    var worksheet = package.Workbook.Worksheets.Add(sheet.Name);
                    for (var column = 0; column < sheet.Headers.Length; column++)
                        worksheet.Cells[1, column + 1].Value = sheet.Headers[column];

                    for (var row = 0; row < sheet.Rows.Length; row++)
                    {
                        for (var column = 0; column < sheet.Rows[row].Length; column++)
                        {
                            var value = sheet.Rows[row][column];
                            if (value != null)
                                worksheet.Cells[row + 2, column + 1].Value = value;
                        }
                    }
                }

                package.SaveAs(new FileInfo(path));
            }
        }

        public sealed class Sheet
        {
            public Sheet(string name, string[] headers, string[][] rows)
            {
                Name = name;
                Headers = headers ?? new string[0];
                Rows = rows ?? new string[0][];
            }

            public string Name { get; }
            public string[] Headers { get; }
            public string[][] Rows { get; }
        }
    }
}
