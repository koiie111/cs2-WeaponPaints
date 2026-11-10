using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace WeaponPaints
{
	public class Database(string dbConnectionString)
	{
		public async Task<MySqlConnection> GetConnectionAsync()
		{
            var connection = new MySqlConnection(dbConnectionString);
            try
            {
				await connection.OpenAsync();
				return connection;
			}
            catch (Exception ex)
            {
                await connection.DisposeAsync();
                WeaponPaints.Instance.Logger.LogError($"Unable to connect to database: {ex.Message}");
				throw;
			}
		}
	}
}