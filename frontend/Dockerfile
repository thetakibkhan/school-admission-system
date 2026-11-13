# syntax=docker/dockerfile:1

# Build stage. The Vite app lives in frontend/; this Dockerfile is written so it works once
# frontend/package.json exists (the frontend task adds the source).
FROM node:22-alpine AS build
WORKDIR /app

COPY frontend/package*.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
