#!/bin/bash
set -e

cleanup() {
  echo "Something went wrong"
  docker stop "$migration_name" >/dev/null 2>&1
  docker rm "$migration_name" >/dev/null 2>&1
  
}
trap cleanup ERR

echo "Bitween Auto Ef Core Migrator"
echo "This products in in beta please report any bugs you find"

if [ "$#" -ne 1 ]; then
  echo "Usage: ./migratedb.sh <name>"
  exit 1
fi
docker info >/dev/null 2>&1
if [ $? -ne 0 ]; then
  echo "Error: Docker daemon is not running. Please start Docker and try again."
  exit 1
fi
migration_name="$1"
echo "Migration Name: $migration_name"

echo "Creating a MySql db using docker"
docker run --name "$migration_name" -e MYSQL_ROOT_PASSWORD=root_password -p 3306:3306 -d mysql:latest >/dev/null 2>&1

connection_string="Server=root:root_password@localhost:3306/$migration_name"
echo $connection_string


echo "Migrating PgSql..."
cd SW.Infolink.PgSql
export InfoLink__DatabaseType=PgSql
dotnet ef migrations add $migration_name -s ../SW.Infolink.Web
cd ..

echo "Migrating MySql..."
cd SW.InfoLink.MySql
export InfoLink__DatabaseType=MySql
export ConnectionStrings__InfolinkDb=$connection_string
dotnet ef migrations add  $migration_name  -s ../SW.Infolink.Web --context SW.Infolink.InfolinkDbContext
cd ..


echo "Migrating MsSql..."
cd SW.InfoLink.MsSql
export InfoLink__DatabaseType=MsSql
dotnet ef migrations add  $migration_name  -s ../SW.Infolink.Web --context SW.Infolink.InfolinkDbContext

docker stop "$migration_name" >/dev/null 2>&1
docker rm "$migration_name" >/dev/null 2>&1
cd ..
