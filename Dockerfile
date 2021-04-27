FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-dotnet
WORKDIR /app
COPY rextester-proxy.fsproj ./rextester-proxy.fsproj
RUN dotnet restore -r linux-x64

COPY Program.fs ./Program.fs
RUN dotnet publish -r linux-x64 -c Release -o out --no-restore --verbosity normal

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:5.0

EXPOSE 5000

WORKDIR /app
COPY --from=build-dotnet /app/out .

ENTRYPOINT ["dotnet", "rextester-proxy.dll"]
