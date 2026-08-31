FROM mcr.microsoft.com/dotnet/sdk:10.0.300@sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6 AS build
WORKDIR /src

COPY ["NuGet.Config", "./"]
COPY ["global.json", "./"]
COPY ["backend/WaterFlex.SaltMonitor.slnx", "backend/WaterFlex.SaltMonitor.slnx"]
COPY ["backend/Directory.Build.props", "backend/Directory.Build.props"]
COPY ["backend/src/WaterFlex.SaltMonitor.Domain/WaterFlex.SaltMonitor.Domain.csproj", "backend/src/WaterFlex.SaltMonitor.Domain/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Ingestion/WaterFlex.SaltMonitor.Ingestion.csproj", "backend/src/WaterFlex.SaltMonitor.Ingestion/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Provisioning/WaterFlex.SaltMonitor.Provisioning.csproj", "backend/src/WaterFlex.SaltMonitor.Provisioning/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Rules/WaterFlex.SaltMonitor.Rules.csproj", "backend/src/WaterFlex.SaltMonitor.Rules/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Api/WaterFlex.SaltMonitor.Api.csproj", "backend/src/WaterFlex.SaltMonitor.Api/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Infrastructure/WaterFlex.SaltMonitor.Infrastructure.csproj", "backend/src/WaterFlex.SaltMonitor.Infrastructure/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Operations/WaterFlex.SaltMonitor.Operations.csproj", "backend/src/WaterFlex.SaltMonitor.Operations/"]
COPY ["backend/src/WaterFlex.SaltMonitor.Worker/WaterFlex.SaltMonitor.Worker.csproj", "backend/src/WaterFlex.SaltMonitor.Worker/"]
COPY ["backend/tests/WaterFlex.SaltMonitor.Tests/WaterFlex.SaltMonitor.Tests.csproj", "backend/tests/WaterFlex.SaltMonitor.Tests/"]

RUN dotnet restore backend/WaterFlex.SaltMonitor.slnx --configfile NuGet.Config

COPY ["backend", "backend/"]

RUN dotnet publish backend/src/WaterFlex.SaltMonitor.Api/WaterFlex.SaltMonitor.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:207cc51496778557731c81ff670333d8ade4a4fec22768fd1be8e78474a84ecf
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=4 \
    CMD curl --fail --silent --show-error http://localhost:8080/health/live > /dev/null || exit 1

ENTRYPOINT ["dotnet", "WaterFlex.SaltMonitor.Api.dll"]
