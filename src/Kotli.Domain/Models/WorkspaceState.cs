using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record WorkspaceState(
    string CurrentBranch,
    string Diff,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> UntrackedFiles);
