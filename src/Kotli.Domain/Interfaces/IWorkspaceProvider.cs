using System.Threading.Tasks;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IWorkspaceProvider
{
    Task<WorkspaceState> GetCurrentStateAsync();
}
