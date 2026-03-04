FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Enigma/Enigma.csproj Enigma/
COPY Enigma.Client/Enigma.Client.csproj Enigma.Client/
RUN dotnet restore Enigma/Enigma.csproj

COPY . .
RUN dotnet publish Enigma/Enigma.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

CMD ["sh", "-c", "dotnet Enigma.dll --urls http://0.0.0.0:${PORT:-10000}"]
