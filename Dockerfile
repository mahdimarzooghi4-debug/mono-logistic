FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Mono.Api/Mono.Api.csproj", "src/Mono.Api/"]
RUN dotnet restore "src/Mono.Api/Mono.Api.csproj"

COPY . .
WORKDIR /src/src/Mono.Api
RUN dotnet publish "Mono.Api.csproj" --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_URLS=\"http://0.0.0.0:${PORT:-10000}\"; exec dotnet Mono.Api.dll"]
