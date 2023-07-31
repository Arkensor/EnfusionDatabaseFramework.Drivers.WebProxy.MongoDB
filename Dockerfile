FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine AS base
WORKDIR /app
EXPOSE 8008

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY ["EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB/EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB.csproj", "EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB/"]
RUN dotnet restore "EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB/EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB.csproj"
COPY . .
WORKDIR "/src/EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB"
RUN dotnet build "EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB.dll"]
