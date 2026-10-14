using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;
using Kotli.Infrastructure.Storage;
using Kotli.Infrastructure.Tools;
using Kotli.Infrastructure.Workspace;
using Xunit;

namespace Kotli.Tests;

public class LocalToolsTests : IDisposable
{
    private readonly string _tempWorkspace;

    public LocalToolsTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "kotli_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspace))
        {
            // Restore permissions before deleting if any test changed UnixFileMode
            foreach (var entry in Directory.EnumerateFileSystemEntries(_tempWorkspace, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }
                catch { }
            }
            Directory.Delete(_tempWorkspace, recursive: true);
        }
    }

    [Fact]
    public async Task ファイル読み込みツールが行番号付与と各種エラー分岐およびパストラバーサル防止を正しく処理すること()
    {
        // デフォルトコンストラクタのカバレッジ確認
        _ = new LocalReadTool();

        var readTool = new LocalReadTool(_tempWorkspace);
        var sampleFile = Path.Combine(_tempWorkspace, "Sample.cs");
        await File.WriteAllLinesAsync(sampleFile, new[] { "line one", "line two", "line three" });

        // 1. 全行読み込み（startLine/endLine省略）
        var fullOutput = await readTool.ReadFileAsync("Sample.cs");
        Assert.Contains("   1: line one", fullOutput);
        Assert.Contains("   3: line three", fullOutput);

        // 2. 行範囲指定読み込み
        var rangeOutput = await readTool.ReadFileAsync("Sample.cs", 1, 2);
        Assert.Contains("   1: line one", rangeOutput);
        Assert.Contains("   2: line two", rangeOutput);
        Assert.DoesNotContain("line three", rangeOutput);

        // 3. 空ファイル読み込み
        var emptyFile = Path.Combine(_tempWorkspace, "Empty.txt");
        await File.WriteAllTextAsync(emptyFile, "");
        var emptyOutput = await readTool.ReadFileAsync("Empty.txt");
        Assert.Contains("(空のファイル)", emptyOutput);

        // 4. 存在しないファイル
        var missingOutput = await readTool.ReadFileAsync("NotExist.txt");
        Assert.Contains("[エラー] ファイルが見つかりません", missingOutput);

        // 5. 開始行が総行数を超える場合
        var outOfBoundsOutput = await readTool.ReadFileAsync("Sample.cs", 10, 12);
        Assert.Contains("開始行 10 は総行数 (3) を超えています", outOfBoundsOutput);

        // 6. 開始行が終了行より大きい場合
        var invertedRangeOutput = await readTool.ReadFileAsync("Sample.cs", 2, 1);
        Assert.Contains("開始行 2 は終了行 1 より大きくすることはできません", invertedRangeOutput);

        // 7. プレフィックス一致の兄弟ディレクトリへのパストラバーサル阻止
        var siblingDir = _tempWorkspace + "-secret";
        var traversalAttempt = await readTool.ReadFileAsync($"../{Path.GetFileName(siblingDir)}/secret.txt");
        Assert.Contains("[エラー] アクセスが拒否されました", traversalAttempt);

        // 8. 読み込み権限がないファイルでの例外キャッチ分岐
        if (!OperatingSystem.IsWindows())
        {
            var lockedFile = Path.Combine(_tempWorkspace, "Locked.txt");
            await File.WriteAllTextAsync(lockedFile, "secret");
            File.SetUnixFileMode(lockedFile, UnixFileMode.None);
            var errorOutput = await readTool.ReadFileAsync("Locked.txt");
            Assert.Contains("[エラー] ファイルの読み込みに失敗しました", errorOutput);
            File.SetUnixFileMode(lockedFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task ディレクトリ一覧ツールがサブディレクトリやファイルおよびエラーケースを正しく処理すること()
    {
        var readTool = new LocalReadTool(_tempWorkspace);
        var subDir = Path.Combine(_tempWorkspace, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, "root.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(subDir, "child.txt"), "world");

        // 1. ルートディレクトリ（空文字指定）
        var rootList = await readTool.ListDirectoryAsync("");
        Assert.Contains("[ディレクトリ]  subdir/", rootList);
        Assert.Contains("[ファイル] root.txt", rootList);

        // 2. サブディレクトリ指定
        var subList = await readTool.ListDirectoryAsync("subdir");
        Assert.Contains("[ファイル] subdir/child.txt", subList);

        // 3. ワークスペース外へのトラバーサル拒否
        var outList = await readTool.ListDirectoryAsync("../outside");
        Assert.Contains("[エラー] アクセスが拒否されました", outList);

        // 4. 存在しないディレクトリ
        var missingDir = await readTool.ListDirectoryAsync("no_such_dir");
        Assert.Contains("[エラー] ディレクトリが見つかりません", missingDir);

        // 5. 権限がないディレクトリでの例外キャッチ分岐
        if (!OperatingSystem.IsWindows())
        {
            var noPermDir = Path.Combine(_tempWorkspace, "noperm");
            Directory.CreateDirectory(noPermDir);
            File.SetUnixFileMode(noPermDir, UnixFileMode.None);
            var errList = await readTool.ListDirectoryAsync("noperm");
            Assert.Contains("[エラー] ディレクトリ一覧の取得に失敗しました", errList);
            File.SetUnixFileMode(noPermDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ファイル書き込みおよびテキスト置換ツールがDiffプレビューと全エラーケースを正しく処理すること()
    {
        // デフォルトコンストラクタのカバレッジ確認
        _ = new LocalWriteTool();

        var writeTool = new LocalWriteTool(_tempWorkspace);

        // 1. サブディレクトリを自動作成して新規ファイル書き込み + 新規Diffプレビュー
        var newDiff = await writeTool.GetWriteDiffPreviewAsync("nested/Hello.txt", "line1\nline2\nline3\nline4\nline5\nline6\nline7");
        Assert.Contains("--- /dev/null", newDiff);
        var writeResult = await writeTool.WriteFileAsync("nested/Hello.txt", "line1\nline2\nline3\nline4\nline5\nline6\nline7");
        Assert.Contains("[成功]", writeResult);

        // 2. 既存ファイル上書き時のDiffプレビュー（前後の共通コンテキスト行を含む）
        var overwriteDiff = await writeTool.GetWriteDiffPreviewAsync("nested/Hello.txt", "line1\nline2\nline3\nLINE4_MOD\nline5\nline6\nline7");
        Assert.Contains("--- a/nested/Hello.txt", overwriteDiff);
        Assert.Contains("- line4", overwriteDiff);
        Assert.Contains("+ LINE4_MOD", overwriteDiff);

        // 3. ワークスペース外への書き込み・プレビュー拒否
        Assert.Contains("[エラー] アクセスが拒否されました", await writeTool.WriteFileAsync("../evil.txt", "x"));
        Assert.Contains("[エラー] ワークスペース外のパスです", await writeTool.GetWriteDiffPreviewAsync("../evil.txt", "x"));
        Assert.Contains("--- a/../evil.txt", await writeTool.GetReplaceDiffPreviewAsync("../evil.txt", "a", "b"));
        Assert.Contains("--- a/missing.txt", await writeTool.GetReplaceDiffPreviewAsync("missing.txt", "a", "b"));

        // 4. 置換プレビュー（対象文字列が存在する場合と存在しない場合）
        var replaceDiffFound = await writeTool.GetReplaceDiffPreviewAsync("nested/Hello.txt", "line4", "LINE4_UPDATED");
        Assert.Contains("- line4", replaceDiffFound);
        Assert.Contains("+ LINE4_UPDATED", replaceDiffFound);

        var replaceDiffNotFound = await writeTool.GetReplaceDiffPreviewAsync("nested/Hello.txt", "not_in_file", "NEW");
        Assert.Contains("- not_in_file", replaceDiffNotFound);

        // 5. ReplaceTextAsync の正常系とエラー系（ワークスペース外・未存在・該当なし・複数一致）
        Assert.Contains("[エラー] アクセスが拒否されました", await writeTool.ReplaceTextAsync("../evil.txt", "a", "b"));
        Assert.Contains("[エラー] ファイルが見つかりません", await writeTool.ReplaceTextAsync("missing.txt", "a", "b"));
        Assert.Contains("見つかりませんでした", await writeTool.ReplaceTextAsync("nested/Hello.txt", "no_match", "b"));

        await writeTool.WriteFileAsync("dup.txt", "same\nsame");
        Assert.Contains("複数検出されました", await writeTool.ReplaceTextAsync("dup.txt", "same", "diff"));

        var replaceOk = await writeTool.ReplaceTextAsync("nested/Hello.txt", "line4", "LINE4_UPDATED");
        Assert.Contains("[成功]", replaceOk);

        // 6. 空文字列へのDiffプレビューおよびワークスペースルート直指定(".")によるI/O例外キャッチ分岐
        var emptyDiff = await writeTool.GetWriteDiffPreviewAsync("nested/Hello.txt", "");
        Assert.Contains("--- a/nested/Hello.txt", emptyDiff);

        var writeRootEx = await writeTool.WriteFileAsync(".", "content");
        Assert.Contains("[エラー] ファイルの書き込みに失敗しました", writeRootEx);

        if (!OperatingSystem.IsWindows())
        {
            var roFile = Path.Combine(_tempWorkspace, "readonly.txt");
            await File.WriteAllTextAsync(roFile, " original ");
            File.SetUnixFileMode(roFile, UnixFileMode.UserRead);
            var replaceEx = await writeTool.ReplaceTextAsync("readonly.txt", "original", "changed");
            Assert.Contains("[エラー] テキストの置換に失敗しました", replaceEx);
            File.SetUnixFileMode(roFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task ファイル検索およびGrep検索ツールが上限切り詰めやバイナリ除外を含めて正しく処理すること()
    {
        // デフォルトコンストラクタのカバレッジ確認
        _ = new LocalSearchTool();

        var searchTool = new LocalSearchTool(_tempWorkspace);
        var subDir = Path.Combine(_tempWorkspace, "src");
        var gitDir = Path.Combine(_tempWorkspace, ".git");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(gitDir);

        // 除外対象ディレクトリ内のファイル（検索されないこと）
        await File.WriteAllTextAsync(Path.Combine(gitDir, "ignored.cs"), "RunMagic");

        // バイナリ拡張子ファイル（Grepでスキップされること）
        await File.WriteAllTextAsync(Path.Combine(subDir, "image.png"), "RunMagic");

        // 160文字を超える長い行を持つファイル
        string longLine = "RunMagic " + new string('X', 180);
        await File.WriteAllTextAsync(Path.Combine(subDir, "TargetService.cs"), $"public class TargetService\n{{\n    // {longLine}\n}}");

        // 1. FindFilesAsync: パターン一致・空パターン・該当なし
        Assert.Contains("src/TargetService.cs", await searchTool.FindFilesAsync("Target"));
        Assert.Contains("src/TargetService.cs", await searchTool.FindFilesAsync(""));
        Assert.Contains("一致するファイルは見つかりませんでした", await searchTool.FindFilesAsync("NonExistentFilePattern999"));

        // 2. GrepSearchAsync: 正常検索・単一ファイル指定・存在しないパス指定・不正な正規表現・空クエリ・ワークスペース外・該当なし
        var grepResult = await searchTool.GrepSearchAsync("RunMagic");
        Assert.Contains("src/TargetService.cs:3:", grepResult);
        Assert.Contains("...", grepResult); // 160文字超の切り詰め

        var singleFileGrep = await searchTool.GrepSearchAsync("RunMagic", "src/TargetService.cs");
        Assert.Contains("src/TargetService.cs:3:", singleFileGrep);

        var nonExistentPathGrep = await searchTool.GrepSearchAsync("RunMagic", "src/no_such_path");
        Assert.Contains("一致する箇所は見つかりませんでした", nonExistentPathGrep);

        // 不正な正規表現("[unclosed")でもRegex.Escapeへフォールバックして検索できること
        await File.WriteAllTextAsync(Path.Combine(subDir, "Bracket.txt"), "contains [unclosed literal");
        var invalidRegexGrep = await searchTool.GrepSearchAsync("[unclosed", "src/Bracket.txt");
        Assert.Contains("src/Bracket.txt:1:", invalidRegexGrep);

        Assert.Contains("[エラー] 検索クエリが空です", await searchTool.GrepSearchAsync(""));
        Assert.Contains("[エラー] アクセスが拒否されました", await searchTool.GrepSearchAsync("query", "../outside"));

        // 3. 権限のないファイルおよびディレクトリがある場合のスキップ分岐
        if (!OperatingSystem.IsWindows())
        {
            var unreadableFile = Path.Combine(subDir, "unreadable.txt");
            await File.WriteAllTextAsync(unreadableFile, "RunMagic");
            File.SetUnixFileMode(unreadableFile, UnixFileMode.None);

            var unreadableDir = Path.Combine(subDir, "locked_dir");
            Directory.CreateDirectory(unreadableDir);
            File.SetUnixFileMode(unreadableDir, UnixFileMode.None);

            _ = await searchTool.GrepSearchAsync("RunMagic");
            _ = await searchTool.FindFilesAsync("*");

            File.SetUnixFileMode(unreadableFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(unreadableDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // 4. FindFilesAsync 100件上限と GrepSearchAsync 50件上限の到達分岐
        var bulkDir = Path.Combine(_tempWorkspace, "bulk");
        Directory.CreateDirectory(bulkDir);
        for (int i = 0; i < 105; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(bulkDir, $"item_{i:D3}.txt"), "BulkMatchLine");
        }

        var maxFilesResult = await searchTool.FindFilesAsync("*.txt");
        Assert.Contains("上限 100 件に達したため省略されました", maxFilesResult);

        var maxGrepResult = await searchTool.GrepSearchAsync("BulkMatchLine", "bulk");
        Assert.Contains("上限 50 件に達したため省略されました", maxGrepResult);
    }

    [Fact]
    public async Task ウェブ検索スタブツールがクエリを含む応答を返すこと()
    {
        var webSearch = new StubWebSearchTool();
        var result = await webSearch.SearchAsync("C# Source Generator");
        Assert.Contains("C# Source Generator", result);
        Assert.Contains("[ウェブ検索]", result);
    }

    [Fact]
    public async Task コマンド実行ツールが標準出力とエラー出力およびタイムアウトやディレクトリ制限を正しく処理すること()
    {
        // デフォルトコンストラクタのカバレッジ確認
        _ = new LocalCommandTool();

        var cmdTool = new LocalCommandTool(_tempWorkspace, TimeSpan.FromSeconds(5), maxOutputChars: 50);
        var subDir = Path.Combine(_tempWorkspace, "sub");
        Directory.CreateDirectory(subDir);

        // 1. 正常なコマンド実行（標準出力）
        var okResult = await cmdTool.ExecuteCommandAsync("echo hello_kotli");
        Assert.Contains("[成功] コマンド終了コード: 0", okResult);
        Assert.Contains("--- 標準出力 ---", okResult);
        Assert.Contains("hello_kotli", okResult);

        // 2. 作業ディレクトリ（相対パス）指定
        await File.WriteAllTextAsync(Path.Combine(subDir, "subfile.txt"), "in_sub");
        var subDirResult = await cmdTool.ExecuteCommandAsync(
            OperatingSystem.IsWindows() ? "dir /b" : "ls",
            "sub");
        Assert.Contains("subfile.txt", subDirResult);

        // 3. 標準エラー出力および非ゼロ終了コード
        var failResult = await cmdTool.ExecuteCommandAsync("echo err_msg >&2 && exit 3");
        Assert.Contains("[失敗] コマンド終了コード: 3", failResult);
        Assert.Contains("--- 標準エラー出力 ---", failResult);
        Assert.Contains("err_msg", failResult);

        // 4. 出力なしのコマンド
        var silentResult = await cmdTool.ExecuteCommandAsync(OperatingSystem.IsWindows() ? "rem" : "true");
        Assert.Contains("(出力なし)", silentResult);

        // 5. 長い出力の切り詰め（maxOutputChars: 50）
        var longOutputResult = await cmdTool.ExecuteCommandAsync("echo 123456789012345678901234567890123456789012345678901234567890");
        Assert.Contains("出力が上限 50 文字を超えたため省略されました", longOutputResult);

        // 6. 空コマンド・ワークスペース外ディレクトリ・存在しないディレクトリ
        Assert.Contains("[エラー] 実行するコマンドが空です", await cmdTool.ExecuteCommandAsync("   "));
        Assert.Contains("[エラー] アクセスが拒否されました", await cmdTool.ExecuteCommandAsync("echo hi", "../outside"));
        Assert.Contains("[エラー] 作業ディレクトリが見つかりません", await cmdTool.ExecuteCommandAsync("echo hi", "no_such_dir"));

        // 7. タイムアウト検証
        var shortTimeoutTool = new LocalCommandTool(_tempWorkspace, TimeSpan.FromMilliseconds(100));
        var timeoutCmd = OperatingSystem.IsWindows() ? "ping -n 5 127.0.0.1" : "sleep 2";
        var timeoutResult = await shortTimeoutTool.ExecuteCommandAsync(timeoutCmd);
        Assert.Contains("タイムアウトしました", timeoutResult);

        // 8. プロセス起動失敗（存在しないシェル実行ファイル指定）の検証
        var invalidShellTool = new LocalCommandTool(
            _tempWorkspace,
            shellExecutable: "/non_existent_shell_bin_kotli",
            shellSwitch: "-c");
        var startErrorResult = await invalidShellTool.ExecuteCommandAsync("echo hi");
        Assert.Contains("[エラー] コマンドの起動に失敗しました:", startErrorResult);
    }

    [Fact]
    public async Task 自動生成されたToolExecutionRouterが全ツール呼び出しと承認フローおよび例外処理を正しくルーティングすること()
    {
        var readTool = new LocalReadTool(_tempWorkspace);
        var writeTool = new LocalWriteTool(_tempWorkspace);
        var webSearch = new StubWebSearchTool();
        var workspace = new GitDiffProvider(_tempWorkspace);
        var searchTool = new LocalSearchTool(_tempWorkspace);
        var memoryStore = new MarkdownMemoryStore("test-session", Path.Combine(_tempWorkspace, ".kotli"));
        var commandTool = new LocalCommandTool(_tempWorkspace);

        // 1. 必須依存関係のnullチェック検証
        Assert.Throws<ArgumentNullException>(() => new ToolExecutionRouter(null!, writeTool, webSearch, workspace));
        Assert.Throws<ArgumentNullException>(() => new ToolExecutionRouter(readTool, null!, webSearch, workspace));
        Assert.Throws<ArgumentNullException>(() => new ToolExecutionRouter(readTool, writeTool, null!, workspace));
        Assert.Throws<ArgumentNullException>(() => new ToolExecutionRouter(readTool, writeTool, webSearch, null!));

        // 2. オプションの検索・メモリ・コマンドツールがnullの場合の分岐検証
        var minimalRouter = new ToolExecutionRouter(readTool, writeTool, webSearch, workspace, searchTool: null, memoryStore: null, commandTool: null, approvalService: null);
        Assert.Equal(6, minimalRouter.GetToolDefinitions().Count());
        var unavailableFind = await minimalRouter.ExecuteToolAsync(new ToolCall("c1", "find_files", "{}"));
        Assert.Contains("現在利用できません", unavailableFind.Content);
        var unavailableGrep = await minimalRouter.ExecuteToolAsync(new ToolCall("c2", "grep_search", "{}"));
        Assert.Contains("現在利用できません", unavailableGrep.Content);
        var unavailableMem = await minimalRouter.ExecuteToolAsync(new ToolCall("c3", "save_memory", "{}"));
        Assert.Contains("現在利用できません", unavailableMem.Content);
        var unavailableCmd = await minimalRouter.ExecuteToolAsync(new ToolCall("c3b", "execute_command", "{\"command\":\"echo hi\"}"));
        Assert.Contains("現在利用できません", unavailableCmd.Content);
        var noApprovalServiceWrite = await minimalRouter.ExecuteToolAsync(
            new ToolCall("c4", "write_file", "{\"filePath\":\"NoApproval.txt\",\"content\":\"ok\"}"));
        Assert.Contains("[成功]", noApprovalServiceWrite.Content);

        // 3. 拒否(Reject)および1回のみ許可(AllowOnce)時の検証 (write_file, replace_text, execute_command)
        var allowOnceRouter = new ToolExecutionRouter(
            readTool, writeTool, webSearch, workspace, searchTool, memoryStore, commandTool,
            new StubApprovalService(ToolApprovalDecision.AllowOnce));
        var allowOnceWrite = await allowOnceRouter.ExecuteToolAsync(
            new ToolCall("ao1", "write_file", "{\"filePath\":\"Once.txt\",\"content\":\"once\"}"));
        Assert.Contains("[成功]", allowOnceWrite.Content);

        var rejectRouter = new ToolExecutionRouter(
            readTool, writeTool, webSearch, workspace, searchTool, memoryStore, commandTool,
            new StubApprovalService(ToolApprovalDecision.Reject));

        var rejectedWrite = await rejectRouter.ExecuteToolAsync(
            new ToolCall("cw1", "write_file", "{\"filePath\":\"Blocked.txt\",\"content\":\"x\"}"));
        Assert.Contains("[キャンセル]", rejectedWrite.Content);

        var rejectedReplace = await rejectRouter.ExecuteToolAsync(
            new ToolCall("cr1", "replace_text", "{\"filePath\":\"Blocked.txt\",\"oldString\":\"a\",\"newString\":\"b\"}"));
        Assert.Contains("[キャンセル]", rejectedReplace.Content);

        var rejectedCmd = await rejectRouter.ExecuteToolAsync(
            new ToolCall("cc1", "execute_command", "{\"command\":\"echo blocked\"}"));
        Assert.Contains("[キャンセル]", rejectedCmd.Content);

        // 4. セッション中常時許可(AllowAlwaysInSession)および全10ツールの実行検証
        var sessionApproveRouter = new ToolExecutionRouter(
            readTool, writeTool, webSearch, workspace, searchTool, memoryStore, commandTool,
            new StubApprovalService(ToolApprovalDecision.AllowAlwaysInSession));

        Assert.Equal(10, sessionApproveRouter.GetToolDefinitions().Count());

        var wRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("1", "write_file", "{\"filePath\":\"RouterTest.txt\",\"content\":\"hello world\"}"));
        Assert.Contains("[成功]", wRes.Content);

        // replace_text 単体でも AllowAlwaysInSession 分岐を通過させる
        var replaceSessionApproveRouter = new ToolExecutionRouter(
            readTool, writeTool, webSearch, workspace, searchTool, memoryStore, commandTool,
            new StubApprovalService(ToolApprovalDecision.AllowAlwaysInSession));
        var repRes = await replaceSessionApproveRouter.ExecuteToolAsync(
            new ToolCall("2", "replace_text", "{\"filePath\":\"RouterTest.txt\",\"oldString\":\"world\",\"newString\":\"kotli\"}"));
        Assert.Contains("[成功]", repRes.Content);

        // 2回目の承認対象ツール呼び出しは _sessionApprovedAll == true の分岐を通る
        var secondWriteRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("2b", "write_file", "{\"filePath\":\"RouterTest2.txt\",\"content\":\"second\"}"));
        Assert.Contains("[成功]", secondWriteRes.Content);

        var rRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("3", "read_file", "{\"filePath\":\"RouterTest.txt\",\"startLine\":1,\"endLine\":1}"));
        Assert.Contains("hello kotli", rRes.Content);

        var lRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("4", "list_directory", ""));
        Assert.Contains("RouterTest.txt", lRes.Content);

        var fRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("5", "find_files", "{\"pattern\":\"RouterTest*\"}"));
        Assert.Contains("RouterTest.txt", fRes.Content);

        var gRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("6", "grep_search", "{\"query\":\"kotli\",\"path\":\"RouterTest.txt\"}"));
        Assert.Contains("RouterTest.txt:1:", gRes.Content);

        var wsRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("7", "web_search", "{\"query\":\"dotnet\"}"));
        Assert.Contains("[ウェブ検索]", wsRes.Content);

        var diffRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("8", "get_git_diff", "{}"));
        Assert.NotNull(diffRes.Content);

        var memRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("9", "save_memory", "{\"key\":\"coding_style\",\"content\":\"use Japanese test names\"}"));
        Assert.Contains("[成功]", memRes.Content);

        var cmdRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("9b", "execute_command", "{\"command\":\"echo router_cmd_ok\"}"));
        Assert.Contains("router_cmd_ok", cmdRes.Content);

        // 5. 不明なツール名および不正JSONによる例外キャッチ分岐の検証
        var unknownRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("10", "unknown_tool", "{}"));
        Assert.Contains("[エラー] 不明なツール名", unknownRes.Content);

        var invalidJsonRes = await sessionApproveRouter.ExecuteToolAsync(
            new ToolCall("11", "read_file", "{invalid_json"));
        Assert.Contains("[エラー] ツールの実行に失敗しました", invalidJsonRes.Content);
    }

    private sealed class StubApprovalService : IToolApprovalService
    {
        private readonly ToolApprovalDecision _decision;
        public StubApprovalService(ToolApprovalDecision decision) => _decision = decision;

        public Task<ToolApprovalDecision> RequestApprovalAsync(
            ToolCall toolCall,
            string? diffPreview = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_decision);
        }
    }
}
