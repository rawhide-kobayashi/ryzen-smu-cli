using System.CommandLine;
using System.Management;
using ZenStates.Core;

namespace ryzen_smu_cli
{
    class Program
    {
        private static readonly Cpu ryzen = new();

        private static readonly string wmiAMDACPI = "AMD_ACPI";
        private static readonly string wmiScope = "root\\wmi";
        private static ManagementObject? classInstance;
        private static string? instanceName;
        private static ManagementBaseObject? pack;

        private static List<WmiCmdListItem> availableCommands = [];

        private static bool wmiPopulated = false;
        private static bool rebootFlag = false;

        static int Main(string[] args)
        {
            var rootCommand = new RootCommand("A CLI for the Ryzen SMU.");

            var pboOffset = new Option<string>("--offset", "Specify a zero-indexed core, or list of cores, and their PBO offset(s), in a fashion similar to taskset. e.g. 0:-10,1:5,2:-20,14:-25");
            var disableCores = new Option<string>("--disable-cores", "Specify a zero-indexed list of cores to disable. e.g. 0,1,4,7,12,15. This setting does not take into account any current core disablement. All cores you wish to disable must be specified. Any that are unspecified will be enabled. This option requires a reboot.");
            var enableCores = new Option<bool>("--enable-all-cores", "Enable all cores.");

            rootCommand.AddOption(pboOffset);
            rootCommand.AddOption(disableCores);
            rootCommand.AddOption(enableCores);

            rootCommand.SetHandler((offsetArgs, coreDisableArgs, enableAll) =>
            {
                if (!string.IsNullOrEmpty(offsetArgs)) ApplyPBOOffset(offsetArgs);

                if (!string.IsNullOrEmpty(coreDisableArgs)) ApplyDisableCores(coreDisableArgs);

                if (enableAll) ApplyDisableCores();

                
            }, pboOffset, disableCores, enableCores);

            if (args.Length == 0)
            {
                rootCommand.Invoke("--help");
            }

            else
            {
                rootCommand.Invoke(args);
            }

            ryzen.Dispose();

            if (rebootFlag) Console.WriteLine("A reboot is required for changes to take effect.");

            return 0;
        }

        private static string GetWmiInstanceName()
        {
            try
            {
                instanceName = WMI.GetInstanceName(wmiScope, wmiAMDACPI);
            }
            catch
            {
                // ignored
            }

            return instanceName;
        }

        private static void PopulateWmiFunctions()
        {
            try
            {
                instanceName = GetWmiInstanceName();
                classInstance = new ManagementObject(wmiScope,
                    $"{wmiAMDACPI}.InstanceName='{instanceName}'",
                    null);

                // Get function names with their IDs
                string[] functionObjects = { "GetObjectID", "GetObjectID2" };
                var index = 1;

                foreach (var functionObject in functionObjects)
                {
                    try
                    {
                        pack = WMI.InvokeMethodAndGetValue(classInstance, functionObject, "pack", null, 0);

                        if (pack != null)
                        {
                            var ID = (uint[])pack.GetPropertyValue("ID");
                            var IDString = (string[])pack.GetPropertyValue("IDString");
                            var Length = (byte)pack.GetPropertyValue("Length");

                            for (var i = 0; i < Length; ++i)
                            {
                                if (IDString[i] == "")
                                    break;

                                WmiCmdListItem item = new($"{IDString[i] + ": "}{ID[i]:X8}", ID[i], !IDString[i].StartsWith("Get"));
                                availableCommands.Add(item);
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    index++;
                }
            }
            catch
            {
                // ignored
            }

            rebootFlag = true;
            wmiPopulated = true;
        }

        private static void ApplyPBOOffset(string offsetArgs)
        {
            string[] arg = offsetArgs.Split(',');

            for (int i = 0; i < arg.Length; i++) 
            {
                int core = Convert.ToInt32(arg[i].Split(':')[0]);
                int offset = Convert.ToInt32(arg[i].Split(':')[1]);

                // Magic numbers from SMUDebugTool
                // This does some bitshifting calculations to get the mask for individual cores for chips with up to two CCDs
                // I'm not sure if it would work with more, in theory. It's unclear to me based on the github issues.
                int mapIndex = core < 8 ? 0 : 1;
                if ((~ryzen.info.topology.coreDisableMap[mapIndex] >> core % 8 & 1) == 1)
                {
                    ryzen.SetPsmMarginSingleCore((uint)(((mapIndex << 8) | core % 8 & 0xF) << 20), offset);
                    Console.WriteLine($"Set core {core} offset to {offset}!");
                }

                else
                {
                    Console.WriteLine($"Unable to set offset on disabled core {core}.");
                }
            }
        }

        private static void ApplyDisableCores(string coreArgs = "Enable")
        {
            if (!wmiPopulated) PopulateWmiFunctions();

            // More magic from SMUDebugTool...
            // uintccd2 = 0x8200; ? :)
            uint[] ccds = [0x8000, 0x8100];

            var cmdItem = availableCommands.FirstOrDefault(item => item.text.Contains("Software Downcore Config"));

            if (cmdItem != null)
            {
                for (int i = 0; i < ccds.Length; i++)
                {
                    if (coreArgs != "Enable")
                    {
                        int[] arg = [.. coreArgs.Split(',').Select(int.Parse)];

                        for (int x = 0; x < 8; x++)
                        {
                            if (arg.Contains(x + (i * 8)))
                            {
                                ccds[i] = Utils.SetBits(ccds[i], x, 1, 1);
                            }
                        }
                    }


                    // Unreadable garbage... But it's my unreadable garbage. It just prints the bitmaps in the expected,
                    // human order.
                    Console.WriteLine($"New core disablement bitmap for CCD{i} (reversed lower half): {new string([.. Convert.ToString((int)(ccds[i] & 0xFF), 2).PadLeft(8, '0').Reverse()])}");
                    WMI.RunCommand(classInstance, cmdItem.value, ccds[i]);
                }
            }

            else
            {
                Console.WriteLine("Something has gone terribly wrong, the downcore config option is not present.");
            }            
        }
    }
}
