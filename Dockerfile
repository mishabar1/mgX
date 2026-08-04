#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# NOTE (.NET 10 upgrade): base images bumped 7.0 -> 10.0.
# The .NET 8+ container images default Kestrel to http://+:8080 (NOT port 80).
# Expose 8080 and point your DigitalOcean service/health-check at 8080. TLS is
# terminated by DigitalOcean (App Platform / load balancer), so no in-container HTTPS.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
# Run as the non-root user shipped in the .NET images (production hardening).
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["MG.Server.csproj", "."]
RUN dotnet restore "./MG.Server.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "MG.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MG.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MG.Server.dll"]
