version: 1.0
builds:
  server-build:
    executableName: DedicatedServer.x86_64
    buildPath: Builds/LinuxServer
    excludePaths: []
buildConfigurations:
  build-config:
    build: server-build
    queryType: sqp
    binaryPath: DedicatedServer.x86_64
    commandLine: -nographics -batchmode -port $$port$$ -queryport $$query_port$$ -logFile $$log_dir$$/server.log
    variables: {}
    readiness: true
fleets:
  fleet:
    buildConfigurations:
      - build-config
    regions:
      Asia:
        minAvailable: 1
        maxServers: 2
    usageSettings:
      - hardwareType: CLOUD
        machineType: GCP-N2
        maxServersPerMachine: 4
