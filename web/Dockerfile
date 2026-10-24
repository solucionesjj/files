# syntax=docker/dockerfile:1
# Local/dev image for the Angular portal. Production serves the built SPA from Caddy instead
# (see deploy/Caddyfile), so this image is only used by the local Docker Compose stack.

# --- build ------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# --- runtime ----------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
