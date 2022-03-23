#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["SW.Infolink.Web/SW.Infolink.Web.csproj", "SW.Infolink.Web/"]
COPY ["SW.Infolink.Api/SW.Infolink.Api.csproj", "SW.Infolink.Api/"]
COPY ["SW.Infolink.Sdk/SW.Infolink.Sdk.csproj", "SW.Infolink.Sdk/"]
COPY ["SW.Infolink.MySql/SW.Infolink.MySql.csproj", "SW.Infolink.MySql/"]
COPY ["SW.Infolink.MsSql/SW.Infolink.MsSql.csproj", "SW.Infolink.MsSql/"]
RUN dotnet restore "SW.Infolink.Web/SW.Infolink.Web.csproj"
COPY . .
WORKDIR "/src/SW.Infolink.Web"
RUN dotnet build "SW.Infolink.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SW.Infolink.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SW.Infolink.Web.dll"]