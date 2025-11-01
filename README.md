# Run Redis as Windows Service

The [original version](https://github.com/redis-windows/redis-windows) is hard to use if exe, config and data folder are different. This repo to improve it.

Build via [dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/install/windows):

```ps1
& dotnet build -c Release -r win-x64 -p Version=1.0.1
& dotnet publish -c Release -r win-x64 --no-build --sc
```

Now `.\bin\Release\net8.0-windows\win-x64\publish\RedisService.exe` is ready to use.

## Usage

Known wrapper options:

```text
-e | --exe        path to redis-server.exe
-d | --dir        working directory. optional; defaults to current directory
-c | --config     path to redis config file. optional
```

Everything that appear before a standalone `--` is parsed by the wrapper, arguments after `--` are forwarded to redis-server. Notice `--config` is parsed and injected as the first argument if set.

Examples:

```ps1
# Forward custom redis options (port, loglevel, disable protected mode)
sc.exe create $ServiceName binPath= "$RedisService -e $RedisServer -d $WorkDir -c $ConfigFilePath -- --port 6390 --loglevel verbose --protected-mode no"

# key=value style
sc.exe create $ServiceName binPath= "$RedisService -e $RedisServer -d $WorkDir -c $ConfigFilePath -- --maxmemory=256mb --maxmemory-policy allkeys-lru"

# No config file (rely on redis defaults)
sc.exe create $ServiceName binPath= "$RedisService -e $RedisServer -d $WorkDir -- --port 6380"
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

## Similar Projects

- [kcherenkov/redis-windows-service](https://github.com/kcherenkov/redis-windows-service) - Handles signals manually rather than using Microsoft.Extensions.Hosting.WindowsServices.
- [zkteco-home/redis-windows](https://github.com/zkteco-home/redis-windows) - Prebuilt binaries which adds `--service-install` and `--service-uninstall` options to `redis-server.exe`.
- [alfishe/redis-service](https://github.com/alfishe/redis-service) - See its C++ wrapper in `service/` folder.
