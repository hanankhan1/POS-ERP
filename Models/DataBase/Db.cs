using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace POSERP.Models.DataBase
{
    public class Db
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public Db(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}