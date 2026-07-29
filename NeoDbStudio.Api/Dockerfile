FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["NeoDbStudio.Api/NeoDbStudio.Api.csproj", "NeoDbStudio.Api/"]
COPY ["NeoDbStudio.Shared/NeoDbStudio.Shared.csproj", "NeoDbStudio.Shared/"]
RUN dotnet restore "NeoDbStudio.Api/NeoDbStudio.Api.csproj"
COPY . .
WORKDIR "/src/NeoDbStudio.Api"
RUN dotnet build "NeoDbStudio.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NeoDbStudio.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NeoDbStudio.Api.dll"]
