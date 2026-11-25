using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.ExpectedData;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009ExpectedWorkbookTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Xlsx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public void Should_Accept_When_RequiredHeadersArePresent()
        {
            var path = Workbook(Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-1"));
            var rows = new List<Yme009ExpectedDeviceRow>();

            var result = Yme009ExpectedWorkbook.Read(path, rows.Add);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.Reason, Is.Null);
            Assert.That(result.RecordCount, Is.EqualTo(1));
            Assert.That(result.Sha256, Is.EqualTo(Sha256(path)));
            Assert.That(rows[0].Sn, Is.EqualTo("SN-1"));
            Assert.That(rows[0].WifiMac, Is.EqualTo("48:C8:62:16:BF:1F"));
        }

        [Test]
        public void Should_Accept_When_HeadersDifferByCaseAndOuterSpace()
        {
            var path = Workbook(new[] { " sn ", "wifiMac", " Imei1", "imei2 " }, Row("SN-1"));

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.RecordCount, Is.EqualTo(1));
        }

        [Test]
        public void Should_Accept_When_RequiredColumnsAreReordered()
        {
            var path = Workbook(
                new[] { "IMEI2", "PSK", "IMEI1", "SN", "SSID", "WIFIMAC" },
                new[] { "IMEI-2", "secret", "IMEI-1", "  SN-9  ", "wifi", " AA:BB " });
            Yme009ExpectedDeviceRow row = null;

            var result = Yme009ExpectedWorkbook.Read(path, item => row = item);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(row.Sn, Is.EqualTo("SN-9"));
            Assert.That(row.WifiMac, Is.EqualTo("AA:BB"));
            Assert.That(row.Imei1, Is.EqualTo("IMEI-1"));
            Assert.That(row.Imei2, Is.EqualTo("IMEI-2"));
        }

        [Test]
        public void Should_Reject_When_RequiredHeaderIsMissing()
        {
            var path = Workbook(new[] { "SN", "WIFIMAC", "IMEI1" }, new[] { "SN-1", "AA", "1" });

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredHeaderMissing));
        }

        [Test]
        public void Should_Reject_When_RequiredHeaderIsDuplicated()
        {
            var path = Workbook(new[] { "SN", "sn", "WIFIMAC", "IMEI1", "IMEI2" }, Row("SN-1"));

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredHeaderDuplicate));
        }

        [Test]
        public void Should_Reject_When_RequiredValueIsEmpty()
        {
            var calls = 0;
            var path = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row("SN-1"),
                new[] { "SN-2", "AA:BB", "  ", "IMEI-2" });

            var result = Yme009ExpectedWorkbook.Read(path, row => calls++);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredValueMissing));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Should_Reject_When_SnIsDuplicatedAfterTrim()
        {
            var calls = 0;
            var path = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row(" ABC "),
                Row("ABC"));

            var result = Yme009ExpectedWorkbook.Read(path, row => calls++);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.SnDuplicate));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Should_Accept_When_SnDiffersOnlyByCase()
        {
            var path = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row("ABC"),
                Row("abc"));

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.RecordCount, Is.EqualTo(2));
        }

        [Test]
        public void Should_IgnoreOptionalColumns_When_ReadingDevices()
        {
            var path = Workbook(
                new[] { "SN", "PSK", "SSID", "SSID_5G", "WIFIMAC", "IMEI1", "IMEI2", "IMEI3", "QR" },
                new[] { "SN-1", "secret", "name", "name5", "AA:BB", "1", "2", "3", "qr" });
            Yme009ExpectedDeviceRow row = null;

            var result = Yme009ExpectedWorkbook.Read(path, item => row = item);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(row.Sn, Is.EqualTo("SN-1"));
            Assert.That(row.WifiMac, Is.EqualTo("AA:BB"));
            Assert.That(row.Imei1, Is.EqualTo("1"));
            Assert.That(row.Imei2, Is.EqualTo("2"));
        }

        [Test]
        public void Should_ReadRowsOneByOne_When_WorkbookHasMultipleDevices()
        {
            var path = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row("SN-1"),
                Row("SN-2"),
                Row("SN-3"));
            var seen = new List<string>();

            var result = Yme009ExpectedWorkbook.Read(path, row => seen.Add(row.Sn));

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.RecordCount, Is.EqualTo(3));
            Assert.That(seen, Is.EqualTo(new[] { "SN-1", "SN-2", "SN-3" }));
        }

        [Test]
        public void Should_ReadOnlyFirstSheet_When_LaterSheetContainsDifferentRows()
        {
            var path = Path.Combine(_directory, "sheets.xlsx");
            Yme009ExpectedWorkbookFiles.WriteSheets(
                path,
                new Yme009ExpectedWorkbookFiles.Sheet("First", Yme009ExpectedWorkbookFiles.RequiredHeaders, new[] { Row("SN-1") }),
                new Yme009ExpectedWorkbookFiles.Sheet("Second", Yme009ExpectedWorkbookFiles.RequiredHeaders, new[] { Row("SN-1"), Row("SN-9") }));

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.RecordCount, Is.EqualTo(1));
        }

        [Test]
        public void Should_LeaveHeaderOnlyFileUnresolved_When_NoDeviceRowExists()
        {
            var path = Workbook(Yme009ExpectedWorkbookFiles.RequiredHeaders);

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Unresolved));
            Assert.That(result.Status, Is.Not.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.Status, Is.Not.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.UnresolvedNoDeviceRows));
            Assert.That(result.RecordCount, Is.EqualTo(0));
        }

        [Test]
        public void Should_LeaveBlankRowUnresolved_When_RowHasNoContent()
        {
            var path = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row("SN-1"),
                new[] { "   ", " ", "  ", "" });

            var result = Yme009ExpectedWorkbook.Read(path, null);

            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Unresolved));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.UnresolvedBlankRow));
        }

        [Test]
        public void Should_Reject_When_FileIsMissingOrNotXlsx()
        {
            var missing = Yme009ExpectedWorkbook.Read(Path.Combine(_directory, "missing.xlsx"), null);
            var textPath = Path.Combine(_directory, "notes.txt");
            File.WriteAllText(textPath, "SN");
            var text = Yme009ExpectedWorkbook.Read(textPath, null);

            Assert.That(missing.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(missing.Reason, Is.EqualTo(Yme009ExpectedReadReasons.FileUnreadable));
            Assert.That(text.Reason, Is.EqualTo(Yme009ExpectedReadReasons.FileUnreadable));
        }

        [Test]
        public void Should_SkipEmptyAndDuplicateChecks_When_ReadingForImport()
        {
            var emptyField = Workbook(
                Yme009ExpectedWorkbookFiles.RequiredHeaders,
                Row("SN-1"),
                new[] { "SN-2", "AA", "", "2" });
            var duplicate = Workbook(Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-1"), Row("SN-1"));
            var missingHeader = Workbook(new[] { "SN", "WIFIMAC", "IMEI1" }, new[] { "SN-1", "AA", "1" });

            var precheck = Yme009ExpectedWorkbook.Read(emptyField, null);
            var imported = Yme009ExpectedWorkbook.ReadForImport(emptyField, null);
            var duplicateImport = Yme009ExpectedWorkbook.ReadForImport(duplicate, null);
            var headerImport = Yme009ExpectedWorkbook.ReadForImport(missingHeader, null);

            Assert.That(precheck.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredValueMissing));
            Assert.That(imported.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(imported.RecordCount, Is.EqualTo(2));
            Assert.That(duplicateImport.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(duplicateImport.RecordCount, Is.EqualTo(2));
            Assert.That(headerImport.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(headerImport.Reason, Is.EqualTo(Yme009ExpectedReadReasons.ParseFailed));
            Assert.That(Yme009ExpectedWorkbook.ComputeSha256(emptyField), Is.EqualTo(imported.Sha256));
        }

        [Test]
        public void Should_ChangeSha256_When_FileContentChanges()
        {
            var path = Workbook(Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-1"));
            var before = Yme009ExpectedWorkbook.Read(path, null).Sha256;
            Yme009ExpectedWorkbookFiles.Write(path, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-2"));
            var after = Yme009ExpectedWorkbook.Read(path, null).Sha256;

            Assert.That(before, Is.Not.EqualTo(after));
            Assert.That(after, Is.EqualTo(Sha256(path)));
        }

        private string Workbook(string[] headers, params string[][] rows)
        {
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".xlsx");
            Yme009ExpectedWorkbookFiles.Write(path, headers, rows);
            return path;
        }

        private static string[] Row(string sn)
        {
            return new[] { sn, "48:C8:62:16:BF:1F", "111111111111111", "222222222222222" };
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
