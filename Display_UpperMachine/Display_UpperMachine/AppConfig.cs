using System.IO;
using System.Text.Json;

namespace Display_UpperMachine
{
    /// <summary>
    /// 应用配置（对应 appsettings.json）。
    /// </summary>
    public class AppConfig
    {
        /// <summary>数据库配置节。</summary>
        public DatabaseConfig Database { get; set; } = new();
    }

    /// <summary>
    /// 数据库配置：Provider 决定用 SQLite 还是 MySQL，各自一个连接串。
    /// </summary>
    public class DatabaseConfig
    {
        /// <summary>数据库类型：Sqlite / MySql（不区分大小写）。</summary>
        public string Provider { get; set; } = "Sqlite";

        /// <summary>SQLite 连接串（Data Source 可用相对路径，相对程序目录）。</summary>
        public string Sqlite { get; set; } = "Data Source=upper.db";

        /// <summary>MySQL 连接串。</summary>
        public string MySql { get; set; } = "";
    }

    /// <summary>
    /// 配置加载器：从程序目录读取 appsettings.json（不存在则用默认值）。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>读取配置文件。</summary>
        public static AppConfig Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
                return new AppConfig();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new AppConfig();
            }
            catch
            {
                // 配置损坏时回退默认（默认 SQLite），避免程序起不来
                return new AppConfig();
            }
        }
    }
}
