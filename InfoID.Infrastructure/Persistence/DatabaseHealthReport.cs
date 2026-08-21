using System.Collections.Generic;

namespace InfoID.Infrastructure.Persistence;

public sealed record DatabaseHealthReport(
    string DatabasePath,
    bool FileExists,
    bool CanConnect,
    bool IsWritable,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    string? ErrorMessage);