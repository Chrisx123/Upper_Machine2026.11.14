using System;

namespace Display_UpperMachine.Storage
{

    public static class StoreFactory
    {
        public static IDataStore Create(AppConfig config)
        {
            var db = config.Database;

            if (string.Equals(db.Provider, "MySql", StringComparison.OrdinalIgnoreCase))
                return new MySqlStore(db.MySql);

            // 默认 SQLite
            return new SqliteStore(db.Sqlite);
        }
    }
}
