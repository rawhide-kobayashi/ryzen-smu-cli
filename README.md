# ryzen-smu-cli

A CLI tool for the ZenStates SMU library. See [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) for compatibility.

All credit to [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) for core functionality and [SMUDebugTool](https://github.com/irusanov/SMUDebugTool) for usage examples.

Usage:
```
.\ryzen-smu-cli.exe --help
Description:
  A CLI for the Ryzen SMU.

Usage:
  ryzen-smu-cli [options]

Options:
  --offset <offset>                Specify a zero-indexed core, or list of cores, and their PBO offset(s), in a fashion
                                   similar to taskset. e.g. 0:-10,1:5,2:-20,14:-25
  --disable-cores <disable-cores>  Specify a zero-indexed list of cores to disable. e.g. 0,1,4,7,12,15. This setting
                                   does not take into account any current core disablement. All cores you wish to
                                   disable must be specified. Any that are unspecified will be enabled. This option
                                   requires a reboot.
  --enable-all-cores               Enable all cores.
  --version                        Show version information
  -?, -h, --help                   Show help and usage information
```