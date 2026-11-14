using MySqlConnector;
using System;

namespace Display_UpperMachine.Storage
{
    /// MySQL 存储实现（MySqlConnector）。
    public class MySqlStore : IDataStore
    {
        private readonly string _connString;
        private MySqlConnection? _conn;
        private readonly object _lock = new();

        public string Name => "MySQL";

        public MySqlStore(string connectionString) => _connString = connectionString;

        public void Init()
        {
            lock (_lock)
            {
                _conn?.Dispose();
                _conn = new MySqlConnection(_connString);
                _conn.Open();

                using var cmd = _conn.CreateCommand();
                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS tag_data (
                        id       BIGINT AUTO_INCREMENT PRIMARY KEY,
                        ts       DATETIME(3) NOT NULL,
                        tag_name VARCHAR(50),
                        address  INT,
                        value    DOUBLE);";
                cmd.ExecuteNonQuery();
            }
        }

        public void Save(DateTime ts, string tagName, int address, double value)
        {
            lock (_lock)
            {
                if (_conn == null) Init();

                using var cmd = _conn!.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO tag_data (ts, tag_name, address, value) VALUES (@ts, @tag, @addr, @val);";
                cmd.Parameters.AddWithValue("@ts", ts);
                cmd.Parameters.AddWithValue("@tag", tagName);
                cmd.Parameters.AddWithValue("@addr", address);
                cmd.Parameters.AddWithValue("@val", value);
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _conn?.Dispose();
                _conn = null;
            }
        }
    }
}
