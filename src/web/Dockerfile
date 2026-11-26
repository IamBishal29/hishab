# syntax=docker/dockerfile:1
# Hishab web app: a static build served by nginx, which also proxies /api to the API container
# (one origin, so the HttpOnly refresh cookie works as in development). Build context: src/web.

# --- build: node and dev dependencies, never shipped --------------------------------------------
FROM node:24-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# --- runtime: unprivileged nginx (port 8080, not root) -----------------------------------------
FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
