using InfoID.Desktop.Features.Database.Models;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.Services;

public interface IDatabaseConnectionService
{
    Task<bool> TestConnectionAsync(DatabaseConnection connection);

    Task ConnectAsync(DatabaseConnection connection);

    Task DisconnectAsync(DatabaseConnection connection);
}