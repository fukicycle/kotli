using System.Collections.Generic;
using System.Threading.Tasks;
using Kotli.Domain.Attributes;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IMemoryStore
{
    Task<IEnumerable<MemoryItem>> LoadAllAsync();
    Task SaveAsync(MemoryItem memoryItem);
    Task DeleteAsync(string key);

    [KotliTool("save_memory", "プロジェクト固有の設計ルール、規約、ユーザーの好みなどを永続メモリ（.kotli/MEMORY.md）に保存・更新します。")]
    Task<string> SaveMemoryToolAsync(
        [KotliToolParam("メモリ項目のキー名（見出し）")] string key,
        [KotliToolParam("保存する具体的な内容")] string content);
}
