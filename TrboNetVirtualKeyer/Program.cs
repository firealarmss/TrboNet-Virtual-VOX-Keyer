using System;
using System.IO.Ports;
using System.Threading.Tasks;
using NAudio.Wave;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.IO;
using System.Diagnostics;

#nullable disable

namespace TrboNetVirtualKeyer
{
    internal class Program
    {
        static string comPort = "COM1";
        static bool isVoxEnabled = true;
        static float voxThreshold = 0.1f;
        static float voxHysteresis = 0.02f;
        static int selectedDeviceIndex = 0;
        static bool isPttActive = false;
        static int voxHangTime = 3000;
        static int debounceTime = 500;
        static DateTime lastBelowThresholdTime = DateTime.MinValue;
        static bool isWithinHangTime = false;

        static Config config;
        static SerialPort serialPort;

        static void Main(string[] args)
        {
            string configFilePath = "config.yml";
            if (args.Length > 0 && args[0] == "-c" && args.Length > 1)
            {
                configFilePath = args[1];
            }

            LoadConfiguration(configFilePath);

            try
            {
                serialPort.Open();

                Task.Factory.StartNew(ListenForPtt);

                Task.Factory.StartNew(VoiceDetection);

                while (true)
                {
                    string input = Console.ReadLine();

                    switch (input?.ToUpper())
                    {
                        case "?":
                            Console.WriteLine("\n" +
                                "Commands:\n" +
                                "   K (Keyup)\n" +
                                "   U (Unkey)\n" +
                                "   VOX ON (Turn vox on)\n" +
                                "   VOX OFF (Turn vox off)\n" +
                                "   THRESHOLD (VOX Threashold)\n" +
                                "   LIST DEVICES (List audio input devices)\n" +
                                "   SET DEVICE (Set audio devices)\n" +
                                "\n");
                            break;
                        case "K":
                            SetPttState(true);
                            break;
                        case "U":
                            SetPttState(false);
                            break;
                        case "VOX ON":
                            isVoxEnabled = true;
                            Console.WriteLine("VOX enabled.");
                            break;
                        case "VOX OFF":
                            isVoxEnabled = false;
                            Console.WriteLine("VOX disabled.");
                            break;
                        case "THRESHOLD":
                            Console.WriteLine("Enter new VOX threshold (0.0 - 1.0):");
                            if (float.TryParse(Console.ReadLine(), out float threshold))
                            {
                                voxThreshold = threshold;
                                Console.WriteLine($"VOX threshold set to {voxThreshold}");
                            }
                            else
                            {
                                Console.WriteLine("Invalid threshold.");
                            }
                            break;
                        case "LIST DEVICES":
                            ListAudioDevices();
                            break;
                        case "SET DEVICE":
                            Console.WriteLine("Enter the device index to select:");
                            if (int.TryParse(Console.ReadLine(), out int deviceIndex))
                            {
                                if (deviceIndex >= 0 && deviceIndex < WaveInEvent.DeviceCount)
                                {
                                    selectedDeviceIndex = deviceIndex;
                                    Console.WriteLine($"Audio device set to {WaveInEvent.GetCapabilities(deviceIndex).ProductName}");
                                }
                                else
                                {
                                    Console.WriteLine("Invalid device index.");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Invalid input.");
                            }
                            break;
                        default:
                            Console.WriteLine("Unknown command. Type ? for help.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                serialPort.Close();
            }
        }

        private static async void ListenForPtt()
        {
            bool lastPttState = false;
            while (serialPort.IsOpen)
            {
                bool currentPttState = serialPort.CDHolding;

                if (currentPttState != lastPttState)
                {
                    if (currentPttState)
                    {
                        Console.WriteLine("PTT High");
                    }
                    else
                    {
                        Console.WriteLine("PTT Low");
                    }
                    lastPttState = currentPttState;
                }

                await Task.Delay(500);
            }
        }

        private static void VoiceDetection()
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.DeviceNumber = selectedDeviceIndex;
                waveIn.WaveFormat = new WaveFormat(8000, 1);
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.StartRecording();

                while (serialPort.IsOpen)
                {
                    Task.Delay(1000).Wait();
                }
            }
        }

        private static void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!isVoxEnabled) return;

            float maxVolume = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i + 0]);
                float sample32 = sample / 32768f;
                if (sample32 > maxVolume) maxVolume = sample32;
            }

            if (maxVolume > (voxThreshold + voxHysteresis))
            {
                SetPttState(true);
                isWithinHangTime = false;
            }
            else if (maxVolume < (voxThreshold - voxHysteresis))
            {
                if (!isWithinHangTime)
                {
                    lastBelowThresholdTime = DateTime.Now;
                    isWithinHangTime = true;
                }

                if (DateTime.Now.Subtract(lastBelowThresholdTime).TotalMilliseconds >= voxHangTime + debounceTime)
                {
                    SetPttState(false);
                }
            }
        }

        private static void SetPttState(bool state)
        {
            if (isPttActive != state)
            {
                isPttActive = state;
                serialPort.DtrEnable = state;

                if (state)
                {
                    Console.WriteLine("COR set high.");
                }
                else
                {
                    Console.WriteLine("COR set low.");
                }
            }
        }

        private static void ListAudioDevices()
        {
            Console.WriteLine("Available audio devices:");
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var deviceInfo = WaveInEvent.GetCapabilities(i);
                Console.WriteLine($"{i}: {deviceInfo.ProductName}");
            }
        }

        private static void LoadConfiguration(string configFilePath)
        {
            if (!File.Exists(configFilePath))
            {
                Console.WriteLine($"Configuration file {configFilePath} not found!");
                serialPort = new SerialPort(comPort, 9600);
                return;
            }

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                using (var reader = new StreamReader(configFilePath))
                {
                    config = deserializer.Deserialize<Config>(reader);
                }

                isVoxEnabled = config.IsVoxEnabled;
                voxThreshold = config.VoxThreshold;
                voxHangTime = config.VoxHangTime;
                debounceTime = config.DebounceTime;
                selectedDeviceIndex = config.SelectedDeviceIndex;
                comPort = config.ComPort;
                serialPort = new SerialPort(comPort, 9600);

                Console.WriteLine("Configuration loaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load configuration: {ex.Message}");
            }
        }
    }
}
