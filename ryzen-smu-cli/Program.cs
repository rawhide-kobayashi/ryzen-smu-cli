using System.CommandLine;
using ZenStates.Core;

class Program
{
    private static readonly Cpu ryzen = new();

    static int Main(string[] args)
    {
        

        var rootCommand = new RootCommand("A CLI for the Ryzen SMU.");

        var pboOffset = new Option<string>("--offset", "Specify a core, or list of cores, and their PBO offset(s), in a fashion similar to taskset. e.g. 0:-10,1:5,2:-20,14:-25");

        rootCommand.AddOption(pboOffset);
        
        rootCommand.SetHandler((offsetArgs) =>
        {
            RunPBOOffset(offsetArgs);
        }, pboOffset);

        return rootCommand.Invoke(args);
    }

    private static void RunPBOOffset(string offsetArgs)
    {
        string[] arg = offsetArgs.Split(',');

        for (int i = 0; i < arg.Length; i++)
        {
            ApplySingleCorePBOOffset(Convert.ToInt32(arg[i].Split(':')[0]), Convert.ToInt32(arg[i].Split(':')[1]));
        }
    }

    private static void ApplySingleCorePBOOffset(int coreNumber, int value)
    {
        // Magic numbers from SMUDebugTool
        // This does some bitshifting calculations to get the mask for individual cores for chips with up to two CCDs
        // I'm not sure if it would work with more, in theory. It's unclear to me based on the github issues.
        int mapIndex = coreNumber < 8 ? 0 : 1;
        if ((~ryzen.info.topology.coreDisableMap[mapIndex] >> coreNumber % 8 & 1) == 1)
        {
            ryzen.SetPsmMarginSingleCore((uint)(((mapIndex << 8) | coreNumber % 8 & 0xF) << 20), value);
            Console.WriteLine($"Set core {coreNumber} to offset {value}!");
        }
    }
}
