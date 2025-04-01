# ryzen-smu-cli

A CLI tool for the ZenStates SMU library. See [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) for compatibility.

Requires [.NET Framework 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

All credit to irusanov, who wrote [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) on which this depends for all meaningful functionality, and [SMUDebugTool](https://github.com/irusanov/SMUDebugTool) for usage examples.

Usage:
```
.\ryzen-smu-cli.exe --help
Description:
  A CLI for the Ryzen SMU.

Usage:
  ryzen-smu-cli [options]

Options:
  --offset <offset>                  Specify a zero-indexed logical core, or list of logical cores, and their PBO offset(s), in a fashion similar to taskset. e.g. 0:-10,1:5,2:-20,14:-25. These are the logical core IDs as they appear in your system, not the true IDs according to fused hardware disabled cores.
  --disable-cores <disable-cores>    Specify a zero-indexed list of logical cores to disable. e.g. 0,1,4,7,12,15. This setting does not take into account any current core disablement. All cores you wish to disable must be specified. Any that are unspecified will be enabled. This option requires a reboot.
  --enable-all-cores                 Enable all cores.
  --get-offsets-terse                Print a list of all PBO offsets on logical cores in a simple, comma-separated format, without core identifiers. e.g. -15,0,2,-20.
  --get-physical-cores               Print a list of physical cores, to find out which ones are disabled in <8-core-per-CCD SKUs.
  --set-pbo-scalar <set-pbo-scalar>  Sets the PBO scalar. This is a whole number between 1 and 10.
  --get-pbo-scalar                   Get the current PBO scalar.
  --version                          Show version information
  -?, -h, --help                     Show help and usage information
```
