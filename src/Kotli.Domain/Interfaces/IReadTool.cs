using System.Threading.Tasks;
using Kotli.Domain.Attributes;

namespace Kotli.Domain.Interfaces;

public interface IReadTool
{
    [KotliTool("read_file", "行番号を指定してローカルファイルの特定のセクションを読み込みます。ファイル全体を読み込む代わりにこれを使用してください。")]
    Task<string> ReadFileAsync(
        [KotliToolParam("読み込むファイルのパス")] string filePath,
        [KotliToolParam("1から始まる開始行 (含む)")] int? startLine = null,
        [KotliToolParam("1から始まる終了行 (含む)")] int? endLine = null);

    [KotliTool("list_directory", "ワークスペースディレクトリ内のすべてのサブディレクトリとファイルを一覧表示します。")]
    Task<string> ListDirectoryAsync(
        [KotliToolParam("一覧表示するディレクトリのパス (ワークスペースルートからの相対パス)")] string dirPath);
}
