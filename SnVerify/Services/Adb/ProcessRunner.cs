/// <author>
/// AI Assistant
/// </author>

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SnVerify.Services.Adb
{
    /// <summary>
    /// 进程执行器实现，用于执行外部进程命令
    /// </summary>
    public class ProcessRunner : IProcessRunner
    {
        /// <summary>
        /// 执行进程命令
        /// </summary>
        public async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            int timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("文件名不能为空", nameof(fileName));

            Process process = null;
            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 等待进程完成或超时
                var completed = await Task.Run(() => process.WaitForExit(timeout), cancellationToken);
                
                if (!completed)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // 忽略 Kill 失败
                    }
                    return ProcessExecutionResult.Timeout();
                }

                // 等待输出读取完成
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var standardOutput = outputBuilder.ToString().TrimEnd('\r', '\n');
                var standardError = errorBuilder.ToString().TrimEnd('\r', '\n');

                if (process.ExitCode == 0)
                {
                    return ProcessExecutionResult.Success(standardOutput, process.ExitCode);
                }
                else
                {
                    return ProcessExecutionResult.Failure(
                        $"Process exited with code {process.ExitCode}",
                        standardError,
                        process.ExitCode);
                }
            }
            catch (OperationCanceledException)
            {
                // CancellationToken 被取消，返回超时结果
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // 忽略 Kill 失败
                    }
                }
                return ProcessExecutionResult.Timeout();
            }
            catch (Exception ex)
            {
                return ProcessExecutionResult.Failure($"Process execution failed: {ex.Message}");
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // 忽略清理失败
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }
}
