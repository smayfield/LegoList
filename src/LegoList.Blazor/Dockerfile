# Build from the repo root:  docker build -f src/LegoList.Blazor/Dockerfile .
# The SDK stage runs on the build machine's architecture and cross-publishes
# for the target (e.g. linux/arm64), so no emulation is needed.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/LegoList.Blazor/LegoList.Blazor.csproj LegoList.Blazor/
RUN dotnet restore LegoList.Blazor/LegoList.Blazor.csproj -a $TARGETARCH
COPY src/LegoList.Blazor/ LegoList.Blazor/
RUN dotnet publish LegoList.Blazor/LegoList.Blazor.csproj -c Release -a $TARGETARCH --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Data Protection keys live here (mounted as a volume in production). Creating the
# directory with the app user's ownership lets a new named volume inherit it.
RUN mkdir /keys && chown $APP_UID /keys
USER $APP_UID
# ASP.NET Core images listen on port 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "LegoList.Blazor.dll"]
