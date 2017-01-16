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

namespace BlinkyHeadlessCS
{
    public sealed class StartupTask : IBackgroundTask
    {
        BackgroundTaskDeferral deferral;
        private GpioPinValue value = GpioPinValue.High;
        private ThreadPoolTimer timer;

        // Pins for switches and fan transistors  5+6?
        private int[] switchPins = { 5, 9, 11, 22, 10, 17, 27 }; //{ 1+2, 3, 4, 5, 6, 7, 8 }; 
        private GpioPin[] pins = new GpioPin[8];

        private const int MAIN_LED_PIN = 9;
        private GpioPin MainLedPin;

        private const int FOGGER_FAN_PIN = 13;

        private readonly SHT15 _sht15 = new SHT15(24, 23);
        private static int _maximumTemperature = 500;
        private const string I2C_CONTROLLER_NAME = "I2C1";
        private I2cDevice I2CDev;
        private TSL2561 TSL2561Sensor;
        private GpioPin fanPin;

        // TSL Gain and MS Values
        private Boolean Gain = false;
        private uint MS = 0;
        private static double CurrentLux = 0;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();
            InitGPIO();
            timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromMilliseconds(500));
            
        }
        private void InitGPIO()
        {
            GpioController controller = GpioController.GetDefault();
            MainLedPin = controller.OpenPin(MAIN_LED_PIN);

            pin = GpioController.GetDefault().OpenPin(LED_PIN);
            pin.Write(GpioPinValue.High);
            pin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private void Timer_Tick(ThreadPoolTimer timer)
        {
            value = (value == GpioPinValue.High) ? GpioPinValue.Low : GpioPinValue.High;
            Debug.WriteLine("Value is " + value + "at time " + timer.Period);
            
            //pin.Write(value);
        }
    }
}
