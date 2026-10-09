#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

COPY --from=mcr.microsoft.com/dotnet/aspnet:6.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

# Python, for adapters written in it: SW.Serverless starts them with python3. Ubuntu 24.04's 3.12,
# the oldest the Python SDK supports, patched with every rebuild of the image. Adapters bring their
# own packages, vendored, so nothing else is installed.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080
EXPOSE 443

# Build the admin UI (SPA) in its own stage; output lands in SW.Bitween.Web/wwwroot
FROM node:22-alpine AS ui-build
WORKDIR /src/SW.Bitween.Web/ClientApp
COPY ["SW.Bitween.Web/ClientApp/package.json", "SW.Bitween.Web/ClientApp/yarn.lock", "./"]
RUN yarn install --frozen-lockfile
COPY ["SW.Bitween.Web/ClientApp/", "./"]
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SW.Bitween.Web/SW.Bitween.Web.csproj", "SW.Bitween.Web/"]
COPY ["SW.Bitween.Api/SW.Bitween.Api.csproj", "SW.Bitween.Api/"]
COPY ["SW.Bitween.Sdk/SW.Bitween.Sdk.csproj", "SW.Bitween.Sdk/"]
COPY ["SW.Bitween.MySql/SW.Bitween.MySql.csproj", "SW.Bitween.MySql/"]
COPY ["SW.Bitween.MsSql/SW.Bitween.MsSql.csproj", "SW.Bitween.MsSql/"]
RUN dotnet restore "SW.Bitween.Web/SW.Bitween.Web.csproj"
COPY . .
WORKDIR "/src/SW.Bitween.Web"
RUN dotnet build "SW.Bitween.Web.csproj" -c Release -o /app/build

FROM build AS publish
# UI is built in the ui-build stage; the SDK image has no node
RUN dotnet publish "SW.Bitween.Web.csproj" -c Release -o /app/publish -p:SkipClientBuild=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=ui-build /src/SW.Bitween.Web/wwwroot ./wwwroot
# Run as the image's built-in non-root user. The one place Bitween writes under /app is the cache
# of downloaded adapter packages (SW.Serverless AdapterLocalPath, ./adapters by default), so that
# directory alone is handed over. It is a cache: packages are fetched again from storage if it is
# empty, so a fresh container needs no volume for it.
RUN mkdir -p /app/adapters && chown $APP_UID /app/adapters
USER $APP_UID
ENTRYPOINT ["dotnet", "SW.Bitween.Web.dll"]
