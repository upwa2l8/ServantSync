# Multi-stage Dockerfile for the .NET 9 Blazor Server app, optimized
# for Azure Container Apps. The build context is the repo root;
# .dockerignore keeps it from carrying tests/, .git/, etc. into the
# image.
#
# Architecture notes:
# - Stage 1 (sdk): restore + publish a framework-dependent Release build
#   so the runtime stage inherits only the published output. The .NET 9
#   ASP.NET Core runtime is in the aspnet:9.0 base image, so a
#   self-contained publish would only inflate the image with redundant
#   binaries.
# - Stage 2 (aspnet): official mcr.microsoft.com/dotnet/aspnet:9.0 runs
#   as non-root (USER $APP_UID = 1654) by default. We keep that and
#   EXPOSE 8080 -- Azure Container Apps' default targetPort is 8080
#   for Linux containers, and ACA terminates TLS at its ingress so the
#   app itself only needs to listen on plain HTTP.
# - The uploads symlink: Program.cs does
#     Path.Combine(app.Environment.WebRootPath, "uploads", "training")
#   to derive the uploads root. We mount a persistent Azure Files
#   share at /data and symlink the uploads subdir into wwwroot so the
#   existing code keeps working without any C# changes. The Backup
#   directory is set via env var to /data/backups (NOT under wwwroot --
#   backups must NEVER be served as static files; see DEPLOY.md
#   "Critical" note + the SECURITY NOTE in SqliteBackupService.cs).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the csproj first so `dotnet restore` is cached as a
# separate layer. Re-runs of the build skip the restore step when the
# csproj hasn't changed.
COPY ["ServantSync.csproj", "./"]
RUN dotnet restore

# Now copy the rest of the source and publish.
COPY . .
RUN dotnet publish -c Release -o /app/publish \
    # Trim Development env override from the image. Production env
    # values come from the Container App's env-var wiring (see
    # SETUP.md), not from appsettings.Development.json.
    && rm -f /app/publish/appsettings.Development.json \
    # Strip .pdb files -- debug symbols are useless in the image and
    # only bloat the push to ACR. Mirrors the old zip-deploy filter
    # (deploy.yml "x *.pdb").
    && find /app/publish -name "*.pdb" -type f -delete

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy only the published output from the build stage. The runtime
# base image already has the .NET 9 ASP.NET Core runtime installed.
COPY --from=build /app/publish .

# Set up the uploads symlink. The /data volume is mounted by Azure
# Container Apps at runtime (see SETUP.md "Mount the Azure Files
# share on the Container App"). /data/uploads will exist on the share
# at first deploy; if it doesn't, the symlink target doesn't exist
# yet, and Directory.CreateDirectory(uploadsRoot) in Program.cs will
# create it on first boot (symlinks are followed on directory ops).
# This pattern lets us keep Program.cs's hardcoded
# `Path.Combine(WebRootPath, "uploads", "training")` path unchanged.
RUN mkdir -p /app/wwwroot/uploads \
    && rm -rf /app/wwwroot/uploads/training \
    && ln -s /data/uploads /app/wwwroot/uploads/training

# The .NET 9 base image's default USER is $APP_UID = 1654 (non-root
# `app` user), which the ACA runtime expects for non-root Linux
# containers. We re-state it explicitly so it's obvious from reading
# the Dockerfile and survives any future base-image changes.
USER $APP_UID

# ACA ingress expects 8080 by default on Linux. The HTTPS -> HTTP
# redirect is handled by ACA's ingress; the app stays on plain HTTP.
# ASPNETCORE_URLS is also pinned via env var in SETUP.md so the
# framework's default 5000/5001 don't take over.
EXPOSE 8080

# Default env values for things the user almost certainly wants the
# same in every environment. The Container App's env-var wiring
# (SETUP.md) overrides these per-environment.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    # ForwardedHeaders middleware is auto-added when this is set.
    # Required so app.UseHttpsRedirection() (and SignalR's wss://
    # upgrade URL) work correctly behind ACA's TLS-terminating ingress.
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    # Persistent DB on the Azure Files share mounted at /data.
    ConnectionStrings__DefaultConnection="Data Source=/data/servantsync.db"

ENTRYPOINT ["dotnet", "ServantSync.dll"]
