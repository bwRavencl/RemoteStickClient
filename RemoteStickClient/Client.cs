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
    class Client
    {
        public static int DEFAULT_PORT = 28789;
        public static int DEFAULT_SERVER_TIMEOUT = 5000;

        public static int PROTOCOL_VERSION = 1;
        public static char PROTOCOL_MESSAGE_DELIMITER = ':';
        public static String PROTOCOL_MESSAGE_CLIENT_HELLO = "CLIENT_HELLO";
	    public static String PROTOCOL_MESSAGE_SERVER_HELLO = "SERVER_HELLO";
	    public static String PROTOCOL_MESSAGE_UPDATE = "UPDATE";
	    public static String PROTOCOL_MESSAGE_UPDATE_REQUEST_ALIVE = PROTOCOL_MESSAGE_UPDATE + "_ALIVE";
	    public static String PROTOCOL_MESSAGE_CLIENT_ALIVE = "CLIENT_ALIVE";

        private static uint DEFAULT_N_CONNECTION_RETRIES = 10;

        private enum ClientState
        {
            Connecting, Connected, Disconnected
        }

        private vJoy joystick;
        private uint id;
        private ClientState clientState = ClientState.Connecting;
        private volatile bool run = true;
        private String address = "192.168.0.108";
        private int port = DEFAULT_PORT;
        private int serverTimeout = DEFAULT_SERVER_TIMEOUT;
        private int updateRate = -1;
        private InputSimulator inputSimulator = new InputSimulator();
        private VirtualKeyCode[] downKeyCodes = new VirtualKeyCode[0];

        public Client(vJoy joystick, uint id)
        {
            this.joystick = joystick;
            this.id = id;
        }

        public void Run()
        {
            UdpClient udpClient = new UdpClient(port);
            udpClient.Client.ReceiveTimeout = serverTimeout;
            try
            {
                Console.WriteLine("Connecting to " + address + ":" + port + "...");

                        udpClient.Connect(address, port);


                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);
                Byte[] sendBytes, receiveBytes;
                string message;
                long counter = -1;
                uint retry = DEFAULT_N_CONNECTION_RETRIES;
                int nButtons = 0;

                while (run)
                {
                    switch (clientState)
                    {
                        case ClientState.Connecting:
                            bool success = false;
                            while (!success && retry > 0)
                            {
                                try
                                {
                                    long maxAxisValue = 0;
                                    joystick.GetVJDAxisMax(id, HID_USAGES.HID_USAGE_X, ref maxAxisValue);

                                    nButtons = joystick.GetVJDButtonNumber(id);

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
                                            updateRate = int.Parse(messageParts[2]);
                                            Console.WriteLine("Server Update Rate: {0} ms", updateRate);
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
                                        Console.WriteLine("Could not open connection to server: Retrying({0})...", DEFAULT_N_CONNECTION_RETRIES - retry);
                                        Thread.Sleep(1000);
                                    }
                                }
                            }

                            if (success)
                            {
                                clientState = ClientState.Connected;
                                Console.WriteLine("Entering State: Connected");
                            }
                            else
                                run = false;

                            break;
                        case ClientState.Connected:
                            receiveBytes = udpClient.Receive(ref ipEndPoint);
                            message = Encoding.ASCII.GetString(receiveBytes);

                            if (message.StartsWith(PROTOCOL_MESSAGE_UPDATE))
                            {
                                string[] messageParts = message.Split(PROTOCOL_MESSAGE_DELIMITER);

                                /*if (messageParts.Length >= 9 + nButtons + 1)
                                {*/
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

                                        joystick.SetAxis(axisX, id, HID_USAGES.HID_USAGE_X);
                                        joystick.SetAxis(axisY, id, HID_USAGES.HID_USAGE_Y);
                                        joystick.SetAxis(axisZ, id, HID_USAGES.HID_USAGE_Z);
                                        joystick.SetAxis(axisRX, id, HID_USAGES.HID_USAGE_RX);
                                        joystick.SetAxis(axisRY, id, HID_USAGES.HID_USAGE_RY);
                                        joystick.SetAxis(axisRZ, id, HID_USAGES.HID_USAGE_RZ);
                                        joystick.SetAxis(axisSL0, id, HID_USAGES.HID_USAGE_SL0);
                                        joystick.SetAxis(axisSL1, id, HID_USAGES.HID_USAGE_SL1);

                                        for (uint i = 1; i <= nButtons; i++)
                                        {
                                            bool button = bool.Parse(messageParts[9 + i]);
                                            joystick.SetBtn(button, id, i);
                                        }

                                        int cursorX = int.Parse(messageParts[10 + nButtons]);
                                        int cursorY = int.Parse(messageParts[11 + nButtons]);

                                        inputSimulator.Mouse.MoveMouseBy(cursorX, cursorY);

                                        int nDownKeyCodes = int.Parse(messageParts[12 + nButtons]);
                                        VirtualKeyCode[] newDownKeyCodes = new VirtualKeyCode[nDownKeyCodes];

                                        for (int i = 0; i < nDownKeyCodes; i++)
                                        {
                                            string keyCodeString = messageParts[13 + nButtons + i];
                                            try
                                            {
                                                VirtualKeyCode keyCode = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), keyCodeString, true);
                                                if (!inputSimulator.InputDeviceState.IsKeyDown(keyCode))
                                                {
                                                    switch(keyCode)
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

                                        int nDownUpKeyStrokes = int.Parse(messageParts[13 + nButtons + nDownKeyCodes]);
                                        for (int i = 0; i < nDownUpKeyStrokes; i++)
                                        {
                                            int nDownUpModifierCodes = int.Parse(messageParts[14 + nButtons + nDownKeyCodes + i]);
                                            VirtualKeyCode[] modifierCodes = new VirtualKeyCode[nDownUpModifierCodes];
                                            for (int j = 0; j < nDownUpModifierCodes; j++)
                                            {
                                                string modifierCodeString = messageParts[15 + nButtons + nDownKeyCodes + i + j];
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

                                            int nDownUpKeyCodes = int.Parse(messageParts[15 + nButtons + nDownKeyCodes + nDownUpModifierCodes + i]);
                                            VirtualKeyCode[] keyCodes = new VirtualKeyCode[nDownUpKeyCodes];
                                            for (int j = 0; j < nDownUpKeyCodes; j++)
                                            {
                                                string keyCodeString = messageParts[16 + nButtons + nDownKeyCodes + nDownUpModifierCodes + i + j];
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
                                    else
                                        Console.WriteLine("Received old packet - ignoring!");
                                /*}
                                else
                                    Console.WriteLine("Received invalid packet - ignoring!");*/
                            }

                            if (message.StartsWith(PROTOCOL_MESSAGE_UPDATE_REQUEST_ALIVE))
                            {
                                //Console.WriteLine("Sending CLIENT_ALIVE...");
                                sendBytes = Encoding.ASCII.GetBytes(PROTOCOL_MESSAGE_CLIENT_ALIVE);
                                udpClient.Send(sendBytes, sendBytes.Length);
                            }

                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                joystick.ResetAll();
                udpClient.Close();
                clientState = ClientState.Disconnected;
                Console.WriteLine("Entering State: Disconnected");
            }
        }
    }
}
