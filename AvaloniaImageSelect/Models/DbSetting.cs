using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;

namespace AvaloniaImageSelect.Models
{
    public class DbSetting
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string? ConfigName { get; set; }
        public string? ConfigValue { get; set; }
        public string? Comment { get; set; }
    }
}
