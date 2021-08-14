#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ["client-api-test-service-dotnet.csproj", ""]
RUN dotnet restore "./client-api-test-service-dotnet.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "client-api-test-service-dotnet.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "client-api-test-service-dotnet.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "client-api-test-service-dotnet.dll"]

### build
# docker build -t client-api-test-service-dotnet:v1.0 .

### run Windows
# docker run --rm -it -p 5002:80 -e IssuanceRequestConfigFile=./requests/issuance_request_config_v2.json -e PresentationRequestConfigFile=./requests/presentation_request_config_v2.json client-api-test-service-dotnet:v1.0

### browse
# http://localhost