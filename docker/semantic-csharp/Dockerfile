# codescan/semantic-csharp — Roslyn-backed semantic analyzer image.
# Phase 1 PoC. Contract: stdout NDJSON, /work read-only mount.

# -------- build stage --------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SemanticAnalyzer.csproj ./
RUN dotnet restore SemanticAnalyzer.csproj
COPY . .
RUN dotnet publish SemanticAnalyzer.csproj -c Release -o /out --no-restore

# -------- runtime stage --------
FROM mcr.microsoft.com/dotnet/sdk:9.0
LABEL org.opencontainers.image.title="codescan-semantic-csharp"
LABEL org.opencontainers.image.description="Roslyn-backed semantic analyzer for C# (Phase 1 PoC)"
LABEL org.opencontainers.image.source="https://github.com/psmon/CodeScan"

# The SDK image is kept at runtime because MSBuildLocator needs an MSBuild
# install to load .sln/.csproj. Slimming to runtime-only would break workspace
# resolution. Phase 1B will split build (Roslyn from source) and runtime
# (msbuild-only) if image size becomes a concern.
WORKDIR /app
COPY --from=build /out /app

WORKDIR /work
ENTRYPOINT ["dotnet", "/app/semantic-csharp.dll"]
