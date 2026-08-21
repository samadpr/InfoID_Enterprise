namespace InfoID.Domain.Common.Enums;

public enum DataSourceType
{
    Excel, CSV, SQLServer, MySQL, PostgreSQL, Oracle, Access, RestApi
}

public enum ImportBatchStatus
{
    Running, Completed, Failed, PartialSuccess
}
