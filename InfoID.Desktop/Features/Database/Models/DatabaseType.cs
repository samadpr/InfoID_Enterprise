using System;
using System.Collections.Generic;
using System.Text;

namespace InfoID.Desktop.Features.Database.Models
{
    public enum DatabaseType
    {
        SQLite,
        MySQL,
        SampleDatabase,
        PostgreSQL,
        SQLServer,
        ODBC,
        Excel,
        InternalDatabase,
        CSV,
        TXT,
        Access,
        API,
        GoogleSheets,
        /// <summary>Visitor registrations from the Visitors page (read-only in the designer).</summary>
        Visitors
    }
}
