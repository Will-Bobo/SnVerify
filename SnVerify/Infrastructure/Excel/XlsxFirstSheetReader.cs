using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SnVerify.Infrastructure.Excel
{
    /// <summary>
    /// 只读取 xlsx 第一个工作表的行。设备行由调用方逐行消费，这里不保留设备集合。
    /// 共享字符串表是 xlsx 的单元格文本存储，不是设备记录。
    /// </summary>
    public static class XlsxFirstSheetReader
    {
        public static void ReadFirstSheetRows(Stream stream, Func<IReadOnlyDictionary<int, string>, bool> onRow)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (onRow == null)
                throw new ArgumentNullException(nameof(onRow));
            if (!stream.CanRead || !stream.CanSeek)
                throw new InvalidDataException("xlsx 读取需要可定位的文件内容。");

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                var sharedStrings = ReadSharedStrings(zip);
                var sheet = FindEntry(zip, ResolveFirstSheetPath(zip));
                if (sheet == null)
                    throw new InvalidDataException("找不到第一个工作表。");

                using (var sheetStream = sheet.Open())
                using (var reader = XmlReader.Create(sheetStream, CreateXmlSettings()))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
                            continue;

                        var cells = ReadRow(reader, sharedStrings);
                        if (!onRow(cells))
                            return;
                    }
                }
            }
        }

        private static Dictionary<int, string> ReadRow(XmlReader rowReader, IReadOnlyList<string> sharedStrings)
        {
            var cells = new Dictionary<int, string>();
            if (rowReader.IsEmptyElement)
                return cells;

            using (var subtree = rowReader.ReadSubtree())
            {
                while (subtree.Read())
                {
                    if (subtree.NodeType != XmlNodeType.Element || subtree.LocalName != "c")
                        continue;

                    var column = ColumnIndex(subtree.GetAttribute("r"));
                    var type = subtree.GetAttribute("t");
                    var text = subtree.IsEmptyElement
                        ? string.Empty
                        : ReadCell(subtree, type, sharedStrings);
                    if (column > 0)
                        cells[column] = text ?? string.Empty;
                }
            }

            return cells;
        }

        private static string ReadCell(XmlReader cellReader, string type, IReadOnlyList<string> sharedStrings)
        {
            var value = new StringBuilder();
            var inline = new StringBuilder();
            using (var subtree = cellReader.ReadSubtree())
            {
                while (subtree.Read())
                {
                    if (subtree.NodeType != XmlNodeType.Element)
                        continue;
                    if (subtree.LocalName == "rPh")
                    {
                        subtree.Skip();
                        continue;
                    }

                    if (subtree.LocalName == "v")
                        value.Append(subtree.ReadElementContentAsString());
                    else if (subtree.LocalName == "t")
                        inline.Append(subtree.ReadElementContentAsString());
                }
            }

            if (string.Equals(type, "s", StringComparison.Ordinal))
            {
                int index;
                if (!int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    || index < 0
                    || index >= sharedStrings.Count)
                {
                    throw new InvalidDataException("共享字符串索引无效。");
                }

                return sharedStrings[index];
            }

            if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
                return inline.ToString();

            return value.Length > 0 ? value.ToString() : inline.ToString();
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = FindEntry(zip, "xl/sharedStrings.xml");
            if (entry == null)
                return list;

            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, CreateXmlSettings()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si")
                        continue;

                    var text = new StringBuilder();
                    using (var subtree = reader.ReadSubtree())
                    {
                        while (subtree.Read())
                        {
                            if (subtree.NodeType != XmlNodeType.Element)
                                continue;
                            if (subtree.LocalName == "rPh")
                            {
                                subtree.Skip();
                                continue;
                            }

                            if (subtree.LocalName == "t")
                                text.Append(subtree.ReadElementContentAsString());
                        }
                    }

                    list.Add(text.ToString());
                }
            }

            return list;
        }

        private static string ResolveFirstSheetPath(ZipArchive zip)
        {
            var workbook = FindEntry(zip, "xl/workbook.xml");
            var relationships = FindEntry(zip, "xl/_rels/workbook.xml.rels");
            if (workbook == null || relationships == null)
                throw new InvalidDataException("xlsx 工作簿结构不完整。");

            string relationshipId = null;
            using (var stream = workbook.Open())
            using (var reader = XmlReader.Create(stream, CreateXmlSettings()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "sheet")
                        continue;

                    relationshipId = AttributeByLocalName(reader, "id");
                    break;
                }
            }

            if (string.IsNullOrEmpty(relationshipId))
                throw new InvalidDataException("xlsx 没有工作表。");

            using (var stream = relationships.Open())
            using (var reader = XmlReader.Create(stream, CreateXmlSettings()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
                        continue;
                    if (!string.Equals(reader.GetAttribute("Id"), relationshipId, StringComparison.Ordinal))
                        continue;

                    var target = reader.GetAttribute("Target");
                    if (string.IsNullOrWhiteSpace(target))
                        throw new InvalidDataException("第一个工作表路径为空。");
                    return NormalizeSheetTarget(target);
                }
            }

            throw new InvalidDataException("找不到第一个工作表关系。");
        }

        private static string NormalizeSheetTarget(string target)
        {
            var path = target.Replace('\\', '/').Trim();
            while (path.StartsWith("./", StringComparison.Ordinal))
                path = path.Substring(2);
            if (path.StartsWith("/", StringComparison.Ordinal))
                path = path.TrimStart('/');
            if (path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                return path;
            return "xl/" + path;
        }

        private static ZipArchiveEntry FindEntry(ZipArchive zip, string path)
        {
            var direct = zip.GetEntry(path);
            if (direct != null)
                return direct;

            var normalized = path.Replace('\\', '/');
            foreach (var entry in zip.Entries)
            {
                if (string.Equals(entry.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        private static string AttributeByLocalName(XmlReader reader, string localName)
        {
            if (!reader.MoveToFirstAttribute())
                return null;

            try
            {
                do
                {
                    if (string.Equals(reader.LocalName, localName, StringComparison.Ordinal))
                        return reader.Value;
                }
                while (reader.MoveToNextAttribute());
            }
            finally
            {
                reader.MoveToElement();
            }

            return null;
        }

        private static int ColumnIndex(string cellReference)
        {
            if (string.IsNullOrEmpty(cellReference))
                return -1;

            var index = 0;
            foreach (var character in cellReference)
            {
                if (character >= 'A' && character <= 'Z')
                    index = index * 26 + (character - 'A' + 1);
                else if (character >= 'a' && character <= 'z')
                    index = index * 26 + (character - 'a' + 1);
                else
                    break;
            }

            return index;
        }

        private static XmlReaderSettings CreateXmlSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = false,
                CloseInput = false
            };
        }
    }
}
