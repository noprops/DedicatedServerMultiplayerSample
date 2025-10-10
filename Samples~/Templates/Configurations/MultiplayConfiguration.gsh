version: 1.0
builds:
  dedicated-server-sample:
    executableName: DedicatedServerMultiplayerSample.x86_64
    buildPath: Builds/LinuxServer
    excludePaths: []
buildConfigurations:
  dedicated-server-config:
    build: dedicated-server-sample
    queryType: sqp
    binaryPath: DedicatedServerMultiplayerSample.x86_64
    commandLine: -nographics -batchmode -port $$port$$ -queryport $$query_port$$ -logFile $$log_dir$$/server.log
    variables: {}
    readiness: true
fleets:
  dedicated-server-fleet:
    buildConfigurations:
      - dedicated-server-config
    regions:
      Asia:
        minAvailable: 1
        maxServers: 2
    usageSettings:
      - hardwareType: CLOUD
        machineType: GCP-N2
        maxServersPerMachine: 4
