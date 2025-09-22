FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

RUN apt-get update && apt-get install -y clang zlib1g-dev binutils-aarch64-linux-gnu

WORKDIR /app

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT [ "./PaymentGateway" ]
