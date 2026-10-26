FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Ohmventory.Api/Ohmventory.Api.csproj src/Ohmventory.Api/
RUN dotnet restore src/Ohmventory.Api/Ohmventory.Api.csproj
COPY src/ src/
RUN dotnet publish src/Ohmventory.Api/Ohmventory.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__ImageDirectory=/data/images
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ohmventory.Api.dll"]
