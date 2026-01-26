/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 存储服务实现，负责 SQLite 数据持久化和 Excel 导出（Phase2 扩展）
    /// </summary>
    public class StorageService : IStorageService, IDisposable
    {
        private readonly string _dbPath;
        private readonly IFileLogger _logger;
        private SQLiteConnection _connection;
        private readonly object _lockObject = new object();
        private readonly object _snapshotLock = new object();
        private StorageSnapshot _snapshot;
        private bool _disposed = false;

        /// <summary>
        /// 当前存储服务状态快照
        /// </summary>
        public StorageSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? StorageSnapshot.Idle();
                }
            }
            private set
            {
                lock (_snapshotLock)
                {
                    _snapshot = value;
                }
            }
        }

        /// <summary>
        /// 初始化存储服务
        /// </summary>
        /// <param name="dbPath">SQLite 数据库文件路径</param>
        /// <param name="logger">文件日志记录器（可选）</param>
        public StorageService(string dbPath, IFileLogger logger = null)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _logger = logger ?? new NullFileLogger();
            _snapshot = StorageSnapshot.Idle();
            
            // 设置 EPPlus 许可证上下文（非商业用途）
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        /// <summary>
        /// 初始化 SQLite 数据库和表结构
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var connectionString = $"Data Source={_dbPath};Version=3;";
                _connection = new SQLiteConnection(connectionString);
                await Task.Run(() => _connection.Open());

                await CreateTablesAsync();
                _logger?.LogInfo($"数据库初始化成功: {_dbPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"数据库初始化失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 确保数据库连接已初始化（如果未初始化则自动初始化）
        /// </summary>
        private void EnsureConnectionInitialized()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                // 同步初始化连接（用于紧急情况）
                var connectionString = $"Data Source={_dbPath};Version=3;";
                _connection = new SQLiteConnection(connectionString);
                _connection.Open();
                
                // 同步创建表结构
                CreateTablesAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// 创建数据库表结构
        /// </summary>
        private async Task CreateTablesAsync()
        {
            var createBatchTable = @"
                CREATE TABLE IF NOT EXISTS Batch (
                    BatchId TEXT PRIMARY KEY,
                    StartTime DATETIME NOT NULL,
                    Operator TEXT,
                    Remark TEXT
                )";

            var createSnVerifyResultTable = @"
                CREATE TABLE IF NOT EXISTS SnVerifyResult (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BatchId TEXT NOT NULL,
                    SN TEXT NOT NULL,
                    Result TEXT NOT NULL,
                    FailReason TEXT,
                    VerifyTime DATETIME NOT NULL,
                    FOREIGN KEY (BatchId) REFERENCES Batch(BatchId)
                )";

            var createIndex = @"
                CREATE INDEX IF NOT EXISTS idx_sn_verify_batch_sn 
                ON SnVerifyResult(BatchId, SN)";

            await ExecuteNonQueryAsync(createBatchTable);
            await ExecuteNonQueryAsync(createSnVerifyResultTable);
            await ExecuteNonQueryAsync(createIndex);
        }

        /// <summary>
        /// 执行非查询 SQL 命令
        /// </summary>
        private async Task ExecuteNonQueryAsync(string sql)
        {
            // 注意：此方法在 CreateTablesAsync 中被调用，而 CreateTablesAsync 在 EnsureConnectionInitialized 中被调用
            // 因此这里不需要再次调用 EnsureConnectionInitialized，避免循环调用
            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// 创建批次
        /// </summary>
        public async Task CreateBatchAsync(BatchInfo batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            if (string.IsNullOrWhiteSpace(batch.BatchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batch));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    INSERT INTO Batch (BatchId, StartTime, Operator, Remark)
                    VALUES (@BatchId, @StartTime, @Operator, @Remark)";

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batch.BatchId);
                            command.Parameters.AddWithValue("@StartTime", batch.StartTime);
                            command.Parameters.AddWithValue("@Operator", (object)batch.Operator ?? DBNull.Value);
                            command.Parameters.AddWithValue("@Remark", (object)batch.Remark ?? DBNull.Value);
                            command.ExecuteNonQuery();
                        }
                    }
                });

                _logger?.LogInfo($"批次创建成功: {batch.BatchId}");
            }
            catch (SQLiteException ex) when (ex.Message.Contains("UNIQUE constraint"))
            {
                _logger?.LogWarning($"批次已存在: {batch.BatchId}");
                throw new InvalidOperationException($"批次 {batch.BatchId} 已存在", ex);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"创建批次失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查批次是否存在
        /// </summary>
        public async Task<bool> BatchExistsAsync(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return false;

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                var sql = "SELECT COUNT(1) FROM Batch WHERE BatchId = @BatchId";
                var result = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查批次存在性失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查指定批次内 SN 是否重复
        /// </summary>
        public async Task<bool> IsSnDuplicateAsync(string batchId, string sn)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    SELECT COUNT(1) FROM SnVerifyResult 
                    WHERE BatchId = @BatchId AND SN = @SN";

                var result = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            command.Parameters.AddWithValue("@SN", sn);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查 SN 重复性失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查指定批次内 SN 在 PASS 记录中是否重复（新增：仅检查 PASS 记录）
        /// </summary>
        public async Task<bool> IsSnDuplicateInPassAsync(string batchId, string sn)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    SELECT COUNT(1) FROM SnVerifyResult 
                    WHERE BatchId = @BatchId AND SN = @SN AND Result = 'PASS'";

                var result = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            command.Parameters.AddWithValue("@SN", sn);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查 PASS 记录中 SN 重复性失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取指定批次和 SN 的 FAIL 记录（如果存在）
        /// </summary>
        public async Task<SnVerifyResult> GetFailResultBySnAsync(string batchId, string sn)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    SELECT Id, BatchId, SN, Result, FailReason, VerifyTime
                    FROM SnVerifyResult
                    WHERE BatchId = @BatchId AND SN = @SN AND Result != 'PASS'
                    ORDER BY VerifyTime DESC
                    LIMIT 1";

                var result = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            command.Parameters.AddWithValue("@SN", sn);
                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    return new SnVerifyResult
                                    {
                                        Id = reader.GetInt32(0),
                                        BatchId = reader.GetString(1),
                                        SN = reader.GetString(2),
                                        Result = reader.GetString(3),
                                        FailReason = reader.IsDBNull(4) ? null : reader.GetString(4),
                                        VerifyTime = reader.GetDateTime(5)
                                    };
                                }
                            }
                        }
                        return null;
                    }
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"获取 FAIL 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 更新现有的校验结果记录
        /// </summary>
        public async Task UpdateVerifyResultAsync(SnVerifyResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (result.Id <= 0)
                throw new ArgumentException("记录 ID 必须大于 0", nameof(result));
            if (string.IsNullOrWhiteSpace(result.BatchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.SN))
                throw new ArgumentException("SN 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.Result))
                throw new ArgumentException("校验结果不能为空", nameof(result));

            EnsureConnectionInitialized();

            try
            {
                Snapshot = StorageSnapshot.Processing(result.BatchId);

                var sql = @"
                    UPDATE SnVerifyResult 
                    SET Result = @Result, FailReason = @FailReason, VerifyTime = @VerifyTime
                    WHERE Id = @Id";

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@Id", result.Id);
                            command.Parameters.AddWithValue("@Result", result.Result);
                            command.Parameters.AddWithValue("@FailReason", (object)result.FailReason ?? DBNull.Value);
                            command.Parameters.AddWithValue("@VerifyTime", result.VerifyTime);
                            command.ExecuteNonQuery();
                        }
                    }
                });

                var recordCount = await GetRecordCountAsync(result.BatchId);
                Snapshot = StorageSnapshot.Saved(result.SN, result.BatchId, recordCount);

                _logger?.LogInfo($"校验结果更新成功: Id={result.Id}, BatchId={result.BatchId}, SN={result.SN}, Result={result.Result}");
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"更新失败: {ex.Message}", result.BatchId);
                _logger?.LogError($"更新校验结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存 SN 校验结果（Phase2：更新 Snapshot）
        /// </summary>
        public async Task SaveVerifyResultAsync(SnVerifyResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(result.BatchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.SN))
                throw new ArgumentException("SN 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.Result))
                throw new ArgumentException("校验结果不能为空", nameof(result));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                // 检查 SN 是否重复
                var isDuplicate = await IsSnDuplicateAsync(result.BatchId, result.SN);
                if (isDuplicate)
                {
                    Snapshot = StorageSnapshot.DuplicateSn(result.SN, result.BatchId);
                    _logger?.LogWarning($"SN 重复，保存失败: BatchId={result.BatchId}, SN={result.SN}");
                    throw new InvalidOperationException($"SN {result.SN} 在批次 {result.BatchId} 中已存在");
                }

                Snapshot = StorageSnapshot.Processing(result.BatchId);

                var sql = @"
                    INSERT INTO SnVerifyResult (BatchId, SN, Result, FailReason, VerifyTime)
                    VALUES (@BatchId, @SN, @Result, @FailReason, @VerifyTime)";

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", result.BatchId);
                            command.Parameters.AddWithValue("@SN", result.SN);
                            command.Parameters.AddWithValue("@Result", result.Result);
                            command.Parameters.AddWithValue("@FailReason", (object)result.FailReason ?? DBNull.Value);
                            command.Parameters.AddWithValue("@VerifyTime", result.VerifyTime);
                            command.ExecuteNonQuery();
                        }
                    }
                });

                // 获取当前批次记录数
                var recordCount = await GetRecordCountAsync(result.BatchId);
                Snapshot = StorageSnapshot.Saved(result.SN, result.BatchId, recordCount);

                _logger?.LogInfo($"校验结果保存成功: BatchId={result.BatchId}, SN={result.SN}, Result={result.Result}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"保存失败: {ex.Message}", result.BatchId);
                _logger?.LogError($"保存校验结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取指定批次的记录数
        /// </summary>
        private async Task<int> GetRecordCountAsync(string batchId)
        {
            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            var sql = "SELECT COUNT(1) FROM SnVerifyResult WHERE BatchId = @BatchId";
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.Parameters.AddWithValue("@BatchId", batchId);
                        var count = command.ExecuteScalar();
                        return Convert.ToInt32(count);
                    }
                }
            });
        }

        /// <summary>
        /// 获取指定批次的所有校验结果
        /// </summary>
        public async Task<IReadOnlyList<SnVerifyResult>> GetResultsByBatchAsync(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    SELECT Id, BatchId, SN, Result, FailReason, VerifyTime
                    FROM SnVerifyResult
                    WHERE BatchId = @BatchId
                    ORDER BY VerifyTime ASC";

                var results = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        var list = new List<SnVerifyResult>();
                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    list.Add(new SnVerifyResult
                                    {
                                        Id = reader.GetInt32(0),
                                        BatchId = reader.GetString(1),
                                        SN = reader.GetString(2),
                                        Result = reader.GetString(3),
                                        FailReason = reader.IsDBNull(4) ? null : reader.GetString(4),
                                        VerifyTime = reader.GetDateTime(5)
                                    });
                                }
                            }
                        }
                        return list;
                    }
                });

                return results.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"查询批次结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 导出批次结果到 Excel 文件（Phase2：支持 PASS/FAIL 分表，更新 Snapshot）
        /// </summary>
        public async Task ExportBatchResultAsync(string batchId, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            try
            {
                Snapshot = StorageSnapshot.Processing(batchId);

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var results = await GetResultsByBatchAsync(batchId);
                var fileName = $"{batchId}.xlsx";
                var filePath = Path.Combine(outputDirectory, fileName);

                await Task.Run(() =>
                {
                    using (var package = new ExcelPackage())
                    {
                        // 创建 PASS Sheet
                        var passSheet = package.Workbook.Worksheets.Add("PASS");
                        WriteSheetHeader(passSheet);
                        var passResults = results.Where(r => r.Result == "PASS").ToList();
                        WriteSheetData(passSheet, passResults, startRow: 2);

                        // 创建 FAIL Sheet（包含 FAIL 和 TIMEOUT）
                        var failSheet = package.Workbook.Worksheets.Add("FAIL");
                        WriteSheetHeader(failSheet);
                        var failResults = results.Where(r => r.Result == "FAIL" || r.Result == "TIMEOUT").ToList();
                        WriteSheetData(failSheet, failResults, startRow: 2);

                        // 保存文件
                        var fileInfo = new FileInfo(filePath);
                        package.SaveAs(fileInfo);
                    }
                });

                Snapshot = StorageSnapshot.Saved(null, batchId, results.Count);
                _logger?.LogInfo($"批次结果导出成功: BatchId={batchId}, FilePath={filePath}");
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"导出失败: {ex.Message}", batchId);
                _logger?.LogError($"导出批次结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 写入 Sheet 表头
        /// </summary>
        private void WriteSheetHeader(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Id";
            sheet.Cells[1, 2].Value = "SN";
            sheet.Cells[1, 3].Value = "Result";
            sheet.Cells[1, 4].Value = "FailReason";
            sheet.Cells[1, 5].Value = "VerifyTime";

            // 设置表头样式
            using (var range = sheet.Cells[1, 1, 1, 5])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
        }

        /// <summary>
        /// 写入 Sheet 数据
        /// </summary>
        private void WriteSheetData(ExcelWorksheet sheet, IList<SnVerifyResult> results, int startRow)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var row = startRow + i;
                var result = results[i];
                sheet.Cells[row, 1].Value = result.Id;
                sheet.Cells[row, 2].Value = result.SN;
                sheet.Cells[row, 3].Value = result.Result;
                sheet.Cells[row, 4].Value = result.FailReason ?? string.Empty;
                // 将 VerifyTime 格式化为可读中文日期时间格式，例如 “2026年1月11日 13:21:22”
                sheet.Cells[row, 5].Value = result.VerifyTime.ToString("yyyy年M月d日 HH:mm:ss");
            }

            // 自动调整列宽
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            System.GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源（内部实现）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }
                _disposed = true;
            }
        }
    }
}
