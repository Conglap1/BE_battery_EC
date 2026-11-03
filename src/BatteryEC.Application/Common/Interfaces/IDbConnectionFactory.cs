using System.Data;
using System.Data.Common;

namespace BatteryEC.Application.Common.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
    ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
