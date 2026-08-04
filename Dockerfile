#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# .NET 8+ container images default Kestrel to http://+:8080 (NOT port 80).
# Expose 8080 and point your DigitalOcean service/health-check at 8080. TLS is
# terminated by DigitalOcean, so no in-container HTTPS.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
# Run as the non-root user shipped in the .NET images (production hardening).
USER $APP_UID

# ---------------------------------------------------------------------------
# Stage 1 — build the Angular client.
# angular.json has outputPath { base: "../wwwroot", browser: "" }, and this stage
# runs in /client, so `npm run build` emits the app to /wwwroot.
# This means deploys always ship a FRESH UI; you no longer have to commit wwwroot.
# ---------------------------------------------------------------------------
FROM node:22 AS clientbuild
WORKDIR /client
COPY Client/package*.json ./
RUN npm install --legacy-peer-deps
COPY Client/ ./
RUN npm run build

# ---------------------------------------------------------------------------
# Stage 2 — build the .NET server.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["MG.Server.csproj", "."]
RUN dotnet restore "./MG.Server.csproj"
COPY . .
RUN dotnet build "MG.Server.csproj" -c Release -o /app/build

FROM build AS publish
# Replace any committed wwwroot with the freshly built Angular app before publishing.
RUN rm -rf /src/wwwroot
COPY --from=clientbuild /wwwroot /src/wwwroot
RUN dotnet publish "MG.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MG.Server.dll"]
