// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.Devices.Gpio;
using Windows.System.Threading;
using System.Diagnostics;
using Blinky;
using Windows.Devices.I2c;
using Windows.Foundation.Diagnostics;
using Windows.Devices.Enumeration;
using SQLite.Net;
using System.IO;
using SQLite.Net.Platform.WinRT;
using Windows.Storage;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Threading;

namespace BlinkyHeadlessCS
{
    public sealed class StartupTask : IBackgroundTask
    {
        BackgroundTaskDeferral deferral;
        private GpioPinValue value = GpioPinValue.High;
        private ThreadPoolTimer timer;
        private Timer _timer;

        #region Switches
        // Pins for switches and fan transistors  5+6?
        //{ 5, 9, 11, 22, 10, 17, 27 }; => { 1+2, 3, 4, 5, 6, 7, 8 };
        private int[] UnusedSwitchPins = { 5, 27 };
        private GpioPin[] UnusedSwitches = new GpioPin[2];

        private const int MAIN_LED_PIN = 9; //3
        private GpioPinValue MainLED_Value;
        private GpioPin MainLED;

        private const int BONSAI_PIN = 11;  //4
        private GpioPinValue Bonsai_Value;
        private GpioPin Bonsai;

        private const int FOGGER_PIN = 22;  //5
        private GpioPinValue Fogger_Value;
        private GpioPin Fogger;

        private const int BOILER_PIN = 10;  //6
        private GpioPinValue Boiler_Value;
        private GpioPin Boiler;

        private const int HEAT_LAMP_PIN = 17;  //7
        private GpioPinValue HeatLamp_Value;
        private GpioPin HeatLamp;

        private const int FOGGER_FAN_PIN = 13;
        private GpioPinValue FoggerFan_Value;
        private GpioPin FoggerFan;
        #endregion

        private const int TEMP_DATA_PIN = 24;
        private const int TEMP_SCK_PIN = 23;
        private SHT15 TempHumiditySensor = null;
        private static double TemperatureF = 0.0;
        private static double Humidity = 0.0;
        private static double _maximumTemperature = 120.0;
        private static double _minimumTemperature = 40.0;
        private static double _maximumHumidity = 100.0;
        private static double _minimumHumidity = 20.0;

        private const string I2C_CONTROLLER_NAME = "I2C1";
        private I2cDevice I2CDev;
        private TSL2561 LightSensor;
        private Boolean Gain = false;
        private uint MS = 0;
        private static double CurrentLux = 0;

        private SensorReading prevReading = null;
        private string path;
        private SQLiteConnection conn;
        private const string ServiceAddress = "http://orchidariumapi20170118080711.azurewebsites.net/api/";
        //private const string ServiceAddress = "http://192.168.0.106:50117/api/";
        private const string DeviceKey = "7f03a2a5-2441-4f7c-91f5-07cf319d323d";

        //LoggingChannel lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
        //lc.LogMessage("I made a message!");


        public void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();
            GetConnection();
            InitGPIO();
            timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromSeconds(10));
            //_timer = new Timer(Timer_Tick, null, 0, 5 * 60 * 1000);
        }

        private void GetConnection()
        {
            path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "db.sqlite");

            conn = new SQLiteConnection(new SQLitePlatformWinRT(), path);

            conn.CreateTable<SensorReading>();
        }

        private void InitGPIO()
        {
            InitializeSwitchesAndFans();
            InitializeI2CDevice();
        }

        private void InitializeSwitchesAndFans()
        {
            GpioController controller = GpioController.GetDefault();
            if (controller == null)
            {
                //pins = null;
                //TODO log issue
                return;
            }
            MainLED = controller.OpenPin(MAIN_LED_PIN);
            Bonsai = controller.OpenPin(BONSAI_PIN);
            Fogger = controller.OpenPin(FOGGER_PIN);
            Boiler = controller.OpenPin(BOILER_PIN);
            HeatLamp = controller.OpenPin(HEAT_LAMP_PIN);
            FoggerFan = controller.OpenPin(FOGGER_FAN_PIN);
            CheckTimeForSwitches();
            MainLED.SetDriveMode(GpioPinDriveMode.Output);
            Bonsai.SetDriveMode(GpioPinDriveMode.Output);
            Fogger.SetDriveMode(GpioPinDriveMode.Output);
            Boiler.SetDriveMode(GpioPinDriveMode.Output);
            HeatLamp.SetDriveMode(GpioPinDriveMode.Output);
            FoggerFan.SetDriveMode(GpioPinDriveMode.Output);

            for (int i = 0; i < UnusedSwitchPins.Length; i++)
            {
                UnusedSwitches[i] = controller.OpenPin(UnusedSwitchPins[i]);
                UnusedSwitches[i].Write(GpioPinValue.Low);
                UnusedSwitches[i].SetDriveMode(GpioPinDriveMode.Output);
            }
        }

        private async void InitializeI2CDevice()
        {
            try
            {
                var settings = new I2cConnectionSettings(TSL2561.TSL2561_ADDR);

                settings.BusSpeed = I2cBusSpeed.FastMode;
                settings.SharingMode = I2cSharingMode.Shared;

                string aqs = I2cDevice.GetDeviceSelector(I2C_CONTROLLER_NAME);  /* Find the selector string for the I2C bus controller                   */
                var dis = await DeviceInformation.FindAllAsync(aqs);            /* Find the I2C bus controller device with our selector string           */

                I2CDev = await I2cDevice.FromIdAsync(dis[0].Id, settings);    /* Create an I2cDevice with our selected bus controller and I2C settings */
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
                return;
            }

            InitializeSensors();
        }

        private void InitializeSensors()
        {
            LightSensor = new TSL2561(ref I2CDev);
            MS = (uint)LightSensor.SetTiming(false, 2);
            LightSensor.PowerUp();

            TempHumiditySensor = new SHT15(TEMP_DATA_PIN, TEMP_SCK_PIN);

            //Debug.WriteLine("TSL2561 ID: " + LightSensor.GetId());
        }

        private async void Timer_Tick(ThreadPoolTimer timer)
        {
            //value = (value == GpioPinValue.High) ? GpioPinValue.Low : GpioPinValue.High;
            //Debug.WriteLine("Value is " + value + "at time " + timer.Period);

            CheckTimeForSwitches();
            GetTempHumSensorReadings();
            GetLuminosityReadings();
            Debug.WriteLine("Temp: " + TemperatureF);
            SensorReading curReading = FillInReading();
            if (ValidReading(curReading) && (prevReading == null || !AreSameReadings(curReading)))
            {
                Debug.WriteLine("Inserting into db");
                InsertValuesIntoDB(curReading);
                await SaveToCloud(curReading);
                prevReading = curReading;
            }
        }

        private bool ValidReading(SensorReading curReading)
        {
            bool valid = true;
            if (curReading.TemperatureF > _maximumTemperature || curReading.TemperatureF < _minimumTemperature)
                valid = false;
            if (curReading.Humidity > _maximumHumidity || curReading.Humidity < _minimumHumidity)
                valid = false;

            return valid;
        }

        private async Task SaveToCloud(SensorReading curReading)
        {
            using (var client = new HttpClient())
            {
                curReading.Id = 0;
                var url = ServiceAddress + "sensorreading";
                var body = JsonConvert.SerializeObject(curReading);
                //_logger.Info("Web reporting client: " + body);
                Debug.WriteLine("Web reporting client: " + body);
                var content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                body = "";

                if (response.Content != null)
                {
                    body = await response.Content.ReadAsStringAsync();
                }

                Debug.WriteLine("Web reporting client: " + response.StatusCode + " " + body);
            }
        }

        private bool AreSameReadings(SensorReading curReading)
        {
            return curReading.BoilerOn == prevReading.BoilerOn &&
                curReading.FoggerOn == prevReading.FoggerOn &&
                curReading.BonsaiOn == prevReading.BonsaiOn &&
                SimilarNumbers(curReading.TemperatureF, prevReading.TemperatureF) &&
                SimilarNumbers(curReading.Humidity, prevReading.Humidity) &&
                SimilarNumbers(curReading.Lux, prevReading.Lux);
        }

        private bool SimilarNumbers(double one, double two)
        {
            double diff = Math.Abs(one - two);
            return diff < 2;
        }

        private SensorReading FillInReading()
        {
            SensorReading reading = new SensorReading();
            reading.TemperatureF = TemperatureF;
            reading.Humidity = Humidity;
            reading.Lux = CurrentLux;
            reading.SoilMoisture = 0;
            reading.FoggerOn = FoggerFan_Value == GpioPinValue.High;
            reading.BoilerOn = Boiler_Value == GpioPinValue.High;
            reading.BonsaiOn = Bonsai_Value == GpioPinValue.High;
            reading.DateAdded = DateTime.Now;

            return reading;
        }

        private void InsertValuesIntoDB(SensorReading reading)
        {
            var success = conn.Insert(reading);
            Debug.WriteLine("Successful db write: " + success);
        }

        private void CheckTimeForSwitches()
        {
            MainLED_Value = IsDayTime() ? GpioPinValue.High : GpioPinValue.Low;
            MainLED.Write(MainLED_Value);

            Bonsai_Value = IsBonsaiWateringTime() ? GpioPinValue.High : GpioPinValue.Low;
            Bonsai.Write(Bonsai_Value);

            Fogger_Value = IsFirstFiveMinutes() ? GpioPinValue.High : GpioPinValue.Low;
            Fogger.Write(Fogger_Value);

            Boiler_Value = IsFirstTenMinutes() ? GpioPinValue.High : GpioPinValue.Low;
            Boiler.Write(Boiler_Value);

            HeatLamp_Value = MainLED_Value;
            HeatLamp.Write(HeatLamp_Value);

            FoggerFan_Value = Fogger_Value;
            FoggerFan.Write(FoggerFan_Value);

            Debug.WriteLine($"Main led is {MainLED_Value}, Fogger is {Fogger_Value}, Boiler is {Boiler_Value}, Bonsai is {Bonsai_Value}");
        }

        private void GetLuminosityReadings()
        {
            uint[] Data = LightSensor.GetData();
            //Debug.WriteLine("Data1: " + Data[0] + ", Data2: " + Data[1]);

            CurrentLux = LightSensor.GetLux(Gain, MS, Data[0], Data[1]);

            String strLux = String.Format("{0:0.00}", CurrentLux);
            String strInfo = "Luminosity: " + strLux + " lux";

            Debug.WriteLine(strInfo);
        }

        private void GetTempHumSensorReadings()
        {
            var rawTemp = TempHumiditySensor.ReadRawTemperature();
            TemperatureF = TempHumiditySensor.CalculateTemperatureF(rawTemp);
            var tempC = TempHumiditySensor.CalculateTemperatureC(rawTemp);
            Humidity = TempHumiditySensor.ReadHumidity(tempC);

            Debug.WriteLine($"Temperature: {TemperatureF} and Humidity: {Humidity}");

            // Check if a warning should be generated 
            //var warning = temperature > _maximumTemperature;
        }




        private bool IsBonsaiWateringTime()
        {
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;
            int day = DateTime.Now.Day - (DateTime.Now.DayOfWeek - DayOfWeek.Wednesday);

            DateTime low = new DateTime(year, month, day, 8, 0, 0, 0);
            DateTime high = new DateTime(year, month, day, 8, 1, 0, 0);
            return low.CompareTo(DateTime.Now) >= 0 && high.CompareTo(DateTime.Now) <= 0;
        }

        private bool IsFirstTenMinutes()
        {
            return DateTime.Now.Minute > 0 && DateTime.Now.Minute < 10;
        }

        private bool IsFirstFiveMinutes()
        {
            return DateTime.Now.Minute > 0 && DateTime.Now.Minute < 5;
        }

        private bool IsDayTime()
        {
            return DateTime.Now.Hour > 8 && DateTime.Now.Hour < 20;
        }

    }
}
