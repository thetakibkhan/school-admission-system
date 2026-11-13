# syntax=docker/dockerfile:1

# Build stage. The Vite app lives in frontend/; this Dockerfile is written so it works once
# frontend/package.json exists (the frontend task adds the source).
FROM node:22-alpine AS build
WORKDIR /app

COPY frontend/package*.json ./
# The frontend task may ship only package.json, so fall back to npm install when no lock file exists.
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

COPY frontend/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
