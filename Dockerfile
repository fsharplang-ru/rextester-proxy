FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-dotnet
WORKDIR /app
# Copy csproj and restore as distinct layers
COPY src/rextester-proxy.fsproj ./rextester-proxy.fsproj
RUN dotnet restore -r linux-x64

# Then build&test app
COPY src/Program.fs ./Program.fs
RUN dotnet test -c Release -r linux-x64 -o out --no-restore --verbosity normal
# Publish
RUN dotnet publish -r linux-x64 -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:5.0

EXPOSE 5000

WORKDIR /app
COPY --from=build-dotnet /app/out .

ENTRYPOINT ["dotnet", "rextester-proxy.dll"]
