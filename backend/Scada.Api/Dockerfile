FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Scada.Api/Scada.Api.csproj Scada.Api/
COPY Scada.Core/Scada.Core.csproj Scada.Core/
COPY Scada.Security/Scada.Security.csproj Scada.Security/
COPY Scada.Data/Scada.Data.csproj Scada.Data/
COPY Scada.Gateway/Scada.Gateway.csproj Scada.Gateway/
COPY Scada.Monitoring/Scada.Monitoring.csproj Scada.Monitoring/
COPY Scada.Drivers/Scada.Drivers.csproj Scada.Drivers/
RUN dotnet restore Scada.Api/Scada.Api.csproj

COPY . .
RUN dotnet publish Scada.Api/Scada.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Scada.Api.dll"]
