[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CompressionType=MSZIP
RebootMode=N
TargetName=packaging\HubPilot\Output\KandooHub-Setup.exe
FrozenTargetName=KandooHub-Setup.exe
PostInstallCmd=<path>\Start-KandooHub.ps1
SourceFiles=0

[Strings]
