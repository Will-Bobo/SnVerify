/// <author>AI Assistant</author>
/// <remarks>
/// 命名校验领域接口。契约见 Phase2.5_Technical_Refactor_Checklist.md §1.3。
/// </remarks>

namespace SnVerify.Domain.Validation
{
    /// <summary>
    /// ProjectName / OrderName 命名校验接口。用于「开始测试」时一次性校验；弹窗由阶段 3 挂接。
    /// </summary>
    public interface IOrderNameValidator
    {
        /// <summary>
        /// 校验名称是否符合规则：禁止文件系统特殊字符，长度上限 64，不允许中文。
        /// </summary>
        /// <param name="name">待校验的 ProjectName 或 OrderName。</param>
        /// <param name="message">校验不通过时的提示信息；通过时为 null 或空。</param>
        /// <returns>true 表示通过，false 表示不通过。</returns>
        bool Validate(string name, out string message);
    }
}
