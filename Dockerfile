# AirSim, public build: static frontend + read-only API + precomputed runs.
# Adjust the image tags to the TargetFramework of the .NET projects and the project path below if it differs.
# Build:  docker build -t airsim .
# Run:    docker run --rm -p 8080:8080 -v $PWD/precomputed:/data/precomputed:ro airsim        (or: docker compose up --build)

# ---- 1. frontend ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---- 2. server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Sim.Server -c Release -o /out

# ---- 3. (optional) precomputed runs generated during the build ----
# Used only by `--target standalone`. The sensitivity analysis (sens) multiplies the time: use a small value or 0 here.
# The default image does not need this stage: it mounts a `precomputed` folder generated beforehand (see docker-compose.yml).
FROM build AS precompute
ARG PRECOMPUTE_ARGS="sens=4"
RUN dotnet /out/Sim.Server.dll precompute scenario=all seed=1 maxmb=30 out=/precomputed ${PRECOMPUTE_ARGS}

# ---- 4. runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
COPY --from=build /out ./
COPY --from=web /web/dist ./wwwroot
COPY scenarios ./scenarios
COPY data ./data

# Public mode: no live simulation, no custom scenarios, no writes to disk (see README).
ENV Sim__Mode=public \
    Sim__ScenariosDir=scenarios \
    Sim__CatalogDir=data/catalog \
    Sim__CountriesDir=data/countries \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sim.Server.dll"]

# `docker build --target standalone`: the precomputed runs are generated and baked into the image.
FROM base AS standalone
COPY --from=precompute /precomputed ./precomputed
ENV Sim__PrecomputedDir=precomputed
USER $APP_UID

# Default image: the precomputed runs are mounted at /data/precomputed (they are large and not part of the repository).
FROM base AS final
ENV Sim__PrecomputedDir=/data/precomputed
USER $APP_UID
