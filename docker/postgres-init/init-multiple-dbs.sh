#!/bin/bash
set -e

# Bu script, PostgreSQL container'ı ilk kez ayağa kalktığında
# otomatik olarak çalışır ve her mikroservis için ayrı bir database oluşturur.

function create_database() {
	local database=$1
	echo "Creating database: $database"
	psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
	    CREATE DATABASE $database;
	EOSQL
}

if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
	echo "Multiple databases creation requested: $POSTGRES_MULTIPLE_DATABASES"
	for db in $(echo $POSTGRES_MULTIPLE_DATABASES | tr ',' ' '); do
		create_database $db
	done
	echo "Multiple databases created"
fi
