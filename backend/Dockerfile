# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MyShop.slnx ./
COPY src/MyShop.Api/MyShop.Api.csproj src/MyShop.Api/
COPY tests/MyShop.Tests/MyShop.Tests.csproj tests/MyShop.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/MyShop.Api -c Release -o /app --no-restore /p:UseAppHost=false

# Runtime "chiseled": không shell, không package manager, chạy non-root → giảm bề mặt tấn công.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "MyShop.Api.dll"]
