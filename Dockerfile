# syntax=docker/dockerfile:1.7@sha256:a57df69d0ea827fb7266491f2813635de6f17269be881f696fbfdf2d83dda33e

# Base images are pinned to immutable multi-arch manifest digests. Refresh these
# intentionally after patch/image scanning.
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.203-noble@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.7-noble-chiseled-extra@sha256:b4855d6d1c557c19a7c7165b354a138b47c17b29669ad0b3b22fb046a1b84fd8

# ----- Stage 1: build & publish -----
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

# Copy CPM/build props first so layer caching survives source-only changes.
COPY .editorconfig Directory.Build.props Directory.Packages.props TodoApp.slnx ./
COPY src/TodoApp.Api/TodoApp.Api.csproj src/TodoApp.Api/
COPY src/TodoApp.Api.Tests/TodoApp.Api.Tests.csproj src/TodoApp.Api.Tests/

# Restore only the API project for the runtime image.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TodoApp.Api/TodoApp.Api.csproj

# Now copy sources.
COPY src/ src/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TodoApp.Api/TodoApp.Api.csproj \
        -c Release \
        --no-restore \
        /p:UseAppHost=false \
        -o /app/out

# The final image is chiseled/distroless, so it has no shell for `RUN mkdir`.
# Prepare writable mount targets in the build stage and copy them into place.
RUN mkdir -p /writable/app/.aspnet/keys /writable/var/lib/todoapp /writable/tmp/todoapp

# ----- Stage 2: runtime -----
FROM ${DOTNET_ASPNET_IMAGE} AS runtime
ARG APP_UID=1654
WORKDIR /app

# ASP.NET binds to 5000 inside the container.
ENV ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_EnableDiagnostics_IPC=0 \
    DOTNET_EnableDiagnostics_Debugger=0 \
    DOTNET_EnableDiagnostics_Profiler=0 \
    TMPDIR=/tmp/todoapp
EXPOSE 5000

# Keep the published app root read-only to the process. Only dev keys, SQLite
# state, and temp files should be writable at runtime.
COPY --from=build --chown=${APP_UID}:${APP_UID} /writable/app/.aspnet/ /app/.aspnet/
COPY --from=build --chown=${APP_UID}:${APP_UID} /writable/var/lib/todoapp/ /var/lib/todoapp/
COPY --from=build --chown=${APP_UID}:${APP_UID} /writable/tmp/todoapp/ /tmp/todoapp/
COPY --from=build /app/out ./

USER ${APP_UID}

ENTRYPOINT ["dotnet", "TodoApp.Api.dll"]
