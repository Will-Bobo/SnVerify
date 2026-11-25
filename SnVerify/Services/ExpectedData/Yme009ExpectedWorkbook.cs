using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.Excel;

namespace SnVerify.Services.ExpectedData
{
    /// <summary>
    /// 一次顺序读取 xlsx：读取过程中计算 SHA-256，再从这块内存解析。
    /// Zip 包需要定位读取，因此保留的是文件字节，不是设备对象集合。
    /// 本方法在导入线程上同步执行，回调里会写当前事务，不能再切到别的线程。
    /// </summary>
    public static class Yme009ExpectedWorkbook
    {
        public static Yme009ExpectedReadResult Read(string path, Action<Yme009ExpectedDeviceRow> onDevice)
        {
            return ReadCore(path, onDevice, false);
        }

        /// <summary>
        /// 正式导入读取。按表头取出字段并计算指纹，不重复表头、空值和 SN 重复预校验。
        /// </summary>
        public static Yme009ExpectedReadResult ReadForImport(string path, Action<Yme009ExpectedDeviceRow> onDevice)
        {
            return ReadCore(path, onDevice, true);
        }

        public static string ComputeSha256(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
            {
                var chunk = new byte[81920];
                int read;
                while ((read = file.Read(chunk, 0, chunk.Length)) > 0)
                    sha.TransformBlock(chunk, 0, read, null, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return ToHex(sha.Hash);
            }
        }

        private static Yme009ExpectedReadResult ReadCore(string path, Action<Yme009ExpectedDeviceRow> onDevice, bool forImport)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Reject(Yme009ExpectedReadReasons.FileUnreadable, 0, string.Empty);

            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                return Reject(Yme009ExpectedReadReasons.FileUnreadable, 0, string.Empty);
            }

            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
                return Reject(Yme009ExpectedReadReasons.FileUnreadable, 0, string.Empty);
            if (!File.Exists(path))
                return Reject(Yme009ExpectedReadReasons.FileUnreadable, 0, string.Empty);

            var hash = string.Empty;
            try
            {
                using (var buffer = new MemoryStream())
                {
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sha = SHA256.Create())
                    {
                        var chunk = new byte[81920];
                        int read;
                        while ((read = file.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            sha.TransformBlock(chunk, 0, read, null, 0);
                            buffer.Write(chunk, 0, read);
                        }

                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        hash = ToHex(sha.Hash);
                    }

                    buffer.Position = 0;
                    return Parse(buffer, hash, onDevice, forImport);
                }
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidDataException ||
                ex is XmlException ||
                ex is NotSupportedException)
            {
                return Reject(Yme009ExpectedReadReasons.FileUnreadable, 0, hash);
            }
        }

        private static Yme009ExpectedReadResult Parse(
            Stream stream,
            string hash,
            Action<Yme009ExpectedDeviceRow> onDevice,
            bool forImport)
        {
            Yme009ExpectedHeaderMap headers = null;
            var seenSn = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            var status = Yme009ExpectedReadStatus.Accepted;
            string reason = null;
            var stopped = false;

            XlsxFirstSheetReader.ReadFirstSheetRows(stream, cells =>
            {
                if (headers == null)
                {
                    headers = Yme009ExpectedDataRules.MatchHeaders(cells);
                    if (headers.Status != Yme009ExpectedReadStatus.Accepted)
                    {
                        status = forImport ? Yme009ExpectedReadStatus.Rejected : headers.Status;
                        reason = forImport
                            ? Yme009ExpectedReadReasons.ParseFailed
                            : headers.Reason;
                        stopped = true;
                        return false;
                    }

                    return true;
                }

                var decision = forImport
                    ? Yme009ExpectedDataRules.ExtractRow(cells, headers)
                    : Yme009ExpectedDataRules.EvaluateRow(cells, headers, seenSn);
                if (decision.Status != Yme009ExpectedReadStatus.Accepted)
                {
                    status = decision.Status;
                    reason = decision.Reason;
                    stopped = true;
                    return false;
                }

                count++;
                if (onDevice != null)
                    onDevice(decision.Row);
                return true;
            });

            if (stopped)
                return new Yme009ExpectedReadResult(status, reason, count, hash);
            if (headers == null)
                return Reject(Yme009ExpectedReadReasons.RequiredHeaderMissing, 0, hash);
            if (count == 0)
            {
                return new Yme009ExpectedReadResult(
                    Yme009ExpectedReadStatus.Unresolved,
                    Yme009ExpectedReadReasons.UnresolvedNoDeviceRows,
                    0,
                    hash);
            }

            return new Yme009ExpectedReadResult(Yme009ExpectedReadStatus.Accepted, null, count, hash);
        }

        private static Yme009ExpectedReadResult Reject(string reason, int count, string hash)
        {
            return new Yme009ExpectedReadResult(Yme009ExpectedReadStatus.Rejected, reason, count, hash);
        }

        private static string ToHex(byte[] hash)
        {
            var chars = new char[hash.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = digits[hash[i] >> 4];
                chars[i * 2 + 1] = digits[hash[i] & 0x0F];
            }

            return new string(chars);
        }
    }
}
