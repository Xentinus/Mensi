# 1) Kliens: Nuxt statikus generálás
FROM node:22-alpine AS client
WORKDIR /src/mensi.client
COPY mensi.client/package.json mensi.client/package-lock.json ./
RUN npm ci
COPY mensi.client/ ./
RUN npm run generate

# 2) Backend publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Mensi.Core/Mensi.Core.csproj Mensi.Core/
COPY Mensi.Core/packages.lock.json Mensi.Core/
COPY Mensi.Server/Mensi.Server.csproj Mensi.Server/
COPY Mensi.Server/packages.lock.json Mensi.Server/
RUN dotnet restore Mensi.Server/Mensi.Server.csproj --locked-mode
COPY Mensi.Core/ Mensi.Core/
COPY Mensi.Server/ Mensi.Server/
RUN dotnet publish Mensi.Server/Mensi.Server.csproj -c Release -o /app --no-restore

# 3) Futtató image: aspnet + statikus kliens a wwwroot-ban
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=client /src/mensi.client/.output/public wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mensi.Server.dll"]
