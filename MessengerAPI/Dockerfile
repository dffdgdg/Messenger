FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["MessengerAPI/MessengerAPI.csproj", "MessengerAPI/"]
COPY ["MessengerShared/MessengerShared.csproj", "MessengerShared/"]
RUN ["dotnet", "restore", "MessengerAPI/MessengerAPI.csproj"]

COPY ["MessengerAPI/", "MessengerAPI/"]
COPY ["MessengerShared/", "MessengerShared/"]
RUN ["dotnet", "publish", "MessengerAPI/MessengerAPI.csproj", "-c", "Release", "-o", "/app/publish", "/p:UseAppHost=false"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

RUN addgroup --system appgroup \
    && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

USER appuser

ENTRYPOINT ["dotnet", "MessengerAPI.dll"]