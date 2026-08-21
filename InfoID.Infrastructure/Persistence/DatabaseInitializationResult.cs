using System;
using System.Collections.Generic;

namespace InfoID.Infrastructure.Persistence;

public sealed record DatabaseInitializationResult(
    bool Success,
    string DatabasePath,
    bool DatabaseFileExistedBeforeInit,
    IReadOnlyList<string> AppliedMigrations,
    string? ErrorMessage,
    Exception? Exception);