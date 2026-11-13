# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/SchoolAdmission.Api.csproj backend/
RUN dotnet restore backend/SchoolAdmission.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/SchoolAdmission.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN useradd --create-home --uid 10001 appuser
COPY --from=build --chown=appuser:appuser /app/publish ./
USER appuser

EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
