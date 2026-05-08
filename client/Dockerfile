# Base images are pinned to immutable multi-arch manifest digests. Refresh
# these intentionally after patch/image scanning. Both digests are the index
# (multi-arch) manifest, so the pin works for amd64 and arm64 builds alike.
ARG NODE_BUILD_IMAGE=node:22-alpine@sha256:8ea2348b068a9544dae7317b4f3aafcdc032df1647bb7d768a05a5cad1a7683f
ARG NGINX_RUNTIME_IMAGE=nginxinc/nginx-unprivileged:1.27-alpine@sha256:65e3e85dbaed8ba248841d9d58a899b6197106c23cb0ff1a132b7bfe0547e4c0

# ----- Stage 1: build -----
FROM ${NODE_BUILD_IMAGE} AS build
WORKDIR /app

# Copy lockfile first so layer caching survives source-only changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ----- Stage 2: runtime -----
# nginxinc/nginx-unprivileged runs as UID 101 and listens on 8080 by default,
# so no USER override is required.
FROM ${NGINX_RUNTIME_IMAGE} AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/ /usr/share/nginx/html/

EXPOSE 8080
