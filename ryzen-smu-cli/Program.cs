using System.CommandLine;
using System.Management;
using ZenStates.Core;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace ryzen_smu_cli
{
    class Program
    {
        private static readonly Cpu ryzen;
        private static readonly Dictionary<int, int> mappedCores;
        static Program()
        {
            if (!IsAdministrator())
            {
                Console.WriteLine("This application must be run as an administrator.");
                Environment.Exit(1);
            }

            try
            {
                ryzen = new Cpu();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("If the previous message was unclear, for some reason, ZenStates-Core failed to initialize an instance of the CPU control object.");
                Environment.Exit(1);
            }

            mappedCores = MapLogicalCoresToPhysical();
        }

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
            if (!IsAdministrator())
            {
                Console.WriteLine("This application must be run as an administrator.");
                Environment.Exit(1);
            }

            var rootCommand = new RootCommand("A CLI for the Ryzen SMU.");

            var pboOffset = new Option<string>("--offset", "Specify a zero-indexed logical core, or list of logical cores, and their PBO offset(s), in a fashion similar to taskset. e.g. 0:-10,1:5,2:-20,14:-25. These are the logical core IDs as they appear in your system, not the true IDs according to fused hardware disabled cores. Alternatively, you may supply a simpler comma-separated list of offset values - e.g. 0,-14,-30,5,-10,-22 - but, obviously, this can only set the value on up to X core that you define.");
            var disableCores = new Option<string>("--disable-cores", "Specify a zero-indexed list of logical cores to disable. e.g. 0,1,4,7,12,15. This setting does not take into account any current core disablement. All cores you wish to disable must be specified. Any that are unspecified will be enabled. This option requires a reboot.");
            var enableCores = new Option<bool>("--enable-all-cores", "Enable all cores.");
            //var toggleJson = new Option<bool>("--json", "Enable json format for informational outputs.");
            var getCurrentPBOTerse = new Option<bool>("--get-offsets-terse", "Print a list of all PBO offsets on logical cores in a simple, comma-separated format, without core identifiers. e.g. -15,0,2,-20. Note that you cannot retrieve the offsets from disabled cores.");
            var getPhysicalCores = new Option<bool>("--get-physical-cores", "Print a list of physical cores, to find out which ones are disabled in <8-core-per-CCD SKUs.");
            var getEnabledCores = new Option<bool>("--get-enabled-cores", "Print a list of logically enabled/disabled cores, and their relationship to the physical cores, inclusive of factory-fused disabled cores.");
            var setPBOScalar = new Option<string>("--set-pbo-scalar", "Sets the PBO scalar. This is a whole number between 1 and 10.");
            var getPBOScalar = new Option<bool>("--get-pbo-scalar", "Get the current PBO scalar.");

            rootCommand.AddOption(pboOffset);
            rootCommand.AddOption(disableCores);
            rootCommand.AddOption(enableCores);
            rootCommand.AddOption(getCurrentPBOTerse);
            rootCommand.AddOption(getPhysicalCores);
            rootCommand.AddOption(getEnabledCores);
            rootCommand.AddOption(setPBOScalar);
            rootCommand.AddOption(getPBOScalar);

            rootCommand.SetHandler((offsetArgs, coreDisableArgs, enableAll, getCurrentPBOTerse, getEnabledCores, getPhysicalCores,
            setPBOScalar, getPBOScalar) =>
            {
                if (!string.IsNullOrEmpty(offsetArgs)) ApplyPBOOffset(offsetArgs);

                if (!string.IsNullOrEmpty(coreDisableArgs)) ApplyDisableCores(coreDisableArgs);

                if (enableAll) ApplyDisableCores();

                if (getCurrentPBOTerse)
                {
                    Console.WriteLine("Current PBO offsets:");
                    string offsetLine = "";
                    bool flagNotifyDisabledCCD = false;
                    for (int i = 0; i < mappedCores.Count; i++)
                    {
                        int mapIndex = i < 8 ? 0 : 1;
                        try
                        {
                            offsetLine += Convert.ToDecimal((int)ryzen.GetPsmMarginSingleCore((uint)(((mapIndex << 8) | ((mappedCores[i] % 8) & 0xF)) << 20)));
                            offsetLine += ",";
                        }

                        catch (InvalidOperationException)
                        {
                            // This will occur if a CCD is disabled through BIOS e.g. with TURBO GAMER MODE.
                            // But it's not a real problem.
                            flagNotifyDisabledCCD = true;
                        }
                    }

                    Console.WriteLine(offsetLine.TrimEnd(','));

                    if (flagNotifyDisabledCCD) Console.WriteLine("You have manually disabled cores. It is not possible to retrieve offset values while they are disabled.");
                }

                if (getPhysicalCores)
                {
                    Console.WriteLine("Fused status of physical cores:");
                    for (var i = 0; i < ryzen.info.topology.physicalCores; i++)
                    {
                        int mapIndex = i < 8 ? 0 : 1;
                        if ((~ryzen.info.topology.coreDisableMap[mapIndex] >> i % 8 & 1) == 0) Console.WriteLine($"Core {i}: Disabled");
                        else Console.WriteLine($"Core {i}: Enabled");
                    }
                }

                if (getEnabledCores)
                {
                    for (var i = 0; i < ryzen.info.topology.physicalCores; i++)
                    {
                        if (mappedCores.ContainsValue(i)) Console.WriteLine($"Core {i}: Enabled");
                        else Console.WriteLine($"Core {i}: Disabled");
                    }
                }

                if (!string.IsNullOrEmpty(setPBOScalar)) ryzen.SetPBOScalar(Convert.ToUInt32(setPBOScalar));
                if (getPBOScalar) Console.WriteLine($"Current PBO scalar: {ryzen.GetPBOScalar()}");
            }, pboOffset, disableCores, enableCores, getCurrentPBOTerse, getEnabledCores, getPhysicalCores, setPBOScalar, getPBOScalar);

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

        private static Dictionary<int, int> MapLogicalCoresToPhysical()
        {
            Dictionary<int, int> mappedCores = [];

            int logicalCoreIter = 0;
            
            for (var i = 0; i < ryzen.info.topology.physicalCores; i++)
            {
                int mapIndex = i < 8 ? 0 : 1;
                if (ryzen.GetPsmMarginSingleCore((uint)(((mapIndex << 8) | ((i % 8) & 0xF)) << 20)) != null)
                {
                    mappedCores.Add(logicalCoreIter, i);
                    logicalCoreIter += 1;
                }
            }

            return mappedCores;
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
            // This checks if the current SKU has a known register for writing PBO offsets
            if (ryzen.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0)
            {
                string validArgFormat = @"^(-?\d{1,2}(,-?\d{1,2})*|\d{1,2}:-?\d{1,2}(,\d{1,2}:-?\d{1,2})*)$";

                if (Regex.IsMatch(offsetArgs, validArgFormat))
                {
                    string[] arg = offsetArgs.Split(',');

                    if (arg.Length <= mappedCores.Count)
                    {
                        for (int i = 0; i < arg.Length; i++) 
                        {
                            // Magic numbers from SMUDebugTool
                            // This does some bitshifting calculations to get the mask for individual cores for chips with up to two CCDs
                            // Support for threadrippers/epyc is theoretically available, if the calculations were expanded, but are untested
                            try
                            {
                                int mapIndex = mappedCores[i] < 8 ? 0 : 1;

                                if (arg[i].Contains(':'))
                                {
                                    int core = Convert.ToInt32(arg[i].Split(':')[0]);
                                    int offset = Convert.ToInt32(arg[i].Split(':')[1]);
                                    ryzen.SetPsmMarginSingleCore((uint)(((mapIndex << 8) | mappedCores[core] % 8 & 0xF) << 20), offset);
                                    Console.WriteLine($"Set logical core {core}, physical core {mappedCores[core]} offset to {offset}!");
                                }

                                else
                                {
                                    ryzen.SetPsmMarginSingleCore((uint)(((mapIndex << 8) | mappedCores[i] % 8 & 0xF) << 20), Convert.ToInt32(arg[i]));
                                    Console.WriteLine($"Set logical core {i}, physical core {mappedCores[i]} offset to {arg[i]}!");
                                }
                            }

                            catch (KeyNotFoundException)
                            {
                                Console.WriteLine($"Tried to set an offset on logical core {Convert.ToInt32(arg[i].Split(':')[0])}, but there are only {mappedCores.Count} (zero-indexed, as a reminder) logical cores active in the system.");
                            }
                        }
                    }

                    else Console.WriteLine("Specified a greater number of offsets than logical cores active in the system. Please check and try again.");
                }

                else Console.WriteLine("Malformed input format for offsets. Please check and try again.");
            }

            else Console.WriteLine("You have attempted to enable PBO offsets on a CPU that does not support them.");
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

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
