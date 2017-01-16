using SQLite.Net.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlinkyHeadlessCS
{
    class SensorReading
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        public double TemperatureF { get; set; }
        public double Humidity { get; set; }
        public double Lux { get; set; }
        public bool FoggerOn { get; set; }
        public bool BoilerOn { get; set; }
    }
}
