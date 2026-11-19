FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SupplyGuard.Domain/SupplyGuard.Domain.csproj src/SupplyGuard.Domain/
COPY src/SupplyGuard.Application/SupplyGuard.Application.csproj src/SupplyGuard.Application/
COPY src/SupplyGuard.Infrastructure/SupplyGuard.Infrastructure.csproj src/SupplyGuard.Infrastructure/
COPY src/SupplyGuard.BackgroundWorker/SupplyGuard.BackgroundWorker.csproj src/SupplyGuard.BackgroundWorker/
RUN dotnet restore src/SupplyGuard.BackgroundWorker/SupplyGuard.BackgroundWorker.csproj
COPY src/ src/
RUN dotnet publish src/SupplyGuard.BackgroundWorker/SupplyGuard.BackgroundWorker.csproj \
    --configuration Release --output /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS worker
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
USER app
ENTRYPOINT ["dotnet", "SupplyGuard.BackgroundWorker.dll"]
