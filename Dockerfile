#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

#COPY --from=mcr.microsoft.com/dotnet/aspnet:6.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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
RUN dotnet publish "SW.Bitween.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SW.Bitween.Web.dll"]