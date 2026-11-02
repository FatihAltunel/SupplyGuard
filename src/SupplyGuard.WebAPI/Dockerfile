FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/SupplyGuard.Domain/SupplyGuard.Domain.csproj src/SupplyGuard.Domain/
COPY src/SupplyGuard.Application/SupplyGuard.Application.csproj src/SupplyGuard.Application/
COPY src/SupplyGuard.Infrastructure/SupplyGuard.Infrastructure.csproj src/SupplyGuard.Infrastructure/
COPY src/SupplyGuard.WebAPI/SupplyGuard.WebAPI.csproj src/SupplyGuard.WebAPI/
RUN dotnet restore src/SupplyGuard.WebAPI/SupplyGuard.WebAPI.csproj

COPY src/ src/
RUN dotnet publish src/SupplyGuard.WebAPI/SupplyGuard.WebAPI.csproj \
    --configuration Release --output /out/api --no-restore /p:UseAppHost=false

FROM build AS migration-build
RUN dotnet tool install --tool-path /tools dotnet-ef --version 8.0.11
# This design-time value is only used to build the bundle; no database is contacted.
RUN SUPPLYGUARD_CONNECTION_STRING="Host=localhost;Database=unused;Username=unused;Password=unused" \
    /tools/dotnet-ef migrations bundle \
    --project src/SupplyGuard.Infrastructure/SupplyGuard.Infrastructure.csproj \
    --startup-project src/SupplyGuard.WebAPI/SupplyGuard.WebAPI.csproj \
    --configuration Release --no-build --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
USER app

FROM runtime AS migrations
COPY --from=migration-build --chown=app:app /out/efbundle ./efbundle
COPY --from=build --chown=app:app /out/api/appsettings.json ./appsettings.json
ENTRYPOINT ["/app/efbundle"]

FROM runtime AS api
COPY --from=build --chown=app:app /out/api/ ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "SupplyGuard.WebAPI.dll"]
