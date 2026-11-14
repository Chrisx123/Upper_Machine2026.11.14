using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace Display_UpperMachine.Storage
{

    public class SqliteStore : IDataStore
    {
        private readonly string _connString;
        private SqliteConnection? _conn;
        private readonly object _lock = new();   // 后台读取线程与界面线程都会写，需串行化

        public string Name => "SQLite";

        public SqliteStore(string connectionString)
        {
            // 相对路径的数据库文件统一放到程序目录，保证位置确定
            var b = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(b.DataSource) && !Path.IsPathRooted(b.DataSource))
                b.DataSource = Path.Combine(AppContext.BaseDirectory, b.DataSource);
            _connString = b.ToString();
        }

        /// <summary>数据库文件完整路径（用于日志显示）。</summary>
        public string DbPath => new SqliteConnectionStringBuilder(_connString).DataSource;

        public void Init()
        {
            lock (_lock)
            {
                _conn?.Dispose();
                _conn = new SqliteConnection(_connString);
                _conn.Open();

                using var cmd = _conn.CreateCommand();
                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS tag_data (
                        id       INTEGER PRIMARY KEY AUTOINCREMENT,
                        ts       TEXT NOT NULL,
                        tag_name TEXT,
                        address  INTEGER,
                        value    REAL);";
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
                    "INSERT INTO tag_data (ts, tag_name, address, value) VALUES ($ts, $tag, $addr, $val);";
                cmd.Parameters.AddWithValue("$ts", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                cmd.Parameters.AddWithValue("$tag", tagName);
                cmd.Parameters.AddWithValue("$addr", address);
                cmd.Parameters.AddWithValue("$val", value);
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
