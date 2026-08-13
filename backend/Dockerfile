FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY ["NuGet.Config", "./"]
COPY ["global.json", "./"]
COPY ["backend", "backend/"]

RUN dotnet restore backend/WaterFlex.SaltMonitor.slnx --configfile NuGet.Config

RUN dotnet publish backend/src/WaterFlex.SaltMonitor.Api/WaterFlex.SaltMonitor.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=4 \
    CMD curl --fail --silent --show-error http://localhost:8080/health > /dev/null || exit 1

ENTRYPOINT ["dotnet", "WaterFlex.SaltMonitor.Api.dll"]
