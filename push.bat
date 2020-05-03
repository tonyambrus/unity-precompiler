@echo off
set src=https://microsoft.pkgs.visualstudio.com/Analog/_packaging/bondi.experiences.verticalslice/nuget/v3/index.json
forfiles /p nupkg /c "cmd /c nuget push -Source %src% -ApiKey az @path" 
pause