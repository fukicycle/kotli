using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public enum ToolApprovalDecision
{
    AllowOnce,
    AllowAlwaysInSession,
    Reject
}

public interface IToolApprovalService
{
    Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolCall toolCall,
        string? diffPreview = null,
        CancellationToken cancellationToken = default);
}
