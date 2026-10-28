FROM node:22-bookworm-slim AS frontend
WORKDIR /repo/src/Desarrollo.Web/ClientApp
COPY src/Desarrollo.Web/ClientApp/package*.json ./
RUN npm ci
COPY src/Desarrollo.Web/ClientApp/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Desarrollo.slnx ./
COPY src/ ./src/
COPY --from=frontend /repo/src/Desarrollo.Web/wwwroot/dist/ ./src/Desarrollo.Web/wwwroot/dist/
RUN dotnet publish src/Desarrollo.Web/Desarrollo.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__RootPath=/var/lib/crm/files \
    DataProtection__KeyRingPath=/var/lib/crm/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Desarrollo.Web.dll"]
