/// <author>AI Assistant</author>
/// <remarks>
/// 命名校验实现。契约见 Phase2.5_Technical_Refactor_Checklist.md §1.3。
/// </remarks>

using System;
using System.IO;

namespace SnVerify.Domain.Validation
{
    /// <summary>
    /// ProjectName / OrderName 校验实现：禁止文件系统特殊字符，长度上限 64，不允许中文。
    /// </summary>
    public class OrderNameValidator : IOrderNameValidator
    {
        private const int MaxLength = 64;

        /// <inheritdoc />
        public bool Validate(string name, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "名称不能为空";
                return false;
            }

            if (name.Length > MaxLength)
            {
                message = $"名称长度不得超过 {MaxLength} 个字符";
                return false;
            }

            if (ContainsChinese(name))
            {
                message = "名称不允许包含中文";
                return false;
            }

            if (ContainsInvalidFileNameChars(name))
            {
                message = "名称不允许包含文件系统特殊字符（如 \\ / : * ? \" < > |）";
                return false;
            }

            return true;
        }

        private static bool ContainsChinese(string s)
        {
            // 常用汉字 Unicode 范围等
            foreach (var c in s)
            {
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
                if (c >= 0x3400 && c <= 0x4DBF) return true;
            }
            return false;
        }

        private static bool ContainsInvalidFileNameChars(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in s)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    return true;
            }
            return false;
        }
    }
}
