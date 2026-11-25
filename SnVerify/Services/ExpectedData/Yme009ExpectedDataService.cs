using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Domain.Validation;
using SnVerify.Services.Storage;

namespace SnVerify.Services.ExpectedData
{
    public interface IYme009ExpectedDataService
    {
        Task<Yme009ExpectedStageResult> StageAsync(string filePath);

        Task<Yme009ExpectedStartResult> StartAsync(Yme009ExpectedStartRequest request);
    }

    /// <summary>
    /// 预期数据的预校验暂存和开始测试导入。不激活 Session。
    /// </summary>
    public sealed class Yme009ExpectedDataService : IYme009ExpectedDataService
    {
        private readonly IStorageService _storage;

        public Yme009ExpectedDataService(IStorageService storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public Task<Yme009ExpectedStageResult> StageAsync(string filePath)
        {
            return Task.Run(() => Stage(filePath));
        }

        public async Task<Yme009ExpectedStartResult> StartAsync(Yme009ExpectedStartRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.File == null || !request.File.PrecheckPassed)
                throw new ArgumentException("没有通过预校验的预期数据文件。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.OrderId))
                throw new ArgumentException("项目名和订单名不能为空。", nameof(request));

            var path = request.File.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Fail(Yme009ExpectedReadReasons.FileUnreadable, Yme009ExpectedOperatorText.FileUnavailable);

            string hash;
            try
            {
                hash = await Task.Run(() => Yme009ExpectedWorkbook.ComputeSha256(path)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail(Yme009ExpectedReadReasons.FileUnreadable, Yme009ExpectedOperatorText.FileUnavailable);
            }

            if (!string.Equals(hash, request.File.Sha256, StringComparison.OrdinalIgnoreCase))
                return Fail(Yme009ExpectedReadReasons.FingerprintMismatch, Yme009ExpectedOperatorText.FingerprintMismatch);

            var at = DateTime.Now;
            var orderKey = await PrepareOrderAsync(request, at).ConfigureAwait(false);
            var sessionName = await NextSessionNameAsync(request.OrderId.Trim(), at).ConfigureAwait(false);
            var imported = await _storage.ImportYme009ExpectedDataAsync(new Yme009ExpectedImportRequest
            {
                OrderId = orderKey,
                SessionName = sessionName,
                StartTime = at,
                FilePath = path,
                ExpectedSha256 = request.File.Sha256
            }).ConfigureAwait(false);

            if (!imported.Committed)
                return Fail(imported.Reason, MessageForImport(imported.Reason));

            return new Yme009ExpectedStartResult(
                true,
                sessionName,
                imported.SessionId,
                imported.RecordCount,
                null,
                Yme009ExpectedOperatorText.ImportDoneDetailWithCount(imported.RecordCount));
        }

        private static Yme009ExpectedStageResult Stage(string filePath)
        {
            var read = Yme009ExpectedWorkbook.Read(filePath, null);
            if (read.Status == Yme009ExpectedReadStatus.Accepted)
            {
                var file = new Yme009ExpectedFileStaging(
                    filePath,
                    Path.GetFileName(filePath),
                    read.RecordCount,
                    read.Sha256);
                return new Yme009ExpectedStageResult(read.Status, null, file, file.FileName + "（" + file.RecordCount + " 条）");
            }

            return new Yme009ExpectedStageResult(
                read.Status,
                read.Reason,
                null,
                Yme009ExpectedOperatorText.ForPrecheck(read.Reason));
        }

        private async Task<int> PrepareOrderAsync(Yme009ExpectedStartRequest request, DateTime at)
        {
            var projectId = request.ProjectId.Trim();
            var orderName = request.OrderId.Trim();
            var productCode = string.IsNullOrWhiteSpace(request.ProductCode) ? null : request.ProductCode.Trim();
            var existingProductId = await _storage.GetProductIdByProductNameAsync(projectId).ConfigureAwait(false);
            int productId;
            if (existingProductId.HasValue)
            {
                productId = existingProductId.Value;
                if (productCode != null)
                    await _storage.EnsureProductCodeAsync(productId, productCode).ConfigureAwait(false);
            }
            else
            {
                productId = await _storage.CreateProductAsync(new Product
                {
                    ProductName = projectId,
                    ProductCode = productCode,
                    CreatedAt = at
                }).ConfigureAwait(false);
            }

            var orderExists = await _storage.OrderExistsByOrderNameAndProductAsync(orderName, productId).ConfigureAwait(false);
            if (!orderExists)
            {
                await _storage.CreateOrderAsync(new Order
                {
                    OrderName = orderName,
                    ProductId = productId,
                    CreatedAt = at
                }).ConfigureAwait(false);
            }

            var orders = await _storage.GetAllOrdersAsync().ConfigureAwait(false);
            var order = orders.FirstOrDefault(item => item.OrderName == orderName && item.ProductId == productId);
            if (order == null)
                throw new InvalidOperationException("未能找到订单记录: " + orderName);
            return order.Id;
        }

        private async Task<string> NextSessionNameAsync(string orderId, DateTime at)
        {
            var sessionName = SessionIdGenerator.Format(orderId, at);
            var guard = 0;
            while (await _storage.SessionExistsAsync(sessionName).ConfigureAwait(false))
            {
                at = at.AddSeconds(1);
                sessionName = SessionIdGenerator.Format(orderId, at);
                guard++;
                if (guard > 10)
                    throw new InvalidOperationException("生成 SessionId 失败：短时间内重复冲突过多");
            }

            return sessionName;
        }

        private static Yme009ExpectedStartResult Fail(string reason, string message)
        {
            return new Yme009ExpectedStartResult(false, string.Empty, 0, 0, reason, message);
        }

        private static string MessageForImport(string reason)
        {
            if (reason == Yme009ExpectedReadReasons.FingerprintMismatch)
                return Yme009ExpectedOperatorText.FingerprintMismatch;
            if (reason == Yme009ExpectedReadReasons.FileUnreadable)
                return Yme009ExpectedOperatorText.FileUnavailable;
            if (reason == Yme009ExpectedReadReasons.WriteFailed)
                return Yme009ExpectedOperatorText.WriteFailed;
            if (reason == Yme009ExpectedReadReasons.UnresolvedBlankRow
                || reason == Yme009ExpectedReadReasons.UnresolvedNoDeviceRows)
                return Yme009ExpectedOperatorText.UnresolvedFile;
            return Yme009ExpectedOperatorText.ParseFailed;
        }
    }
}