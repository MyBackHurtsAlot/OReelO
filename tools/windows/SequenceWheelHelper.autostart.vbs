Set shell = CreateObject("WScript.Shell")
helper = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\SequenceWheel\SequenceWheelHelper.exe")
shell.Run Chr(34) & helper & Chr(34), 0, False
