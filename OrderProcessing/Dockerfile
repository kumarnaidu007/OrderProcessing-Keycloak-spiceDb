FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["OrderProcessing/OrderProcessing.csproj", "OrderProcessing/"]
RUN dotnet restore "OrderProcessing/OrderProcessing.csproj"

COPY . .
WORKDIR "/src/OrderProcessing"
RUN dotnet publish "OrderProcessing.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderProcessing.dll"]
