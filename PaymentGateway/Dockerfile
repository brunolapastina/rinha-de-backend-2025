FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

RUN apt-get update && apt-get install -y clang zlib1g-dev binutils-aarch64-linux-gnu

WORKDIR /App

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
#RUN dotnet publish --os linux --arch x64 -o out
RUN dotnet publish -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
WORKDIR /PaymentGateway
COPY --from=build /App/out .
ENTRYPOINT ["./PaymentGateway"]