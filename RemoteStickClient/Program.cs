using System;
using System.Threading;

using vJoyInterfaceWrap;

namespace RemoteStickClient
{
    class Program
    {
        //public static uint N_BUTTONS = 32;

        private static uint id = 1;

        static void Main(string[] args)
        {
            vJoy joystick = new vJoy();
            vJoyInterfaceWrap.vJoy.JoystickState iReport = new vJoy.JoystickState();

            if (!joystick.vJoyEnabled())
            {
                Console.WriteLine("vJoy driver not enabled: Failed Getting vJoy attributes.\n");
                return;
            }
            else
                Console.WriteLine("vJoy driver:\n\tVendor: {0}\n\tProduct :{1}\n\tVersion Number:{2}\n", joystick.GetvJoyManufacturerString(), joystick.GetvJoyProductString(), joystick.GetvJoySerialNumberString());

            VjdStat status = joystick.GetVJDStatus(id);
            switch (status)
            {
                case VjdStat.VJD_STAT_OWN:
                    Console.WriteLine("vJoy device {0} is already owned by this feeder\n", id);
                    break;
                case VjdStat.VJD_STAT_FREE:
                    Console.WriteLine("vJoy device {0} is available\n", id);
                    break;
                case VjdStat.VJD_STAT_BUSY:
                    Console.WriteLine("vJoy device {0} is already owned by another feeder\nCannot continue\n", id);
                    return;
                case VjdStat.VJD_STAT_MISS:
                    Console.WriteLine("vJoy device {0} is not installed or disabled\nCannot continue\n", id);
                    return;
                default:
                    Console.WriteLine("vJoy device {0} general error\nCannot continue\n", id);
                    return;
            };

            bool axisX = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_X);
            bool axisY = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_Y);
            bool axisZ = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_Z);
            bool axisRX = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RX);
            bool axisRY = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RY);
            bool axisRZ = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RZ);
            bool axisSL0 = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_SL0);
            bool axisSL1 = joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_SL1);
            if (!(axisX && axisY && axisZ && axisRX && axisRY && axisRZ && axisSL0 && axisSL1))
            {
                Console.WriteLine("Error: RemoteStick requires a vJoy device with all axis enabled. vJoy device {0} is missing the following axis:", id);
                if (!axisX)
                    Console.WriteLine("\tX");
                if (!axisY)
                    Console.WriteLine("\tY");
                if (!axisZ)
                    Console.WriteLine("\tZ");
                if (!axisRX)
                    Console.WriteLine("\tRX");
                if (!axisRY)
                    Console.WriteLine("\tRY");
                if (!axisRZ)
                    Console.WriteLine("\tRZ");
                if (!axisSL0)
                    Console.WriteLine("\tSL0");
                if (!axisSL1)
                    Console.WriteLine("\tSL1");
                Console.WriteLine("Please add the missing axis using the 'Configure vJoy' application.\n");
                return;
            }

            /*int nButtons = joystick.GetVJDButtonNumber(id);
            if (nButtons < N_BUTTONS)
            {
                Console.WriteLine("Error: RemoteStick requires at least {0} buttons. vJoy device {1} has only {2} buttons.\nPlease increase the number of buttons using the 'Configure vJoy' application.\n", nButtons, id, nButtons);
                return;
            }*/


            if ((status == VjdStat.VJD_STAT_OWN) || ((status == VjdStat.VJD_STAT_FREE) && (!joystick.AcquireVJD(id))))
            {
                Console.WriteLine("Failed to acquire vJoy device number {0}.\n", id);
                return;
            }
            else
                Console.WriteLine("Acquired vJoy device {0}.\n", id);

            joystick.ResetVJD(id);

            Client client = new Client(joystick, id);
            Thread clientThread = new Thread(client.Run);

            clientThread.Start();
            Console.ReadKey();
        }
    }
}
