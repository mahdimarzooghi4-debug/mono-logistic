FROM node:24-alpine AS console-build
WORKDIR /console
COPY ["src/Mono.OperationsConsole/package.json", "./package.json"]
COPY ["src/Mono.OperationsConsole/package-lock.json", "./package-lock.json"]
RUN npm ci
COPY src/Mono.OperationsConsole/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Mono.Api/Mono.Api.csproj", "src/Mono.Api/"]
RUN dotnet restore "src/Mono.Api/Mono.Api.csproj"
COPY ["tools/Mono.DbMigrator/Mono.DbMigrator.csproj", "tools/Mono.DbMigrator/"]
RUN dotnet restore "tools/Mono.DbMigrator/Mono.DbMigrator.csproj"

COPY . .
COPY --from=console-build /console/dist/ ./src/Mono.Api/wwwroot/
WORKDIR /src/src/Mono.Api
RUN dotnet publish "Mono.Api.csproj" --configuration Release --no-restore --output /app/publish /p:UseAppHost=false
WORKDIR /src
RUN dotnet publish "tools/Mono.DbMigrator/Mono.DbMigrator.csproj" --configuration Release --no-restore --output /app/migrator /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/migrator /app/migrator
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "if [ \"${MONO_AUTO_MIGRATE:-false}\" = \"true\" ]; then dotnet /app/migrator/Mono.DbMigrator.dll || exit $?; fi; export ASPNETCORE_URLS=\"http://0.0.0.0:${PORT:-10000}\"; exec dotnet Mono.Api.dll"]
