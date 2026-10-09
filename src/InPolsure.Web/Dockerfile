# syntax=docker/dockerfile:1
# InPolsure.Web container image (docs/labs/lab-01-walking-skeleton.md §6.6, ADR-0004).
# Build context is the repository root:
#   docker build -f src/InPolsure.Web/Dockerfile -t inpolsure-web:local .
# Base images are pinned to a patch tag plus digest; Dependabot (docker ecosystem) updates both.

# ---- build ---------------------------------------------------------------------------------
# Runs on the build host's architecture; the output is portable IL (framework-dependent,
# no RID), so the same publish output runs on amd64 and arm64 runtime images.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /repo

# Restore as a separate, cached layer: only the files that affect restore are copied first.
# Tests are not in the build context (.dockerignore), so the Web project is restored, not the .slnx.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/InPolsure.SharedKernel/InPolsure.SharedKernel.csproj src/InPolsure.SharedKernel/
COPY src/InPolsure.Ui/InPolsure.Ui.csproj src/InPolsure.Ui/
COPY src/InPolsure.Web/InPolsure.Web.csproj src/InPolsure.Web/
RUN dotnet restore src/InPolsure.Web/InPolsure.Web.csproj

# Analyzer/code-style settings affect compilation, not restore.
COPY .editorconfig ./
COPY src/ src/

# Git commit SHA appended to InformationalVersion (0.1.0+<sha>, docs/DELIVERY.md §9; OTel service.version).
# .git is not in the build context, so the SHA is passed in. Empty -> plain 0.1.0.
# Set by CI (T-01.11): --build-arg SOURCE_REVISION_ID=${{ github.sha }}
ARG SOURCE_REVISION_ID=

# UseAppHost=false: the runtime image starts the app with `dotnet`, no native host needed.
# PublishDocumentationFiles=false: keep XML doc files out of the image (T-01.8 moves this
# into Directory.Build.props; passing it here as well is harmless).
# ContinuousIntegrationBuild=true: deterministic source paths in PDBs.
RUN dotnet publish src/InPolsure.Web/InPolsure.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:PublishDocumentationFiles=false \
        -p:ContinuousIntegrationBuild=true \
        -p:SourceRevisionId="$SOURCE_REVISION_ID"

# ---- runtime -------------------------------------------------------------------------------
# Chiseled Ubuntu: no shell, no package manager, non-root by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled@sha256:9651fa59abcdf177c30392cb44a820605ca5d618429ab37acbf6e7c644510b02 AS final
WORKDIR /app

# Image default is 8080; stated explicitly because the platform probes and ingress rely on it.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build --chown=root:root /app/publish ./

# Non-root user provided by the base image (APP_UID=1654); explicit so that
# `docker image inspect --format '{{.Config.User}}'` shows it.
USER $APP_UID

# No HEALTHCHECK: Container Apps uses its own probes and the chiseled image has no shell or curl.
ENTRYPOINT ["dotnet", "InPolsure.Web.dll"]
