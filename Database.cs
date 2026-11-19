using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace WeaponPaints
{
	public class Database(string dbConnectionString)
	{
        public Task<MySqlConnection> GetConnectionAsync() => GetConnectionAsync(CancellationToken.None);

        public async Task<MySqlConnection> GetConnectionAsync(CancellationToken token)
		{
            var connection = new MySqlConnection(dbConnectionString);
            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
				return connection;
			}
            catch (Exception ex)
            {
                await connection.DisposeAsync();
                if (!token.IsCancellationRequested) WeaponPaints.Instance.Logger.LogError($"Unable to connect to database: {ex.Message}");
				throw;
			}
		}
	}
}
