using System.CommandLine;
using ZenStates.Core;

class Program
{
    private static readonly Cpu ryzen = new();

    static int Main(string[] args)
    {
        

        var rootCommand = new RootCommand("A CLI for the Ryzen SMU.");

        var pboOffset = new Option<string>("--offset", "Specify a core, or list of cores, and their PBO offset(s).");

        rootCommand.AddOption(pboOffset);
        // How to format this command and process it is to be decided.
        rootCommand.SetHandler((core_values) =>
        {
            Console.WriteLine(core_values);
        }, pboOffset);

        // works
        ApplySingleCorePBOOffset(0, -15);

        return rootCommand.Invoke(args);
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
        }
    }
}
