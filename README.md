# Run Redis as Windows Service

The [original version](https://github.com/redis-windows/redis-windows) is hard to use if exe, config and data folder are different. This repo to improve it.

Build via [dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/install/windows):

```ps1
& dotnet publish -c Release -r win-x64 --sc
```

Install service (run as Admininstrator):

```ps1
$ServiceName = "Redis"
$RedisService = (Get-Command -ErrorAction Stop "RedisService").Path
$RedisServer = (Get-Command -ErrorAction Stop "redis-server.exe").Path
$ConfigFilePath = Resolve-Path -ErrorAction Stop ".\redis.windows.conf"
$WorkDir = "C:\var\redis-server"

# ServiceType=Win32OwnProcess makes the service run as a process.
# LocalService account has limited access and works well.
& sc.exe create $ServiceName binPath= "$RedisService -e $RedisServer -d $WorkDir -c $ConfigFilePath" start= demand type= own obj= "NT AUTHORITY\LocalService" password= ""
if (0 -ne $LASTEXITCODE) {
    throw "sc.exe create exited with code $LASTEXITCODE"
}

& sc.exe qc $ServiceName
if (0 -ne $LASTEXITCODE) {
    throw "sc.exe qc exited with code $LASTEXITCODE"
}

& sc.exe start $ServiceName
if (0 -ne $LASTEXITCODE) {
    throw "sc.exe start exited with code $LASTEXITCODE"
}
```
