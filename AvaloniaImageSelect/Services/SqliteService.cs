using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AvaloniaImageSelect.Models;
using SQLite;

namespace AvaloniaImageSelect.Services
{
    public class SqliteService
    {
        private string _dbPath = "config.db";
        private SQLiteConnection _sqliteDb;
        public SqliteService()
        {
            _sqliteDb = new SQLiteConnection(_dbPath);
            _sqliteDb.CreateTable<DbSetting>();
        }

        public bool InsertOrUpdateSetting(DbSetting setting)
        {
            if (_sqliteDb.Table<DbSetting>().Any(p => p.ConfigName == setting.ConfigName))
            {
                var existingSetting = _sqliteDb.Table<DbSetting>().FirstOrDefault(p => p.ConfigName == setting.ConfigName);
                setting.Id = existingSetting.Id;
                return _sqliteDb.Update(setting) > 0;
            }
            else
            {
                return _sqliteDb.Insert(setting) > 0;
            }
        }

        public string GetImageFolder()
        {
            return _sqliteDb.Table<DbSetting>()
                .FirstOrDefault(p => p.ConfigName == "ImageFolder")?.ConfigValue ?? string.Empty;
        }

        public string GetDestinationImageFolder()
        {
            return _sqliteDb.Table<DbSetting>()
                .FirstOrDefault(p => p.ConfigName == "DestinationImageFolder")?.ConfigValue ?? string.Empty;
        }

        public bool GetDeleteWhenClose()
        {
            var deleteWhenClose =  _sqliteDb.Table<DbSetting>()
                .FirstOrDefault(p => p.ConfigName == "DeleteWhenClose")?.ConfigValue ?? string.Empty;
            if (!bool.TryParse(deleteWhenClose, out bool result))
            {
                return false;
            }
            return result;
        }
    }
}
