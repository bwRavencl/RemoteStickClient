using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using vJoyInterfaceWrap;
using WindowsInput;
using WindowsInput.Native;

namespace RemoteStickClient
{
    class Program
    {
        public static String APPLICATION_NAME = "RemoteStick Client";
        public static String CONSOLE_TITLE_DISCONNECTED = APPLICATION_NAME + " - Disconnected";
        public static uint JVOY_DEVICE_ID = 1;
        public static int DEFAULT_PORT = 28789;
        public static int SERVER_TIMEOUT = 5000;
        public static uint N_CONNECTION_RETRIES = 10;

        public static int PROTOCOL_VERSION = 1;
        public static char PROTOCOL_MESSAGE_DELIMITER = ':';
        public static String PROTOCOL_MESSAGE_CLIENT_HELLO = "CLIENT_HELLO";
        public static String PROTOCOL_MESSAGE_SERVER_HELLO = "SERVER_HELLO";
        public static String PROTOCOL_MESSAGE_UPDATE = "UPDATE";
        public static String PROTOCOL_MESSAGE_UPDATE_REQUEST_ALIVE = PROTOCOL_MESSAGE_UPDATE + "_ALIVE";
        public static String PROTOCOL_MESSAGE_CLIENT_ALIVE = "CLIENT_ALIVE";

        private enum ClientState
        {
            Connecting, Connected
        }

        static void Main(string[] args)
        {
            string input = null;
            if (args.Length > 0)
                input = args[0];

            Console.Title = CONSOLE_TITLE_DISCONNECTED;

            vJoy joystick = new vJoy();

            if (!joystick.vJoyEnabled())
            {
                Console.WriteLine("vJoy driver not enabled: Failed Getting vJoy attributes.\n");
                return;
            }
            else
                Console.WriteLine("vJoy driver:\n\tVendor: {0}\n\tProduct: {1}\n\tVersion Number: {2}\n", joystick.GetvJoyManufacturerString(), joystick.GetvJoyProductString(), joystick.GetvJoySerialNumberString());

            VjdStat status = joystick.GetVJDStatus(JVOY_DEVICE_ID);
            switch (status)
            {
                case VjdStat.VJD_STAT_OWN:
                    Console.WriteLine("vJoy device {0} is already owned by this feeder\n", JVOY_DEVICE_ID);
                    break;
                case VjdStat.VJD_STAT_FREE:
                    Console.WriteLine("vJoy device {0} is available\n", JVOY_DEVICE_ID);
                    break;
                case VjdStat.VJD_STAT_BUSY:
                    Console.WriteLine("vJoy device {0} is already owned by another feeder\nCannot continue\n", JVOY_DEVICE_ID);
                    return;
                case VjdStat.VJD_STAT_MISS:
                    Console.WriteLine("vJoy device {0} is not installed or disabled\nCannot continue\n", JVOY_DEVICE_ID);
                    return;
                default:
                    Console.WriteLine("vJoy device {0} general error\nCannot continue\n", JVOY_DEVICE_ID);
                    return;
            };

            bool hasAxisX = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_X);
            bool hasAxisY = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_Y);
            bool hasAxisZ = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_Z);
            bool hasAxisRX = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RX);
            bool hasAxisRY = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RY);
            bool hasAxisRZ = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RZ);
            bool hasAxisSL0 = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_SL0);
            bool hasAxisSL1 = joystick.GetVJDAxisExist(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_SL1);
            if (!(hasAxisX && hasAxisY && hasAxisZ && hasAxisRX && hasAxisRY && hasAxisRZ && hasAxisSL0 && hasAxisSL1))
            {
                Console.WriteLine("Error: RemoteStick requires a vJoy device with all axis enabled. vJoy device {0} is missing the following axis:", JVOY_DEVICE_ID);
                if (!hasAxisX)
                    Console.WriteLine("\tX");
                if (!hasAxisY)
                    Console.WriteLine("\tY");
                if (!hasAxisZ)
                    Console.WriteLine("\tZ");
                if (!hasAxisRX)
                    Console.WriteLine("\tRX");
                if (!hasAxisRY)
                    Console.WriteLine("\tRY");
                if (!hasAxisRZ)
                    Console.WriteLine("\tRZ");
                if (!hasAxisSL0)
                    Console.WriteLine("\tSL0");
                if (!hasAxisSL1)
                    Console.WriteLine("\tSL1");
                Console.WriteLine("Please add the missing axis using the 'Configure vJoy' application.\n");
                return;
            }

            if ((status == VjdStat.VJD_STAT_OWN) || ((status == VjdStat.VJD_STAT_FREE) && (!joystick.AcquireVJD(JVOY_DEVICE_ID))))
            {
                Console.WriteLine("Failed to acquire vJoy device number {0}.\n", JVOY_DEVICE_ID);
                return;
            }
            else
                Console.WriteLine("Acquired vJoy device {0}.\n", JVOY_DEVICE_ID);

            do
            {
                joystick.ResetVJD(JVOY_DEVICE_ID);

                bool validAddress = false;
                string host = null;
                int port = DEFAULT_PORT;
                do
                {
                    if (input == null)
                    {
                        Console.Write("Server Address: ");
                        input = Console.ReadLine();
                        Console.WriteLine();
                    }

                    int colonIndex = input.IndexOf(':');
                    if (colonIndex != -1)
                    {
                        try
                        {
                            port = int.Parse(input.Substring(colonIndex + 1));

                            if (port >= 1024 && port <= 65535)
                                host = input.Substring(0, colonIndex);
                        }
                        catch (Exception e) { }
                    }
                    else
                        host = input;

                    if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                    {
                        Console.WriteLine("'{0}' is not a valid server address. Please retry!\n", input);

                        input = null;
                        host = null;
                        port = DEFAULT_PORT;
                    }
                    else
                        validAddress = true;
                } while (!validAddress);

                UdpClient udpClient = new UdpClient(port);
                udpClient.Client.ReceiveTimeout = SERVER_TIMEOUT;
                try
                {
                    Console.WriteLine("Connecting to " + host + ":" + port + "...");
                    udpClient.Connect(host, port);

                    IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);
                    Byte[] sendBytes, receiveBytes;
                    string message;
                    long counter = -1;
                    uint retry = N_CONNECTION_RETRIES;
                    int nButtons = 0;

                    bool run = true;
                    ClientState clientState = ClientState.Connecting;
                    InputSimulator inputSimulator = new InputSimulator();
                    VirtualKeyCode[] downKeyCodes = new VirtualKeyCode[0];

                    while (run)
                    {
                        switch (clientState)
                        {
                            case ClientState.Connecting:
                                bool success = false;
                                long updateRate = 0L;
                                while (!success && retry > 0)
                                {
                                    try
                                    {
                                        long maxAxisValue = 0;
                                        joystick.GetVJDAxisMax(JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_X, ref maxAxisValue);

                                        nButtons = joystick.GetVJDButtonNumber(JVOY_DEVICE_ID);

                                        sendBytes = Encoding.ASCII.GetBytes(PROTOCOL_MESSAGE_CLIENT_HELLO + PROTOCOL_MESSAGE_DELIMITER + maxAxisValue + PROTOCOL_MESSAGE_DELIMITER + nButtons);
                                        udpClient.Send(sendBytes, sendBytes.Length);

                                        receiveBytes = udpClient.Receive(ref ipEndPoint);
                                        message = Encoding.ASCII.GetString(receiveBytes);

                                        if (message.StartsWith(PROTOCOL_MESSAGE_SERVER_HELLO))
                                        {
                                            string[] messageParts = message.Split(PROTOCOL_MESSAGE_DELIMITER);
                                            int serverProtocolVersion = int.Parse(messageParts[1]);
                                            if (PROTOCOL_VERSION != serverProtocolVersion)
                                            {
                                                retry = 0;
                                                Console.WriteLine("Error protocol version mismatch:\n\tClient: {0}\n\tServer: {1}", PROTOCOL_VERSION, serverProtocolVersion);
                                            }
                                            else
                                            {
                                                updateRate = long.Parse(messageParts[2]);
                                                success = true;
                                            }
                                        }
                                        else
                                            retry--;
                                    }
                                    catch (SocketException e)
                                    {
                                        if (retry > 0)
                                        {
                                            retry--;
                                            Console.WriteLine("Could not open connection to server: Retrying({0})...", N_CONNECTION_RETRIES - retry);
                                            Thread.Sleep(1000);
                                        }
                                    }
                                }

                                if (success)
                                {
                                    clientState = ClientState.Connected;
                                    Console.WriteLine("\nConnection established!\nUpdate Rate: {0}", updateRate);
                                    Console.Title = Program.APPLICATION_NAME + " - " + host + ':' + port;
                                }
                                else
                                {
                                    run = false;
                                    input = null;
                                }

                                break;
                            case ClientState.Connected:
                                receiveBytes = udpClient.Receive(ref ipEndPoint);
                                message = Encoding.ASCII.GetString(receiveBytes);

                                if (message.StartsWith(PROTOCOL_MESSAGE_UPDATE))
                                {
                                    string[] messageParts = message.Split(PROTOCOL_MESSAGE_DELIMITER);

                                    long newCounter = long.Parse(messageParts[1]);

                                    if (newCounter > counter)
                                    {
                                        int axisX = int.Parse(messageParts[2]);
                                        int axisY = int.Parse(messageParts[3]);
                                        int axisZ = int.Parse(messageParts[4]);
                                        int axisRX = int.Parse(messageParts[5]);
                                        int axisRY = int.Parse(messageParts[6]);
                                        int axisRZ = int.Parse(messageParts[7]);
                                        int axisSL0 = int.Parse(messageParts[8]);
                                        int axisSL1 = int.Parse(messageParts[9]);

                                        joystick.SetAxis(axisX, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_X);
                                        joystick.SetAxis(axisY, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_Y);
                                        joystick.SetAxis(axisZ, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_Z);
                                        joystick.SetAxis(axisRX, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RX);
                                        joystick.SetAxis(axisRY, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RY);
                                        joystick.SetAxis(axisRZ, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_RZ);
                                        joystick.SetAxis(axisSL0, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_SL0);
                                        joystick.SetAxis(axisSL1, JVOY_DEVICE_ID, HID_USAGES.HID_USAGE_SL1);

                                        for (uint i = 1; i <= nButtons; i++)
                                        {
                                            bool button = bool.Parse(messageParts[9 + i]);
                                            joystick.SetBtn(button, JVOY_DEVICE_ID, i);
                                        }

                                        int cursorX = int.Parse(messageParts[10 + nButtons]);
                                        int cursorY = int.Parse(messageParts[11 + nButtons]);
                                        inputSimulator.Mouse.MoveMouseBy(cursorX, cursorY);

                                        int scrollClicks = int.Parse(messageParts[12 + nButtons]);
                                        inputSimulator.Mouse.VerticalScroll(scrollClicks);

                                        int nDownKeyCodes = int.Parse(messageParts[13 + nButtons]);
                                        VirtualKeyCode[] newDownKeyCodes = new VirtualKeyCode[nDownKeyCodes];

                                        for (int i = 0; i < nDownKeyCodes; i++)
                                        {
                                            string keyCodeString = messageParts[14 + nButtons + i];
                                            try
                                            {
                                                VirtualKeyCode keyCode = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), keyCodeString, true);
                                                if (!inputSimulator.InputDeviceState.IsKeyDown(keyCode))
                                                {
                                                    switch (keyCode)
                                                    {
                                                        case VirtualKeyCode.LBUTTON:
                                                            inputSimulator.Mouse.LeftButtonDown();
                                                            break;
                                                        case VirtualKeyCode.RBUTTON:
                                                            inputSimulator.Mouse.RightButtonDown();
                                                            break;
                                                        default:
                                                            inputSimulator.Keyboard.KeyDown(keyCode);
                                                            break;
                                                    }
                                                }

                                                newDownKeyCodes[i] = keyCode;
                                            }
                                            catch (ArgumentException e)
                                            {
                                                Console.WriteLine(e.ToString());
                                            }
                                        }

                                        IEnumerable<VirtualKeyCode> upKeyCodes = downKeyCodes.Except(newDownKeyCodes);
                                        foreach (VirtualKeyCode k in upKeyCodes)
                                        {
                                            if (inputSimulator.InputDeviceState.IsKeyDown(k))
                                            {
                                                switch (k)
                                                {
                                                    case VirtualKeyCode.LBUTTON:
                                                        inputSimulator.Mouse.LeftButtonUp();
                                                        break;
                                                    case VirtualKeyCode.RBUTTON:
                                                        inputSimulator.Mouse.RightButtonUp();
                                                        break;
                                                    default:
                                                        inputSimulator.Keyboard.KeyUp(k);
                                                        break;
                                                }
                                            }
                                        }

                                        downKeyCodes = newDownKeyCodes;

                                        int nDownUpKeyStrokes = int.Parse(messageParts[14 + nButtons + nDownKeyCodes]);
                                        for (int i = 0; i < nDownUpKeyStrokes; i++)
                                        {
                                            int nDownUpModifierCodes = int.Parse(messageParts[15 + nButtons + nDownKeyCodes + i]);
                                            VirtualKeyCode[] modifierCodes = new VirtualKeyCode[nDownUpModifierCodes];
                                            for (int j = 0; j < nDownUpModifierCodes; j++)
                                            {
                                                string modifierCodeString = messageParts[16 + nButtons + nDownKeyCodes + i + j];
                                                try
                                                {
                                                    modifierCodes[j] = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), modifierCodeString, true);
                                                }
                                                catch (ArgumentException e)
                                                {
                                                    modifierCodes[j] = 0;
                                                    Console.WriteLine(e.ToString());
                                                }
                                            }

                                            int nDownUpKeyCodes = int.Parse(messageParts[16 + nButtons + nDownKeyCodes + nDownUpModifierCodes + i]);
                                            VirtualKeyCode[] keyCodes = new VirtualKeyCode[nDownUpKeyCodes];
                                            for (int j = 0; j < nDownUpKeyCodes; j++)
                                            {
                                                string keyCodeString = messageParts[17 + nButtons + nDownKeyCodes + nDownUpModifierCodes + i + j];
                                                try
                                                {
                                                    keyCodes[j] = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), keyCodeString, true);
                                                }
                                                catch (ArgumentException e)
                                                {
                                                    keyCodes[j] = 0;
                                                    Console.WriteLine(e.ToString());
                                                }
                                            }

                                            inputSimulator.Keyboard.ModifiedKeyStroke(modifierCodes, keyCodes);
                                        }

                                        counter = newCounter;
                                    }
                                }

                                if (message.StartsWith(PROTOCOL_MESSAGE_UPDATE_REQUEST_ALIVE))
                                {
                                    sendBytes = Encoding.ASCII.GetBytes(PROTOCOL_MESSAGE_CLIENT_ALIVE);
                                    udpClient.Send(sendBytes, sendBytes.Length);
                                }

                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("\n{0}", e.ToString());
                }
                finally
                {
                    joystick.ResetAll();
                    udpClient.Close();

                    Console.Title = CONSOLE_TITLE_DISCONNECTED;
                    Console.WriteLine("\nThe connection to the server has been terminated.\n");
                }
            } while (true);
        }
    }
}
