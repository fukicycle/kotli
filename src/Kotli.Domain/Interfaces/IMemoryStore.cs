using System.Collections.Generic;
using System.Threading.Tasks;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IMemoryStore
{
    Task<IEnumerable<MemoryItem>> LoadAllAsync();
    Task SaveAsync(MemoryItem memoryItem);
    Task DeleteAsync(string key);
}
