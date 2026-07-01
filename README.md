# Run Redis as Windows Service

The [original version](https://github.com/redis-windows/redis-windows) is hard to use if exe, config and data folder are different. This repo to improve it.

The wrapper is a .NET Worker Service using `Microsoft.Extensions.Hosting.WindowsServices`. It launches `redis-server.exe` as a child process, logs to the Windows Event Log, and **stops itself if redis-server exits** so the SCM can apply its recovery policy instead of reporting a healthy service with a dead Redis.

The project multi-targets `net8.0-windows` (LTS) and `net10.0-windows` (LTS).

Build via [dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/install/windows):

```ps1
# Build/test the whole solution
& dotnet build -c Release
& dotnet test

# Publish a single-file, self-contained, trimmed exe for one framework
& dotnet publish RedisService.csproj -c Release -r win-x64 -f net10.0-windows -p:Version=1.0.1
```

The published exe lands at `.\bin\Release\<framework>\win-x64\publish\RedisService.exe` (e.g. `net10.0-windows`).

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

Install service (run as Administrator):

The repo ships helper scripts in [`scripts/`](scripts/). Copy the published `RedisService.exe` next to them (or pass `-RedisService`) and run:

```ps1
# Install and start (see script header for all parameters)
.\scripts\install-service.ps1 -WorkDir 'C:\var\redis-server' -ConfigFilePath '.\redis.windows.conf'

# Remove
.\scripts\uninstall-service.ps1 -ServiceName 'Redis'
```

Or install manually with `sc.exe`:

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

## Design notes

Why the wrapper is shaped the way it is:

- **`BackgroundService` + `AddWindowsService`** instead of hand-written SCM signal handling. The hosting framework already maps service start/stop to `StartAsync`/`StopAsync` correctly; reimplementing it is error-prone (see the `kcherenkov` project below).
- **Self-stop on child exit.** If `redis-server` dies, the wrapper calls `StopApplication()` so the SCM sees the service stop and can apply its configured recovery (`sc failure`). Otherwise Windows would report a healthy service with a dead Redis.
- **Start failure is a startup failure.** A missing/unstartable `redis-server.exe` throws out of `StartAsync`, which the SCM reports as a failed start — not a silent no-op.
- **Graceful stop, then kill.** On stop the wrapper sends a console Ctrl-C to redis (via `AttachConsole` + `GenerateConsoleCtrlEvent`), which redis handles like SIGINT — flushing and persisting before it exits. If it does not exit within the timeout, the whole process tree is killed as a fallback.
- **No host command-line args / config layer.** Configuration is redis's job (via `-c` and forwarded options); the wrapper stays a thin supervisor.

Deliberate non-goals: health checks and an `appsettings` layer.

## Alternative: NSSM

If you would rather converge your services on a single off-the-shelf supervisor, [NSSM](https://nssm.cc) can run `redis-server.exe` directly. Its `AppStopMethodConsole` gives the same graceful Ctrl-C stop, plus built-in restart throttling and stdout/stderr log rotation. The only thing it does not do is translate the cygwin config path — [`scripts/install-service-nssm.ps1`](scripts/install-service-nssm.ps1) pre-computes it (pass `-NoCygwinPath` if your `redis-server` accepts native Windows paths). The trade-off versus this wrapper is a ~1 MB binary with no bundled runtime, at the cost of maintaining the wrapper's clean path-translation encapsulation.

## Similar Projects

- [kcherenkov/redis-windows-service](https://github.com/kcherenkov/redis-windows-service) - Handles signals manually rather than using Microsoft.Extensions.Hosting.WindowsServices.
- [zkteco-home/redis-windows](https://github.com/zkteco-home/redis-windows) - Prebuilt binaries which adds `--service-install` and `--service-uninstall` options to `redis-server.exe`.
- [alfishe/redis-service](https://github.com/alfishe/redis-service) - See its C++ wrapper in `service/` folder.
